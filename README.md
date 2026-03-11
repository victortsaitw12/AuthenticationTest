# JWT 認證系統教學 - 第八部分：基於角色的授權（Role-Based Authorization）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 深入理解 ASP.NET Core 中的角色（Role）授權機制
2. 支援用戶擁有多重角色
3. 使用 `[Authorize(Roles = "...")]` 特性進行細粒度端點保護
4. 使用 `User.IsInRole()` 進行程式化角色檢查
5. 實作管理用戶角色的 Admin API

---

> **前置知識：** 請先完成前七個分支，特別是 `6_Add_Roles` 和 `7_Refresh_Token`，了解基本角色概念和完整的 JWT 認證流程。

---

## 第十一步：深入基於角色的授權

本步驟在前一分支的角色基礎上，實現更完整的角色管理系統，支援多重角色、程式化角色檢查和角色管理 API。

### 角色授權的核心概念回顧

在 ASP.NET Core 中，角色授權的完整流程：

```
用戶登入
   ↓
AuthService.CreateToken() 將角色寫入 JWT Payload
   ↓
JWT Middleware 解析 Token，建立 ClaimsPrincipal
   ↓
[Authorize(Roles = "Admin")] 檢查 ClaimsPrincipal 中的 Role Claims
   ↓
允許或拒絕訪問
```

### 多重角色支援

#### 問題：單一角色的限制

在 `6_Add_Roles` 分支中，`User` 實體只有一個 `string Role` 欄位，這導致一個用戶只能擁有一個角色。現實系統中，用戶可能同時是 `Manager` 和 `Auditor`。

#### 解決方案：逗號分隔的多重角色

本分支使用逗號分隔字串儲存多重角色，無需修改資料庫結構：

```
User.Role = "Admin,Manager"
User.Role = "User"
User.Role = "Manager,Auditor"
```

#### 更新 CreateToken() - 生成多重 Role Claims

修改 `Services/AuthService.cs` 的 `CreateToken()` 方法：

```csharp
private string CreateToken(User user)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
    };

    // ✅ 新增：支援多重角色 - 將逗號分隔的角色字串轉換為多個 Role Claims
    var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(r => r.Trim())
                     .Where(r => !string.IsNullOrEmpty(r));
    foreach (var role in roles)
    {
        claims.Add(new Claim(ClaimTypes.Role, role));
    }

    // ... 其餘 Token 建立邏輯不變
}
```

**為什麼要為每個角色建立獨立的 Claim？**

`[Authorize(Roles = "Admin")]` 在底層是這樣運作的：
```csharp
// ASP.NET Core 內部實現（簡化）
bool hasRole = claimsPrincipal.Claims
    .Where(c => c.Type == ClaimTypes.Role)
    .Any(c => c.Value == "Admin");
```

因此，每個角色需要是一個獨立的 `ClaimTypes.Role` 聲明。如果只存一個 `"Admin,Manager"` 字串，`[Authorize(Roles = "Admin")]` 會失敗，因為它找不到值等於 `"Admin"` 的 Claim。

**JWT Payload 範例：**
```json
{
  "http://schemas.xmlsoap.org/2003/05/identity/claims/name": "alice",
  "http://schemas.xmlsoap.org/2003/05/identity/claims/nameidentifier": "...",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": ["Admin", "Manager"],
  "iss": "MyApp",
  "aud": "MyAppUsers",
  "exp": 1709941200
}
```

### [Authorize(Roles = "...")] 特性深入解析

#### 單一角色要求

```csharp
// 只有 Admin 才能訪問
[Authorize(Roles = "Admin")]
[HttpGet("admin-only")]
public IActionResult AdminOnlyEndpoint()
{
    return Ok("You are an Admin.");
}
```

#### 多重角色（OR 邏輯）

```csharp
// Admin 或 Manager 皆可訪問（逗號代表 OR）
[Authorize(Roles = "Admin,Manager")]
[HttpGet("management")]
public IActionResult ManagementEndpoint()
{
    return Ok("You are an Admin or Manager.");
}
```

> **⚠️ 重要：** 逗號在 `Roles` 屬性中代表 **OR**（任一角色即可），而非 AND（同時擁有所有角色）。

#### 多重角色（AND 邏輯）

如需要求用戶**同時**擁有多個角色，需要堆疊多個 `[Authorize]` 特性：

```csharp
// 必須同時擁有 Admin 和 Auditor 角色
[Authorize(Roles = "Admin")]
[Authorize(Roles = "Auditor")]
[HttpGet("admin-audit")]
public IActionResult AdminAuditEndpoint()
{
    return Ok("You are both Admin and Auditor.");
}
```

