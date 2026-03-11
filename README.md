# JWT 認證系統教學 - 第十部分：Policy-Based Authorization

## 專案概述

本分支新增：

1. 在 `AddAuthorization()` 中集中定義具名授權策略
2. 在 Policy 中組合多個 Requirement（AND 邏輯）
3. `FallbackPolicy`：讓整個應用預設受保護
4. `[AllowAnonymous]` 明確豁免公開端點

---

> **前置知識：** 請先完成 `9_claim_based_authorization`，了解 `IAuthorizationRequirement` 和 `IAuthorizationHandler` 的工作原理。

---

## 第十三步：Policy-Based Authorization

### Branch 9 留下的問題

Branch 9 教我們寫 Requirement 和 Handler，但規則的**使用方式**仍然分散：

```csharp
// 每個 Controller 各自 new 一個 Requirement，規則寫死在裡面
[Authorize(Roles = "Admin,Manager")]         // Controller A
[Authorize(Roles = "Admin,Manager")]         // Controller B（重複）

// 或是直接 new 物件
await authorizationService.AuthorizeAsync(User, userId, new SameUserRequirement());
```

問題：條件一改（例如多加一個 Director 角色），要找遍所有 Controller 修改。

### 解法：給規則取一個名字

把規則集中定義在 `Program.cs`，其他地方只引用名字：

```
Program.cs（集中定義）              Controller（只寫名字）
────────────────────────            ──────────────────────────────
"ManagerOrAbove"                    [Authorize(Policy = "ManagerOrAbove")]
  = MinimumRoleLevel(Manager)
```

---

## 定義具名 Policy

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));

    // 組合多個 Requirement（AND 邏輯）：三個條件都必須滿足
    options.AddPolicy("StrictAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimTypes.Name);
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin));
    });

    // FallbackPolicy：保護「忘記加 [Authorize]」的端點
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

Policy 的常用內建方法：

```csharp
policy.RequireAuthenticatedUser();
policy.RequireClaim(ClaimTypes.Email);
policy.RequireRole("Admin");
policy.RequireAssertion(ctx =>
    ctx.User.HasClaim(c => c.Type == "subscription" && c.Value == "premium"));
```

---

## 在 Controller 使用

### 靜態宣告

```csharp
[Authorize(Policy = "ManagerOrAbove")]
[HttpGet("reports")]
public IActionResult GetReports() => Ok("Reports for Manager and above.");

[Authorize(Policy = "StrictAdminOnly")]
[HttpDelete("users/{userId:guid}")]
public async Task<IActionResult> DeleteUser(Guid userId) { ... }
```

### 程式化評估（by name）

Branch 9 傳入 `Requirement` 物件；Branch 10 改成傳入 **Policy 名稱**，Controller 不需要知道背後的規則細節：

```csharp
[Authorize]
[HttpGet("dashboard")]
public async Task<IActionResult> GetDashboard()
{
    if ((await authorizationService.AuthorizeAsync(User, "AdminOnly")).Succeeded)
        return Ok(new { level = "Admin", data = "Full system dashboard." });

    if ((await authorizationService.AuthorizeAsync(User, "ManagerOrAbove")).Succeeded)
        return Ok(new { level = "Manager", data = "Team performance dashboard." });

    return Ok(new { level = "User", data = "Personal activity dashboard." });
}
```

---

## FallbackPolicy

### 預設開放 vs 預設保護

| 策略 | 預設狀態 | 忘記加特性時 |
|------|---------|------------|
| 預設開放（舊做法） | 所有端點公開 | **漏洞** |
| FallbackPolicy（新做法） | 所有端點需登入 | 安全（只是多保護一層） |

### 實作

```csharp
// Program.cs
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

```csharp
// 公開端點需明確加上 [AllowAnonymous]
[AllowAnonymous]
[HttpPost("register")]
public async Task<ActionResult<User>> Register(UserDto request) { ... }
```

### FallbackPolicy vs DefaultPolicy

| | DefaultPolicy | FallbackPolicy |
|---|---|---|
| 觸發條件 | `[Authorize]`（無參數） | 完全沒有 `[Authorize]` |
| 預設值 | `RequireAuthenticatedUser()` | `null`（不保護） |

---

## 測試

準備三個用戶：`alice`（User）、`bob`（Manager）、`charlie`（Admin）

**無 Token 訪問（驗證 FallbackPolicy）**

| 端點 | 預期 |
|------|------|
| `POST /api/auth/register` | `200 OK`（AllowAnonymous） |
| `GET /api/auth/profile` | `401 Unauthorized`（FallbackPolicy） |

**`GET /api/auth/reports` - ManagerOrAbove**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `200 OK` |
| charlie | `200 OK` |

---

## 參考資源

- [ASP.NET Core Policy-Based Authorization](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [AuthorizationOptions 文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationoptions)
