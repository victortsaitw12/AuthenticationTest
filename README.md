# JWT 認證系統教學 - 第十部分：基於 Policy 的授權（Policy-Based Authorization）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 Policy-Based Authorization 解決「規則散落各地」的問題
2. 在 `AddAuthorization()` 中**集中定義**具名授權策略
3. 在 Policy 中**組合多個 Requirement**（AND 邏輯）
4. 使用 `FallbackPolicy` 讓整個應用預設受保護
5. 使用 `[AllowAnonymous]` 明確豁免公開端點

---

> **前置知識：** 請先完成 `9_claim_based_authorization`，了解 `IAuthorizationRequirement` 和 `IAuthorizationHandler` 的工作原理。本分支是在那個基礎上，新增「把規則命名化、集中管理」的能力。

---

## 第十三步：基於 Policy 的授權

### Branch 9 留下的問題

Branch 9 教會了我們如何寫 Requirement 和 Handler，但**在哪裡使用這些規則**仍然分散：

```csharp
// Branch 9 的做法：每次都要 new 一個 Requirement，條件直接寫在 Controller 裡
var result = await authorizationService.AuthorizeAsync(
    User, userId, new SameUserRequirement());

// 更早的做法：規則直接寫在特性裡
[Authorize(Roles = "Admin,Manager")]
[Authorize(Roles = "Admin,Manager")]  // 在另一個 Controller 重複一遍
```

**這帶來幾個問題：**

| 問題 | 描述 |
|------|------|
| 規則散落 | 「Manager 以上」這個條件，散落在 10 個 Controller 的特性上 |
| 修改麻煩 | 條件一改（例如加入 Director 角色），要找到所有地方修改 |
| 無法組合 | `[Authorize(Roles)]` 只能做簡單的角色比對，無法把多個 Requirement 組在一起 |
| 難以測試 | 規則和 Controller 耦合，不易單獨測試 |

### Policy-Based Authorization 的核心思想

**把「授權規則」從「使用規則的地方」分離出來**，集中定義在 `Program.cs`，給每條規則取一個名字，其他地方只寫名字。

```
Program.cs（集中定義）          Controller（只寫名字）
─────────────────────           ─────────────────────
"ManagerOrAbove"                [Authorize(Policy = "ManagerOrAbove")]
  = MinimumRoleLevel(Manager)
```

---

### 定義具名 Policy

所有 Policy 在 `Program.cs` 的 `AddAuthorization()` 中集中定義：

```csharp
builder.Services.AddAuthorization(options =>
{
    // 策略一：Manager 或以上等級（使用 Branch 9 定義的 Requirement）
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

    // 策略二：Admin 等級
    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));

    // 策略三：組合多個 Requirement（AND 邏輯）
    // 必須同時滿足：已登入 + Token 有 Name Claim + Admin 等級
    options.AddPolicy("StrictAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(ClaimTypes.Name);
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin));
    });

    // FallbackPolicy：保護「忘記加 [Authorize]」的端點（見下方說明）
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

**Policy 的內建快捷方法：**

```csharp
policy.RequireAuthenticatedUser();              // 已登入
policy.RequireClaim(ClaimTypes.Email);          // 有 Email Claim（不限值）
policy.RequireClaim(ClaimTypes.Role, "Admin");  // Role Claim 值為 "Admin"
policy.RequireRole("Admin");                    // 等同上一行
policy.RequireAssertion(ctx =>                  // 任意自定義條件
    ctx.User.HasClaim(c => c.Type == "subscription" && c.Value == "premium"));
```

---

### 在 Controller 使用 `[Authorize(Policy = "...")]`

```csharp
// 一行字引用複雜規則，不需要知道規則的細節
[Authorize(Policy = "ManagerOrAbove")]
[HttpGet("reports")]
public IActionResult GetReports()
{
    return Ok("Reports for Manager and above.");
}

[Authorize(Policy = "AdminOnly")]
[HttpGet("system-config")]
public IActionResult GetSystemConfig()
{
    return Ok("System config, Admin only.");
}

// 組合 Policy：多個 Requirement 的 AND 組合
[Authorize(Policy = "StrictAdminOnly")]
[HttpDelete("users/{userId:guid}")]
public async Task<IActionResult> DeleteUser(Guid userId)
{
    var user = await authService.DeleteUserAsync(userId);
    if (user is null) return NotFound();
    return Ok($"User {user.Username} deleted.");
}
```

#### 程式化 Policy 評估（by name）

Branch 9 的程式化授權是傳入 `Requirement` 物件；Branch 10 改成傳入 **Policy 名稱**，Controller 不需要知道背後的規則：

```csharp
[Authorize]
[HttpGet("dashboard")]
public async Task<IActionResult> GetDashboard()
{
    var adminCheck = await authorizationService.AuthorizeAsync(User, "AdminOnly");
    if (adminCheck.Succeeded)
        return Ok(new { level = "Admin", data = "Full system dashboard." });

    var managerCheck = await authorizationService.AuthorizeAsync(User, "ManagerOrAbove");
    if (managerCheck.Succeeded)
        return Ok(new { level = "Manager", data = "Team performance dashboard." });

    return Ok(new { level = "User", data = "Personal activity dashboard." });
}
```

---

### FallbackPolicy：讓「忘記加 [Authorize]」成為歷史

這是 Branch 10 引入的重要安全概念。

#### 問題：防禦性思維 vs 開放性思維

**預設開放（歷史做法）：**
```
預設：所有端點都公開
需要保護時：加上 [Authorize]
風險：忘記加 = 漏洞
```

**預設保護（FallbackPolicy）：**
```
預設：所有端點都需要登入（FallbackPolicy）
需要公開時：加上 [AllowAnonymous]
好處：忘記加 = 安全（只是多一層保護）
```

#### 實作 FallbackPolicy

**Step 1：在 Program.cs 設定 FallbackPolicy**

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

**Step 2：在公開端點加上 `[AllowAnonymous]`**

```csharp
[AllowAnonymous]  // ← 明確豁免，可以不帶 Token 訪問
[HttpPost("register")]
public async Task<ActionResult<User>> Register(UserDto request) { ... }

