# JWT 認證系統教學 - 第十部分：基於 Policy 的授權（Policy-Based Authorization）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 Policy-Based Authorization 的架構和優勢
2. 使用 `AddAuthorization()` 定義具名授權策略
3. 在 Policy 中組合多個 Requirement（AND 邏輯）
4. 使用 `[Authorize(Policy = "...")]` 保護端點
5. 使用 `IAuthorizationService` 進行程式化 Policy 評估

---

> **前置知識：** 請先完成前九個分支，特別是 `9_claim_based_authorization`，了解 `IAuthorizationRequirement` 和 `IAuthorizationHandler` 的工作原理。

---

## 第十三步：基於 Policy 的授權

本步驟介紹 ASP.NET Core 中最靈活、最推薦的授權方式——**Policy-Based Authorization**。它將授權規則集中定義、命名，讓程式碼更清晰、更易於維護和測試。

### 為什麼需要 Policy-Based Authorization？

#### 問題：魔術字串到處散落

在前兩個分支中，授權規則分散在各個 Controller 中：

```csharp
// 同樣的「需要是 Admin 或 Manager」邏輯，出現在多個地方
[Authorize(Roles = "Admin,Manager")]  // UserController
[Authorize(Roles = "Admin,Manager")]  // ProductController
[Authorize(Roles = "Admin,Manager")]  // OrderController
```

**問題：**
1. 規則分散，難以維護
2. 如果需要修改條件（例如加入 `Director` 角色），需要找到所有地方修改
3. 無法組合複雜邏輯（例如「Manager 且年齡 >= 25」）
4. 難以測試

#### 解決方案：Policy 集中定義

```csharp
// Program.cs - 集中定義，一處修改全部生效
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));
});

// Controller - 只需寫 Policy 名稱
[Authorize(Policy = "ManagerOrAbove")]
public IActionResult ManagePage() { ... }
```

### Policy 的組成

一個 Policy 由一個或多個 **Requirement** 組成，所有 Requirement 必須**全部滿足**（AND 邏輯）才能授權成功：

```
Policy "StrictAdminOnly"
├── RequireAuthenticatedUser()      ← 必須已登入
├── RequireClaim(ClaimTypes.Name)   ← Token 必須包含 Name Claim
└── MinimumRoleLevelRequirement(Admin) ← 必須是 Admin 等級
    ↓ 全部通過 → 授權成功
```

### MinimumRoleLevelRequirement - 等級制授權

#### Requirement 定義

`Requirements/MinimumRoleLevelRequirement.cs`:

```csharp
/// <summary>
/// 角色等級定義：數字越大，權限越高
/// </summary>
public enum RoleLevel
{
    User    = 1,
    Manager = 2,
    Admin   = 3
}

/// <summary>
/// 要求：用戶的角色等級必須達到指定的最低等級
/// </summary>
public class MinimumRoleLevelRequirement : IAuthorizationRequirement
{
    public RoleLevel MinimumLevel { get; }

    public MinimumRoleLevelRequirement(RoleLevel minimumLevel)
    {
        MinimumLevel = minimumLevel;
    }
}
```

#### Handler 實作

`Handlers/MinimumRoleLevelAuthorizationHandler.cs`:

```csharp
public class MinimumRoleLevelAuthorizationHandler
    : AuthorizationHandler<MinimumRoleLevelRequirement>
{
    // 角色名稱 → 等級的對應表
    private static readonly Dictionary<string, RoleLevel> RoleLevelMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "User",    RoleLevel.User    },
        { "Manager", RoleLevel.Manager },
        { "Admin",   RoleLevel.Admin   }
    };

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumRoleLevelRequirement requirement)
    {
        // 取出用戶擁有的所有 Role Claims
        var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

        // 將角色名稱轉換為等級，取出最高等級
        var highestLevel = userRoles
            .Where(role => RoleLevelMap.ContainsKey(role))
            .Select(role => RoleLevelMap[role])
            .DefaultIfEmpty(0)
            .Max();

        if ((int)highestLevel >= (int)requirement.MinimumLevel)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

**這個 Handler 的優點：**

```
用戶只有 "User" 角色（等級 1）
    → MinimumRoleLevelRequirement(Manager) 要求等級 2
    → 1 < 2 → 授權失敗