### 程式化角色檢查 - User.IsInRole()

有時你需要在方法內部根據角色執行不同邏輯，而不是直接拒絕訪問：

```csharp
[Authorize(Roles = "Admin,Manager")]
[HttpGet("management")]
public IActionResult ManagementEndpoint()
{
    // 根據角色返回不同內容
    var message = User.IsInRole("Admin")
        ? "You are an Admin accessing management."
        : "You are a Manager accessing management.";

    return Ok(message);
}
```

**`User` 物件來自哪裡？**

`User` 是 `ControllerBase` 基底類別的屬性，型別是 `ClaimsPrincipal`，由 JWT Middleware 在請求處理前自動建立：

```csharp
// ASP.NET Core 內部（簡化）
public ClaimsPrincipal User => HttpContext.User;
```

**可用的 User 輔助方法：**

```csharp
// 檢查是否有某個角色
bool isAdmin = User.IsInRole("Admin");

// 取得特定 Claim 的值
string? username = User.FindFirstValue(ClaimTypes.Name);
string? userId   = User.FindFirstValue(ClaimTypes.NameIdentifier);

// 取得所有角色
var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

// 檢查是否已認證
bool isAuthenticated = User.Identity?.IsAuthenticated ?? false;
```

### 個人資料端點 - 查看自己的角色

```csharp
[Authorize]
[HttpGet("profile")]
public IActionResult GetProfile()
{
    var username = User.FindFirstValue(ClaimTypes.Name);
    var userId   = User.FindFirstValue(ClaimTypes.NameIdentifier);
    var roles    = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

    return Ok(new
    {
        Username = username,
        UserId   = userId,
        Roles    = roles
    });
}
```

**回應範例：**
```json
{
  "username": "alice",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "roles": ["Admin", "Manager"]
}
```

### 角色管理 API

#### 授予角色

```csharp
// Admin 才能授予角色
[Authorize(Roles = "Admin")]
[HttpPut("{userId:guid}/grant-role")]
public async Task<ActionResult<User>> GrantRole(Guid userId, [FromBody] string role)
{
    var user = await authService.GrantRoleAsync(userId, role);
    if (user is null)
    {
        return NotFound("User not found.");
    }
    return Ok(user);
}
```

#### 撤銷角色

```csharp
// Admin 才能撤銷角色
[Authorize(Roles = "Admin")]
[HttpDelete("{userId:guid}/revoke-role/{role}")]
public async Task<ActionResult<User>> RevokeRole(Guid userId, string role)
{
    var user = await authService.RevokeRoleAsync(userId, role);
    if (user is null)
    {
        return NotFound("User not found.");
    }
    return Ok(user);
}
```

#### Service 層實作

`Services/AuthService.cs` 中的 `GrantRoleAsync` 和 `RevokeRoleAsync`：

```csharp
public async Task<User?> GrantRoleAsync(Guid userId, string role)
{
    var user = await context.Users.FindAsync(userId);
    if (user is null) return null;

    // 解析現有角色（逗號分隔）
    var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(r => r.Trim())
                     .ToList();

    // 避免重複新增角色（不區分大小寫）
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

    // 過濾掉要撤銷的角色
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

## 測試 API 端點

### 完整測試流程

**第一步：以 Admin 身份登入**

確保資料庫中有一個 `Role = "Admin"` 的用戶（可透過直接修改資料庫，或使用 grant-role API 授予）。

```json
POST /api/auth/login
{
  "username": "admin",
  "password": "AdminPassword123!"
}
```

**第二步：測試 Admin 專屬端點**

在 Scalar UI 中設置 Authorization Header 後：

```
GET /api/auth/admin-only
```

**預期結果：** `200 OK - "You are an Admin."`

**第三步：測試 Management 端點**

```
GET /api/auth/management
```

**預期結果：** `200 OK - "You are an Admin accessing management."`

**第四步：查看個人資料**

```
GET /api/auth/profile
```

**預期結果：**
```json
{
  "username": "admin",
  "userId": "...",
  "roles": ["Admin"]
}
```

**第五步：授予另一個用戶 Manager 角色**

先取得另一個用戶的 ID（可從 register 回應中取得），然後：

```json
PUT /api/auth/{userId}/grant-role
Authorization: Bearer {admin_token}
Content-Type: application/json