[AllowAnonymous]
[HttpPost("login")]
public async Task<ActionResult<TokenResponseDto>> Login(UserDto request) { ... }

[AllowAnonymous]
[HttpPost("refresh-token")]
public async Task<ActionResult<TokenResponseDto>> RefreshToken(...) { ... }
```

#### FallbackPolicy vs DefaultPolicy

```csharp
// DefaultPolicy：[Authorize] 沒有指定任何條件時，使用這個 Policy
// 預設是 RequireAuthenticatedUser()
options.DefaultPolicy = ...

// FallbackPolicy：完全沒有 [Authorize] 特性的端點，套用這個 Policy
// 預設是 null（即沒有任何保護）
options.FallbackPolicy = ...
```

| | DefaultPolicy | FallbackPolicy |
|---|---|---|
| 觸發條件 | `[Authorize]`（沒有參數） | 完全沒有 `[Authorize]` |
| 預設值 | `RequireAuthenticatedUser()` | `null`（不保護） |
| 用途 | 統一無參數 `[Authorize]` 的行為 | 全局預設保護 |

---

### Policy 的組成與 AND 邏輯

Policy 中所有條件都是 **AND**：

```
Policy "StrictAdminOnly"
    ├── RequireAuthenticatedUser()    必須滿足
    ├── RequireClaim(ClaimTypes.Name) 必須滿足
    └── MinimumRoleLevelRequirement   必須滿足
    ↓ 全部通過 → 授權成功，缺一不可
```

若要 OR 邏輯，在 Handler 內部實現（Branch 9 已示範過）。

---

## 測試 API 端點

### 驗證 FallbackPolicy

用**沒有 Token** 的請求存取各端點：

| 端點 | 預期結果 | 原因 |
|------|---------|------|
| `POST /api/auth/register` | `200 OK` | `[AllowAnonymous]` 豁免 |
| `POST /api/auth/login` | `200 OK` | `[AllowAnonymous]` 豁免 |
| `GET /api/auth` | `401 Unauthorized` | FallbackPolicy 生效 |
| `GET /api/auth/profile` | `401 Unauthorized` | FallbackPolicy 生效 |

### 驗證 Policy 端點

準備三個用戶：`alice`（User）、`bob`（Manager）、`charlie`（Admin）

**`GET /api/auth/reports` - ManagerOrAbove Policy**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `200 OK` |
| charlie | `200 OK` |

**`GET /api/auth/system-config` - AdminOnly Policy**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `403 Forbidden` |
| charlie | `200 OK` |

---

## 授權方式完整比較

| 授權方式 | 定義位置 | 能否組合 | 能傳 Resource | 適合場景 |
|---------|---------|---------|--------------|---------|
| `[Authorize(Roles)]` | Controller 特性 | 僅 OR | ❌ | 簡單角色檢查 |
| `IAuthorizationService` + Requirement | Controller 方法內 | ✅ | ✅ | 資源所有權 |
| `[Authorize(Policy)]` | Program.cs 集中 | ✅ AND | ❌（靜態） | 複雜業務規則、可重用 |
| `IAuthorizationService` + Policy name | Controller 方法內 | ✅ AND | ❌ | 分級內容、動態判斷 |

---

## 最佳實踐

**1. Policy 名稱用常數，避免魔術字串**

```csharp
public static class Policies
{
    public const string ManagerOrAbove = "ManagerOrAbove";
    public const string AdminOnly      = "AdminOnly";
    public const string StrictAdmin    = "StrictAdminOnly";
}

[Authorize(Policy = Policies.AdminOnly)]
```

**2. Policy 命名用業務語言**

```csharp
// ✅ 反映業務
"CanApproveLeaveRequests"
"CanViewFinancialReports"

// ❌ 技術語言，難以理解
"RoleLevel2OrAbove"
```

**3. 預設保護（FallbackPolicy）是更安全的架構選擇**

```csharp
// ✅ 建議：預設保護，公開端點明確豁免
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build();
// 公開端點加 [AllowAnonymous]
```

---

## 課程總結

恭喜完成整個 JWT 認證授權系列！

| 分支 | 主題 | 核心概念 |
|------|------|---------|
| 1 | 用戶註冊與登入 | BCrypt 密碼雜湊 |
| 2 | 生成 JWT Token | JWT 結構、Claims、簽名 |
| 3 | 連接資料庫 | EF Core、SQL Server |
| 4 | Service 層重構 | 依賴注入、分層架構 |
| 5 | 端點安全保護 | JWT Bearer Middleware、[Authorize] |
| 6 | 基本角色授權 | ClaimTypes.Role、[Authorize(Roles)] |
| 7 | 刷新令牌 | Refresh Token、Token 輪換 |
| 8 | 深入角色授權 | 多重角色、角色管理 API |
| 9 | **Claim-Based 授權** | **Requirement + Handler + 資源授權（Runtime）** |
| **10** | **Policy-Based 授權** | **集中命名、多條件組合、FallbackPolicy** |

---

## 參考資源

- [ASP.NET Core Policy-Based Authorization](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [AuthorizationOptions 文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationoptions)
- [ASP.NET Core 授權概覽](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/introduction)