用戶有 "Manager" 角色（等級 2）
    → MinimumRoleLevelRequirement(Manager) 要求等級 2
    → 2 >= 2 → 授權成功

用戶有 "Admin" 角色（等級 3）
    → MinimumRoleLevelRequirement(Manager) 要求等級 2
    → 3 >= 2 → Admin 自然通過 Manager 等級要求
```

### 在 Program.cs 中定義 Policies

```csharp
// 先註冊 Handlers
builder.Services.AddScoped<IAuthorizationHandler, SameUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MinimumRoleLevelAuthorizationHandler>();

// 定義具名授權策略
builder.Services.AddAuthorization(options =>
{
    // 策略一：要求用戶已登入（最低門檻）
    options.AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());

    // 策略二：要求 Manager 或以上等級
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

    // 策略三：要求 Admin 等級
    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));

    // 策略四：組合多個要求（AND 邏輯）
    options.AddPolicy("StrictAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimTypes.Name);
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin));
    });
});
```

**Policy 的內建快捷方法：**

```csharp
// 要求已認證
policy.RequireAuthenticatedUser();

// 要求擁有某個 Claim（只檢查 Key 存在）
policy.RequireClaim("department");

// 要求 Claim 等於特定值
policy.RequireClaim("department", "Finance", "Accounting");

// 要求擁有某個角色
policy.RequireRole("Admin");

// 要求同時擁有多個角色（AND）
policy.RequireRole("Admin", "Auditor");  // 注意：此方法為 OR，非 AND

// 自定義斷言（Assertion）
policy.RequireAssertion(context =>
    context.User.HasClaim(c => c.Type == "subscription" && c.Value == "premium"));
```

### 在 Controller 中使用 Policy

#### 特性方式（靜態）

```csharp
// 使用具名 Policy：Manager 或以上等級
[Authorize(Policy = "ManagerOrAbove")]
[HttpGet("reports")]
public IActionResult GetReports()
{
    return Ok("Reports for Manager and above.");
}

// 組合 Policy：嚴格的 Admin 檢查
[Authorize(Policy = "StrictAdminOnly")]
[HttpDelete("users/{userId:guid}")]
public async Task<IActionResult> DeleteUser(Guid userId)
{
    var user = await authService.DeleteUserAsync(userId);
    if (user is null) return NotFound("User not found.");
    return Ok($"User {user.Username} has been deleted.");
}
```

#### 程式化方式（動態）

```csharp
[Authorize]
[HttpGet("dashboard")]
public async Task<IActionResult> GetDashboard()
{
    // 動態評估不同 Policy，根據結果返回不同內容
    var adminCheck = await authorizationService.AuthorizeAsync(User, "AdminOnly");
    if (adminCheck.Succeeded)
    {
        return Ok(new { level = "Admin", data = "Full system dashboard." });
    }

    var managerCheck = await authorizationService.AuthorizeAsync(User, "ManagerOrAbove");
    if (managerCheck.Succeeded)
    {
        return Ok(new { level = "Manager", data = "Team performance dashboard." });
    }

    return Ok(new { level = "User", data = "Personal activity dashboard." });
}
```

**程式化 Policy 評估的優點：**

不直接拒絕訪問（403），而是根據用戶等級返回不同的資料。這在建立「分級內容」的 API 時非常有用。

### Policy vs Role vs Claim 的比較

| 特性 | Role-Based | Claim-Based | Policy-Based |
|------|-----------|-------------|-------------|
| 定義位置 | Controller 特性 | Handler | Program.cs 集中定義 |
| 可重用性 | 低 | 中 | 高 |
| 可組合性 | 低（只能 OR） | 中（需手動組合） | 高（AND/OR 自由組合） |
| 可測試性 | 低 | 中 | 高 |
| 適合場景 | 簡單存取控制 | 資源所有權 | 複雜業務規則 |
| 程式化評估 | 需要 IsInRole() | 需要 AuthorizeAsync | AuthorizeAsync + Policy 名稱 |

**選擇指引：**

```
需求：「只有 Admin 才能訪問」
→ 使用 [Authorize(Roles = "Admin")]（簡單夠用）