"Manager"
```

**第六步：以更新後的用戶重新登入，驗證多重角色**

用授予了 `"Admin,Manager"` 的用戶登入後測試：
- `GET /api/auth/admin-only` - 應成功
- `GET /api/auth/management` - 應成功
- `GET /api/auth/profile` 中的 roles 應顯示 `["Admin", "Manager"]`

### 授權失敗測試

使用無角色用戶的 Token 嘗試訪問 Admin 端點：

```
GET /api/auth/admin-only
Authorization: Bearer {regular_user_token}
```

**預期結果：** `403 Forbidden`

> **注意：** 401 和 403 的區別：
> - `401 Unauthorized`：用戶**未認證**（沒有 Token 或 Token 無效）
> - `403 Forbidden`：用戶已認證，但**沒有權限**（角色不符）

---

## 角色授權的最佳實踐

**1. 使用常數定義角色名稱，避免魔術字串**
```csharp
public static class Roles
{
    public const string Admin   = "Admin";
    public const string Manager = "Manager";
    public const string User    = "User";
}

[Authorize(Roles = Roles.Admin)]
```

**2. 角色命名採用 PascalCase，保持大小寫一致**
```csharp
// ✅ 好
"Admin", "Manager", "User"

// ❌ 不好
"admin", "MANAGER", "User"
```

**3. 在 Service 層驗證角色名稱合法性**
```csharp
public async Task<User?> GrantRoleAsync(Guid userId, string role)
{
    var validRoles = new[] { Roles.Admin, Roles.Manager, Roles.User };
    if (!validRoles.Contains(role)) return null;
    // ...
}
```

**4. 角色管理端點需嚴格限制**
```csharp
// ✅ 只有 Admin 才能管理角色
[Authorize(Roles = Roles.Admin)]
[HttpPut("{userId:guid}/grant-role")]
```

**5. 記錄角色變更（稽核日誌）**
```csharp
_logger.LogWarning("Role {Role} granted to user {UserId} by admin {AdminId}",
    role, userId, User.FindFirstValue(ClaimTypes.NameIdentifier));
```

---

## 常見問題

### Q: `[Authorize(Roles = "Admin,Manager")]` 中逗號是 OR 還是 AND？

**答：** 是 **OR**。任一角色都可以訪問。要實現 AND 邏輯，需堆疊多個 `[Authorize]` 特性：
```csharp
[Authorize(Roles = "Admin")]
[Authorize(Roles = "Auditor")]  // 同時需要 Admin 和 Auditor
```

### Q: `User.IsInRole()` 和 `[Authorize(Roles = "...")]` 的差別？

**答：** 功能上相同，都是檢查 JWT Claims 中的 Role。`[Authorize]` 用於請求進入前就拒絕存取，`User.IsInRole()` 用於方法內部根據角色執行不同邏輯。

### Q: 用戶更換角色後，舊的 Token 仍然有效嗎？

**答：** 是的。JWT 是**無狀態**的，Token 生成後就固定了。若需立即生效，可以縮短 Token 有效期（如 15 分鐘）讓用戶重新登入，或實作 Token 黑名單機制。

### Q: 角色授權的缺點是什麼？

**答：** 角色授權是**粗粒度**的。例如「用戶只能編輯自己的文章」這類需求，用角色很難表達。這時需要更精細的授權機制，例如：
- 基於 Claim 的授權（下一分支：`9_claim_based_authorization`）
- 基於 Policy 的授權（`10_policy_based_authorization`）

---

## 安全性提示

⚠️ **重要注意事項：**

1. **防止角色升級攻擊** - 角色管理端點必須嚴格限制，只有 Admin 才能修改角色
2. **最小權限原則** - 用戶只應擁有完成工作所需的最少角色
3. **避免角色爆炸** - 避免創建過多細粒度角色，改用 Policy 或 Claim 授權
4. **Token 失效問題** - 角色變更後舊 Token 仍有效，需配合短期 Token + Refresh Token 機制

---

## 進階話題預告

本分支涵蓋了基於角色的授權深度應用。後續分支將涵蓋：
- 🎯 基於 Claim 的授權（`9_claim_based_authorization`）- 使用自定義 Claim 和 `IAuthorizationRequirement` 實現細粒度授權
- 📋 基於 Policy 的授權（`10_policy_based_authorization`）- 定義可重用的授權策略

---

## 參考資源

- [ASP.NET Core 角色授權文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [ClaimsPrincipal 文檔](https://docs.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal)
- [OWASP 訪問控制速查表](https://cheatsheetseries.owasp.org/cheatsheets/Access_Control_Cheat_Sheet.html)
