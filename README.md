# JWT 認證系統教學 - 第九部分：基於 Claim 的授權（Claim-Based Authorization）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 Claim 與 Role 的本質差別
2. 使用特性方式（`[Authorize(Policy = "...")]`）進行 Claim 授權
3. 建立自定義的 `IAuthorizationRequirement`
4. 實作 `AuthorizationHandler<TRequirement, TResource>` 進行資源授權
5. 使用 `IAuthorizationService` 進行程式化授權檢查
6. 理解特性方式與程式化方式的差別與適用場景

---

> **前置知識：** 請先完成前八個分支，了解基本認證流程和角色授權。

---

## 第十二步：基於 Claim 的授權

本步驟介紹比角色授權更靈活的 **Claim-Based Authorization**，並通過實際範例說明何時應該使用它。

### 角色授權的局限性

回顧 `8_role_based_authorization` 分支：

```csharp
// 角色授權：只能問「你是什麼角色？」
[Authorize(Roles = "Admin")]
public IActionResult AdminOnly() { ... }
```

**無法用角色授權解決的問題：**

```
需求：「用戶只能修改自己的個人資料」

[Authorize(Roles = "User")]  // ❌ 這無法阻止 User A 修改 User B 的資料
public IActionResult UpdateProfile(Guid userId) { ... }
```

這類問題需要**資源授權（Resource Authorization）**：授權決策不只取決於用戶是誰，還取決於**操作的對象（資源）**。

### Claim 是什麼？

Claim（聲明）是 JWT 中的鍵值對，描述用戶的屬性：

```json
{
  "http://schemas.xmlsoap.org/2003/05/identity/claims/name": "alice",
  "http://schemas.xmlsoap.org/2003/05/identity/claims/nameidentifier": "3fa85f64...",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin"
}
```

**角色（Role）本質上也是一種 Claim：**
```
ClaimTypes.Role = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
```

**Claim vs Role 的差別：**

| 特性 | Role | Claim |
|------|------|-------|
| 粒度 | 粗粒度（身份標籤） | 細粒度（任何屬性） |
| 用途 | 功能訪問控制 | 資源所有權、屬性匹配 |
| 範例 | "Admin", "Manager" | userId, department, email |
| 靈活性 | 低 | 高 |

### 特性方式（Attribute Way）：使用 RequireClaim 定義 Policy

Claim-Based Authorization 也可以用 `[Authorize(Policy = "...")]` 特性的方式使用，只要在 `Program.cs` 中用 `RequireClaim()` 定義好 Policy 即可。

#### 在 Program.cs 中定義 Policy

```csharp
builder.Services.AddAuthorization(options =>
{
    // 要求 Token 必須包含 NameIdentifier Claim（即已登入且有 UserId）
    options.AddPolicy("HasUserId", policy =>
        policy.RequireClaim(ClaimTypes.NameIdentifier));

    // 要求 Role Claim 值為 "Admin"
    // 這等同於 [Authorize(Roles = "Admin")]，但用 Claim 的角度來理解
    options.AddPolicy("RequireAdminClaim", policy =>
        policy.RequireClaim(ClaimTypes.Role, "Admin"));
});
```

**`RequireClaim()` 的兩種用法：**

```csharp
// 只檢查 Claim 是否存在（不管值是什麼）
policy.RequireClaim(ClaimTypes.NameIdentifier);

// 檢查 Claim 存在，且值必須是列表中的其中一個（OR 邏輯）
policy.RequireClaim(ClaimTypes.Role, "Admin", "SuperAdmin");
```

#### 在 Controller 使用 [Authorize(Policy = "...")]

```csharp
// 要求 Token 必須包含 NameIdentifier Claim
[Authorize(Policy = "HasUserId")]
[HttpGet("my-id")]
public IActionResult GetMyId()
{
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    return Ok(new { UserId = userId });
}

// 要求 Role Claim 值為 "Admin"
[Authorize(Policy = "RequireAdminClaim")]
[HttpGet("admin-via-claim")]
public IActionResult AdminViaClaimPolicy()
{
    return Ok("Accessed via RequireAdminClaim policy.");
}
```

#### 揭示 Role 的本質

`RequireClaim(ClaimTypes.Role, "Admin")` 與 `[Authorize(Roles = "Admin")]` 在功能上完全相同。這說明了一個重要概念：

> **角色（Role）本質上就是一種 Claim。**
> `[Authorize(Roles = "Admin")]` 只是 `RequireClaim(ClaimTypes.Role, "Admin")` 的語法糖。