需求：「用戶只能修改自己的資料」
→ 使用 IAuthorizationService + SameUserRequirement（資源授權）

需求：「Manager 且訂閱等級為 Premium 才能訪問高級報告」
→ 使用 Policy（複雜組合邏輯）

需求：「未來可能修改條件的業務規則」
→ 使用 Policy（集中管理，易於維護）
```

### 授權架構全貌

至此，本系列教學涵蓋了 ASP.NET Core 授權的完整架構：

```
┌─────────────────────────────────────────────────────────────┐
│                    授權架構                                  │
├─────────────────┬──────────────────┬────────────────────────┤
│   Role-Based    │   Claim-Based    │    Policy-Based        │
├─────────────────┼──────────────────┼────────────────────────┤
│ [Authorize      │ IAuthorization   │ [Authorize             │
│  (Roles="...")]  │ Service +        │  (Policy="...")]        │
│                 │ Requirement +    │ AddAuthorization()     │
│                 │ Handler          │ + Requirements         │
│                 │                  │ + Handlers             │
├─────────────────┼──────────────────┼────────────────────────┤
│ 粗粒度          │ 細粒度            │ 業務邏輯封裝            │
│ 快速實作        │ 資源授權          │ 可重用、可測試          │
└─────────────────┴──────────────────┴────────────────────────┘
```

---

## 測試 API 端點

### 測試 Policy 端點

**準備：** 確保有三種等級的用戶：
- `alice` - Role: "User"
- `bob` - Role: "Manager"
- `charlie` - Role: "Admin"

**測試 ManagerOrAbove Policy：**

| 用戶 | 請求 | 預期結果 |
|------|------|---------|
| alice (User) | `GET /api/auth/reports` | `403 Forbidden` |
| bob (Manager) | `GET /api/auth/reports` | `200 OK` |
| charlie (Admin) | `GET /api/auth/reports` | `200 OK` |

**測試 AdminOnly Policy：**

| 用戶 | 請求 | 預期結果 |
|------|------|---------|
| alice (User) | `GET /api/auth/system-config` | `403 Forbidden` |
| bob (Manager) | `GET /api/auth/system-config` | `403 Forbidden` |
| charlie (Admin) | `GET /api/auth/system-config` | `200 OK` |

**測試動態 Dashboard：**

| 用戶 | 請求 | 預期結果 |
|------|------|---------|
| alice (User) | `GET /api/auth/dashboard` | `200 OK` - Personal dashboard |
| bob (Manager) | `GET /api/auth/dashboard` | `200 OK` - Team dashboard |
| charlie (Admin) | `GET /api/auth/dashboard` | `200 OK` - Full dashboard |

---

## Policy 測試策略

Policy 的集中定義讓**單元測試**變得更容易：

```csharp
// 測試 Policy 定義（單元測試）
[Test]
public async Task ManagerOrAbove_AdminUser_ShouldSucceed()
{
    // Arrange
    var user = CreateClaimsPrincipal(roles: new[] { "Admin" });
    var authService = BuildAuthorizationService(ConfigureTestPolicies);

    // Act
    var result = await authService.AuthorizeAsync(user, "ManagerOrAbove");

    // Assert
    Assert.IsTrue(result.Succeeded);
}
```

這種測試方式比測試整個 HTTP 請求更快速、更可靠。

---

## 最佳實踐

**1. 使用常數定義 Policy 名稱，避免拼寫錯誤**

```csharp
public static class Policies
{
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string ManagerOrAbove   = "ManagerOrAbove";
    public const string AdminOnly        = "AdminOnly";
}

[Authorize(Policy = Policies.AdminOnly)]
```

**2. Policy 命名用業務語言，而非技術語言**

```csharp
// ✅ 好：反映業務需求
options.AddPolicy("CanViewFinancialReports", ...);
options.AddPolicy("CanApproveLeaveRequests", ...);

