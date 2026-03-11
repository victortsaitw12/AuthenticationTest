# JWT 認證系統教學 - 第八部分：深入角色授權（Role-Based Authorization）

## 專案概述

本分支在 Branch 6 的角色基礎上，新增：

1. 支援用戶擁有**多重角色**
2. `[Authorize(Roles)]` 的 OR 與 AND 邏輯
3. `User.IsInRole()` 程式化角色檢查
4. 角色管理 API（授予 / 撤銷）

---

> **前置知識：** 請先完成 `7_Refresh_Token`。

---

## 第十一步：多重角色支援

### 問題：單一角色欄位的限制

Branch 6 的 `User.Role` 是單一字串，一個用戶只能有一個角色。現實中用戶可能同時是 `Manager` 和 `Auditor`。

### 解法：逗號分隔多重角色

資料庫欄位不變，改用逗號分隔儲存多個角色：

```
User.Role = "Admin,Manager"
```

### 更新 CreateToken()

`[Authorize(Roles = "Admin")]` 的底層是比對 `ClaimTypes.Role` 的**每個 Claim 值**，因此每個角色必須是獨立的 Claim：

```csharp
// ❌ 只有一個 Claim，值是 "Admin,Manager" 字串 → [Authorize(Roles = "Admin")] 會失敗
claims.Add(new Claim(ClaimTypes.Role, user.Role));

// ✅ 每個角色各自一個 Claim → 正確做法
var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(r => r.Trim());
foreach (var role in roles)
    claims.Add(new Claim(ClaimTypes.Role, role));
```

生成的 JWT Payload：
```json
{
  "role": ["Admin", "Manager"],
  "name": "alice"
}
```

---

## [Authorize(Roles)] 的 OR 與 AND

### OR 邏輯（逗號分隔）

```csharp
// Admin 或 Manager 皆可訪問
[Authorize(Roles = "Admin,Manager")]
[HttpGet("management")]
public IActionResult ManagementEndpoint() { ... }
```

### AND 邏輯（堆疊多個特性）

```csharp
// 必須同時擁有 Admin 和 Auditor
[Authorize(Roles = "Admin")]
[Authorize(Roles = "Auditor")]
[HttpGet("admin-audit")]
public IActionResult AdminAuditEndpoint() { ... }
```

---

## User.IsInRole() 程式化檢查

`[Authorize]` 用於請求進入前直接拒絕，`User.IsInRole()` 用於進入後根據角色執行不同邏輯：

```csharp
[Authorize(Roles = "Admin,Manager")]
[HttpGet("management")]
public IActionResult ManagementEndpoint()
{
    var message = User.IsInRole("Admin")
        ? "You are an Admin accessing management."
        : "You are a Manager accessing management.";

    return Ok(message);
}
```

其他常用的 `User` 輔助方法：

```csharp
string? username = User.FindFirstValue(ClaimTypes.Name);
string? userId   = User.FindFirstValue(ClaimTypes.NameIdentifier);
var roles        = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
```

---

## 角色管理 API

### 授予角色

```csharp
[Authorize(Roles = "Admin")]
[HttpPut("{userId:guid}/grant-role")]
public async Task<ActionResult<User>> GrantRole(Guid userId, [FromBody] string role)
{
    var user = await authService.GrantRoleAsync(userId, role);
    if (user is null) return NotFound("User not found.");
    return Ok(user);
}
```

### 撤銷角色

```csharp
[Authorize(Roles = "Admin")]
[HttpDelete("{userId:guid}/revoke-role/{role}")]
public async Task<ActionResult<User>> RevokeRole(Guid userId, string role)
{
    var user = await authService.RevokeRoleAsync(userId, role);
    if (user is null) return NotFound("User not found.");
    return Ok(user);
}
```

### Service 層實作

```csharp
public async Task<User?> GrantRoleAsync(Guid userId, string role)
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return null;

    var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(r => r.Trim()).ToList();

    if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
    {
        roles.Add(role);
        user.Role = string.Join(",", roles);
        await context.SaveChangesAsync();
    }
    return user;
}

public async Task<User?> RevokeRoleAsync(Guid userId, string role)
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return null;

    var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(r => r.Trim())
                     .Where(r => !r.Equals(role, StringComparison.OrdinalIgnoreCase))
                     .ToList();

    user.Role = string.Join(",", roles);
    await context.SaveChangesAsync();
    return user;
}
```

---

## 測試

準備兩個用戶：`alice`（無角色）、`charlie`（Admin）

**授予角色**

```
PUT /api/auth/{charlieId}/grant-role
Authorization: Bearer {charlie_token}
Body: "Manager"
```

**測試 Management 端點**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| charlie（Admin） | `200 OK` - "You are an Admin..." |
| bob（Manager 角色） | `200 OK` - "You are a Manager..." |

> **401 vs 403：**
> - `401 Unauthorized` — 沒有 Token 或 Token 無效（未認證）
> - `403 Forbidden` — 有 Token 但角色不符（已認證，無權限）

---

## 角色授權的限制

角色授權是**粗粒度**的，難以表達「用戶只能編輯自己的文章」這類資源層級的需求。下一分支將介紹：
- `9_claim_based_authorization` — `IAuthorizationRequirement` + 資源授權

---

## 參考資源

- [ASP.NET Core 角色授權文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [ClaimsPrincipal 文檔](https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal)