```
[Authorize(Roles = "Admin")]
     ↓ 等同於
[Authorize(Policy = "RequireAdminClaim")]
     ↓ 其中 Policy 定義為
policy.RequireClaim(ClaimTypes.Role, "Admin")
     ↓ 底層就是檢查
User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "Admin")
```

### 特性方式 vs 程式化方式

| 對比 | 特性方式 `[Authorize(Policy)]` | 程式化方式 `IAuthorizationService` |
|------|-------------------------------|-----------------------------------|
| **適用場景** | 靜態規則（不依賴請求中的資料） | 動態規則（需要傳入資源物件） |
| **能否傳 Resource** | ❌ 否 | ✅ 是 |
| **程式碼位置** | 特性（方法外部） | 方法內部 |
| **範例** | 「必須是 Admin」 | 「只能修改自己的資料」 |

**何時必須用程式化方式？**

```csharp
// ❌ 這樣無法確認 userId 是否屬於目前登入的用戶
[Authorize(Policy = "SameUser")]  // Policy 無法知道 userId 是誰
[HttpPut("{userId:guid}/profile")]
public IActionResult UpdateProfile(Guid userId) { ... }

// ✅ 必須用程式化方式，才能把 userId 傳給 Handler 做比對
[Authorize]
[HttpPut("{userId:guid}/profile")]
public async Task<IActionResult> UpdateProfile(Guid userId)
{
    var result = await authorizationService.AuthorizeAsync(
        User,
        userId,                    // ← resource 在這裡傳入
        new SameUserRequirement());
    // ...
}
```

---

### ASP.NET Core 授權架構

```
[Authorize] 特性
    ↓
AuthorizationMiddleware
    ↓
IAuthorizationService.AuthorizeAsync()
    ↓
IAuthorizationHandler（可以有多個）
    ↓
AuthorizationHandlerContext（包含 User + Resource + Requirement）
    ↓
context.Succeed() / context.Fail()
```

**三個核心介面：**

1. **`IAuthorizationRequirement`** - 定義授權的**要求**（What to check）
2. **`AuthorizationHandler<TRequirement>`** - 實現授權的**邏輯**（How to check）
3. **`IAuthorizationService`** - 執行授權的**服務**（Who runs the check）

### 實作 SameUserRequirement

#### Step 1：定義 Requirement

`Requirements/SameUserRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace AuthenticationTest.Requirements
{
    /// <summary>
    /// 要求：只有資源擁有者本人（或 Admin）才能執行此操作
    /// </summary>
    public class SameUserRequirement : IAuthorizationRequirement { }
}
```

> **重點：** `IAuthorizationRequirement` 只是一個標記介面（Marker Interface），本身不包含邏輯。邏輯在 Handler 中實現。

#### Step 2：實作 Handler

`Handlers/SameUserAuthorizationHandler.cs`:

```csharp
using AuthenticationTest.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace AuthenticationTest.Handlers
{
    /// <summary>
    /// 處理 SameUserRequirement：
    /// 允許條件：操作者是資源擁有者本人，或操作者是 Admin
    /// </summary>
    public class SameUserAuthorizationHandler
        : AuthorizationHandler<SameUserRequirement, Guid>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SameUserRequirement requirement,
            Guid resourceUserId)           // ← resource 就是我們傳入的 userId
        {
            // 從 JWT Claims 取出目前登入用戶的 ID
            var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            // 條件一：操作的用戶 ID 等於資源擁有者的 ID（同一個人）
            var isSameUser = currentUserId == resourceUserId.ToString();

            // 條件二：操作者是 Admin，有權代替任何人操作
            var isAdmin = context.User.IsInRole("Admin");

            if (isSameUser || isAdmin)
            {
                context.Succeed(requirement);  // ✅ 授權成功
            }
            // 注意：不呼叫 context.Fail()，讓其他 Handler 有機會處理

            return Task.CompletedTask;
        }
    }
}
```

**Handler 泛型參數解析：**

```csharp
AuthorizationHandler<SameUserRequirement, Guid>
//                   ↑ Requirement 型別    ↑ Resource 型別
```

- `TRequirement`：這個 Handler 處理哪種 Requirement
- `TResource`：資源的型別（這裡是 `Guid`，代表用戶的 ID）

#### Step 3：在 Program.cs 註冊 Handler