// ❌ 不好：技術性的，難以理解業務含義
options.AddPolicy("RoleLevel2OrAbove", ...);
```

**3. 複雜 Policy 應有對應的單元測試**

```csharp
// 每個 Policy 都應該有測試覆蓋邊界條件
[Test] public async Task ManagerOrAbove_ManagerUser_ShouldSucceed() { ... }
[Test] public async Task ManagerOrAbove_UserRole_ShouldFail() { ... }
[Test] public async Task ManagerOrAbove_NoRole_ShouldFail() { ... }
[Test] public async Task ManagerOrAbove_AdminUser_ShouldSucceed() { ... }
```

**4. 在 Program.cs 集中定義，不要分散**

```csharp
// ✅ 好：全部在 Program.cs 的 AddAuthorization() 中
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(...);
    options.AddPolicy(...);
});

// ❌ 不好：使用 Extension Method 分散定義，難以一覽全局
builder.Services.AddUserPolicies();
builder.Services.AddAdminPolicies();
```

---

## 常見問題

### Q: Policy 中的多個 Requirement 是 AND 還是 OR？

**答：** 是 **AND**。Policy 中的所有 Requirement 都必須滿足，授權才會成功。若需要 OR 邏輯，應在單一 Handler 內部實現：

```csharp
// OR 邏輯：在 Handler 中實現
protected override Task HandleRequirementAsync(...)
{
    if (conditionA || conditionB)  // OR 在這裡
        context.Succeed(requirement);
}
```

### Q: `[Authorize(Roles = "Admin")]` 和 `[Authorize(Policy = "AdminOnly")]` 有什麼差別？

功能相同，但 Policy 更靈活：
- `Roles` 只能檢查角色，未來難以擴充
- Policy 可以組合任意 Requirements，業務規則變化時只需修改 Program.cs

**最佳實踐：** 複雜或可能變化的授權規則使用 Policy；簡單的角色檢查可以直接用 `Roles`。

### Q: 可以同時使用多個 `[Authorize]` 特性（堆疊）嗎？

**可以。** 堆疊的 `[Authorize]` 特性是 AND 邏輯：

```csharp
[Authorize(Policy = "ManagerOrAbove")]
[Authorize(Policy = "AuthenticatedUser")]
public IActionResult StackedPolicies() { ... }
// 兩個 Policy 都必須通過
```

但通常可以直接在單一 Policy 中組合多個 Requirement，更清晰：

```csharp
options.AddPolicy("VerifiedManager", policy =>
{
    policy.RequireAuthenticatedUser();
    policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager));
});
```

---

## 安全性提示

⚠️ **重要注意事項：**

1. **定期審查 Policy 定義** - 隨著業務變化，Policy 的要求可能需要更新
2. **Policy 的 Default Policy** - 可設置全局默認 Policy，確保所有端點都有基本保護
   ```csharp
   options.DefaultPolicy = new AuthorizationPolicyBuilder()
       .RequireAuthenticatedUser()
       .Build();
   ```
3. **FallbackPolicy** - 設置未標記 `[Authorize]` 的端點也需認證
   ```csharp
   options.FallbackPolicy = options.DefaultPolicy;
   ```

---

## 課程總結

恭喜完成整個 JWT 認證授權系列！以下是完整的學習路徑：

| 分支 | 主題 | 核心概念 |
|------|------|---------|
| 1 | 用戶註冊與登入 | BCrypt 密碼雜湊 |
| 2 | 生成 JWT Token | JWT 結構、Claims、簽名 |
| 3 | 連接資料庫 | EF Core、SQLServer |
| 4 | Service 層重構 | 依賴注入、分層架構 |
| 5 | 端點安全保護 | JWT Bearer Middleware、[Authorize] |
| 6 | 基本角色授權 | ClaimTypes.Role、[Authorize(Roles)] |
| 7 | 刷新令牌 | Refresh Token、Token 輪換 |
| 8 | 深入角色授權 | 多重角色、角色管理 API |
| 9 | Claim 授權 | IAuthorizationRequirement、Handler、資源授權 |
| **10** | **Policy 授權** | **AddAuthorization、具名 Policy、組合規則** |

---

## 參考資源

- [ASP.NET Core Policy-Based Authorization 文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [AuthorizationOptions.AddPolicy 文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationoptions.addpolicy)
- [ASP.NET Core 授權簡介](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/introduction)
- [OWASP 訪問控制速查表](https://cheatsheetseries.owasp.org/cheatsheets/Access_Control_Cheat_Sheet.html)