```csharp
// 必須將 Handler 註冊到 DI 容器，ASP.NET Core 才能自動找到並執行它
builder.Services.AddScoped<IAuthorizationHandler, SameUserAuthorizationHandler>();
```

> **為什麼需要手動註冊？** 不同於 `[Authorize(Roles = "...")]`，自定義 Handler 不是框架內建的，需要顯式告訴 DI 容器它的存在。

#### Step 4：在 Controller 中使用 IAuthorizationService

```csharp
[Authorize]
[HttpPut("{userId:guid}/profile")]
public async Task<IActionResult> UpdateProfile(Guid userId, [FromBody] string newUsername)
{
    // 程式化授權：將資源（userId）傳給 Handler 進行判斷
    var authResult = await authorizationService.AuthorizeAsync(
        User,                      // ClaimsPrincipal（目前登入的用戶）
        userId,                    // Resource（操作的目標用戶 ID）
        new SameUserRequirement()  // Requirement（要滿足的條件）
    );

    if (!authResult.Succeeded)
    {
        return Forbid();  // 403 Forbidden
    }

    // 授權成功，執行更新邏輯
    var user = await authService.UpdateUsernameAsync(userId, newUsername);
    if (user is null) return NotFound("User not found.");

    return Ok(user);
}
```

**`IAuthorizationService` 如何注入？**

在 Controller 建構子中加入注入：
```csharp
public class AuthController(
    IAuthService authService,
    IAuthorizationService authorizationService  // ← 新增注入
) : ControllerBase
```

`IAuthorizationService` 是 ASP.NET Core 內建服務，無需額外在 Program.cs 中註冊，`AddAuthorization()` 已自動完成。

### 授權流程圖

```
PUT /api/auth/{userId}/profile

[Authorize] 先確保用戶已登入
   ↓
進入 UpdateProfile 方法
   ↓
authorizationService.AuthorizeAsync(User, userId, SameUserRequirement)
   ↓
ASP.NET Core 找到所有處理 SameUserRequirement 的 Handler
   ↓
SameUserAuthorizationHandler.HandleRequirementAsync(context, requirement, userId)
   ↓
是否是同一個用戶 OR 是 Admin？
   ├─ 是 → context.Succeed() → authResult.Succeeded = true → 繼續執行
   └─ 否 → 不呼叫 Succeed → authResult.Succeeded = false → 403 Forbidden
```

### Claim 的讀取方式

從 `context.User`（即 `ClaimsPrincipal`）讀取各種 Claim：

```csharp
// 讀取單個 Claim 值
string? name = context.User.FindFirstValue(ClaimTypes.Name);
string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

// 讀取所有同類型的 Claim（例如多重角色）
var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

// 自定義 Claim（直接使用字串 Key）
string? department = context.User.FindFirstValue("department");

// 使用 HasClaim 檢查是否有某個 Claim
bool hasEmailClaim = context.User.HasClaim(c => c.Type == ClaimTypes.Email);
bool isVerified = context.User.HasClaim("is_verified", "true");
```

### 在 JWT 中添加自定義 Claim

若要讓 Token 攜帶自定義 Claim，在 `CreateToken()` 中添加：

```csharp
private string CreateToken(User user)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),

        // 標準 Claim
        // new Claim(ClaimTypes.Email, user.Email),

        // 自定義 Claim（使用自定義字串 Key）
        // new Claim("department", user.Department),
        // new Claim("subscription_type", user.SubscriptionType),
    };

    // ... 其餘不變
}
```

**自定義 Claim 的解析：**

```csharp
// 在 Handler 中讀取自定義 Claim
var department = context.User.FindFirstValue("department");
if (department == "Finance")
{
    context.Succeed(requirement);
}
```

---

## 測試 API 端點

### 測試「用戶只能更新自己的資料」

**場景一：用戶更新自己的資料（應成功）**

1. 用戶 Alice 登入，取得 Token（Token 中含有 Alice 的 userId）
2. 發送請求：
```
PUT /api/auth/{alice_userId}/profile
Authorization: Bearer {alice_token}

"alice_new_username"
```
**預期結果：** `200 OK`

**場景二：用戶嘗試更新別人的資料（應失敗）**

1. 用戶 Alice 使用自己的 Token
2. 發送請求（使用 Bob 的 userId）：
```
PUT /api/auth/{bob_userId}/profile
Authorization: Bearer {alice_token}

"hacked_username"
```
**預期結果：** `403 Forbidden`

**場景三：Admin 更新任何人的資料（應成功）**

```
PUT /api/auth/{any_userId}/profile
Authorization: Bearer {admin_token}

"admin_changed_name"
```
**預期結果：** `200 OK`（因為 SameUserAuthorizationHandler 中 `isAdmin = true`）

---

## 最佳實踐

**1. Requirement 只定義「什麼」，Handler 定義「怎麼做」**

```csharp
// ✅ 好：Requirement 是純粹的資料容器或標記
public class MinimumAgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; }
    public MinimumAgeRequirement(int minimumAge) => MinimumAge = minimumAge;
}

// Handler 中才有邏輯
protected override Task HandleRequirementAsync(...)
{
    var birthYear = ...; // 從 Claims 取得
    if (DateTime.Now.Year - birthYear >= requirement.MinimumAge)
        context.Succeed(requirement);
}
```

**2. Handler 中不呼叫 `context.Fail()` 允許其他 Handler 繼續**

```csharp
// ✅ 好：不呼叫 Fail，讓其他 Handler 有機會授權
if (condition) context.Succeed(requirement);
// return; ← 不 Fail

// ❌ 慎用：呼叫 Fail 會立即中斷，忽略其他 Handler
context.Fail();
```

**3. 複雜的授權邏輯應放在 Handler，而非 Controller**

```csharp
// ❌ 不好：授權邏輯混在 Controller 裡
public IActionResult UpdateProfile(Guid userId)
{
    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (currentUserId != userId.ToString() && !User.IsInRole("Admin"))
        return Forbid();
    // ...
}

// ✅ 好：授權邏輯封裝在 Handler，Controller 只需一行
public async Task<IActionResult> UpdateProfile(Guid userId)
{
    var result = await authorizationService.AuthorizeAsync(User, userId, new SameUserRequirement());
    if (!result.Succeeded) return Forbid();
    // ...
}
```

**4. Handler 要單一職責**

```csharp
// ✅ 好：每個 Handler 只做一件事
public class SameUserAuthorizationHandler
    : AuthorizationHandler<SameUserRequirement, Guid> { ... }

public class OwnerOrAdminHandler
    : AuthorizationHandler<EditResourceRequirement, BlogPost> { ... }
```

---

## 常見問題

### Q: `IAuthorizationService` 和 `[Authorize]` 特性有什麼差別？

| 特性 | `[Authorize]` 特性 | `IAuthorizationService` |
|------|-------------------|------------------------|
| 使用時機 | 請求進入前的靜態檢查 | 方法內部的動態檢查 |
| 資源傳遞 | 無法傳遞資源 | 可傳遞任何資源物件 |
| 適合場景 | 角色/Policy 全局控制 | 資源所有權、動態條件 |

### Q: 為什麼 `SameUserRequirement` 是空的類別？

`IAuthorizationRequirement` 只是一個標記介面，用來讓 ASP.NET Core 知道「這是一個授權要求」。授權邏輯全部在 Handler 中。若需要傳遞參數（如最低年齡、訂閱等級），可以在 Requirement 中加入屬性：

```csharp
public class MinimumAgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; }
    public MinimumAgeRequirement(int minimumAge) => MinimumAge = minimumAge;
}
```

### Q: 可以有多個 Handler 處理同一個 Requirement 嗎？

**可以。** ASP.NET Core 會執行所有已註冊的相容 Handler。只要其中任一 Handler 呼叫 `context.Succeed()`，授權就成功（除非某個 Handler 呼叫了 `context.Fail()`）。

---

## 安全性提示

⚠️ **重要注意事項：**

1. **資源授權不能只依靠 URL** - 即使路由正確，也必須在 Handler 中驗證所有權
2. **Handler 中使用 `ClaimTypes.NameIdentifier` 而非 Username** - ID 是不可變的，Username 可能改變
3. **不要在 Token 中存儲敏感資訊** - 即使是 Claim，也不應包含密碼、信用卡等

---

## 進階話題預告

本分支涵蓋了 Claim-Based Authorization 和資源授權。後續分支將涵蓋：
- 📋 基於 Policy 的授權（`10_policy_based_authorization`）- 將授權規則命名化、集中管理，讓 `[Authorize(Policy = "...")]` 封裝複雜的業務邏輯

---

## 參考資源

- [ASP.NET Core 基於資源的授權文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased)
- [ASP.NET Core 自定義授權策略文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [IAuthorizationRequirement 介面](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationrequirement)
- [AuthorizationHandler<TRequirement> 文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationhandler-1)
