# JWT 認證系統教學 - 第九部分：基於 Claim 的授權（Claim-Based Authorization）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解靜態授權（`[Authorize]` 特性）無法解決的場景
2. 認識 `IAuthorizationRequirement` / `AuthorizationHandler` / `IAuthorizationService` 三個核心元件
3. 實作 `AuthorizationHandler<TRequirement, TResource>` 進行**資源授權**
4. 在 Controller 方法內用 `IAuthorizationService` 進行**執行期（Runtime）動態授權**

---

> **前置知識：** 請先完成前八個分支，了解基本認證流程和角色授權。

---

## 本分支異動的檔案

| 狀態 | 檔案 | 異動內容 |
|------|------|---------|
| 新增 | `Requirements/SameUserRequirement.cs` | 定義 `SameUserRequirement`（Marker Interface） |
| 新增 | `Handlers/SameUserAuthorizationHandler.cs` | 實作資源授權邏輯（比對 userId，Admin 可跳過） |
| 修改 | `Program.cs` | 註冊 `SameUserAuthorizationHandler` |
| 修改 | `Services/IAuthService.cs` | 新增 `UpdateUsernameAsync` |
| 修改 | `Services/AuthService.cs` | 實作 `UpdateUsernameAsync` |
| 修改 | `Controllers/AuthController.cs` | 注入 `IAuthorizationService`；新增 `UpdateProfile` 端點 |

---

## 第十二步：基於 Claim 的授權

### 靜態特性的根本限制

前幾個分支的授權都是**靜態**的：在方法被呼叫之前，就決定要不要放行。

```csharp
[Authorize(Roles = "Admin")]   // ← 決定在執行前：「你是 Admin 嗎？」
public IActionResult Delete(Guid userId) { ... }
```

**但有一類需求靜態特性無法解決：**

```
需求：「用戶只能修改自己的個人資料」

問題：特性在編譯期就確定了，它根本不知道請求裡的 userId 是誰的資料。
```

```csharp
[Authorize(Roles = "User")]  // ❌ 只能問「你是 User 嗎？」
                             //    無法問「這個 userId 是你自己的嗎？」
[HttpPut("{userId:guid}/profile")]
public IActionResult UpdateProfile(Guid userId) { ... }
```

這類需求叫做**資源授權（Resource Authorization）**：
- 授權決策發生在**執行期（Runtime）**
- 需要把**請求中的資源**（這裡是 `userId`）傳給授權邏輯做比對

---

### 三個核心元件

ASP.NET Core 提供了一套完整的架構來解決這個問題：

```
IAuthorizationRequirement    → 定義「要求是什麼」（What）
AuthorizationHandler<T>      → 實作「如何判斷」（How）
IAuthorizationService        → 執行「在何處觸發」（Where）
```

---

### 實作 SameUserRequirement

#### Step 1：定義 Requirement

`Requirements/SameUserRequirement.cs`:

```csharp
public class SameUserRequirement : IAuthorizationRequirement { }
```

`IAuthorizationRequirement` 是一個**標記介面（Marker Interface）**，本身沒有任何成員，只用來標記「這是一個授權要求」。邏輯完全在 Handler 裡，這樣做的好處是**一個 Requirement 可以有多個 Handler**（例如正式環境一種判斷邏輯，測試環境另一種）。

若要傳遞參數，可在 Requirement 裡加入屬性：

```csharp
// 有參數的 Requirement 範例
public class MinimumAgeRequirement : IAuthorizationRequirement
{
    public int MinimumAge { get; }
    public MinimumAgeRequirement(int minimumAge) => MinimumAge = minimumAge;
}
```

#### Step 2：實作 Handler

`Handlers/SameUserAuthorizationHandler.cs`:

```csharp
public class SameUserAuthorizationHandler
    : AuthorizationHandler<SameUserRequirement, Guid>
//                         ↑ 處理哪種 Requirement   ↑ Resource 的型別
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SameUserRequirement requirement,
        Guid resourceUserId)           // ← 就是我們在 Controller 傳進來的 userId
    {
        var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        var isSameUser = currentUserId == resourceUserId.ToString();
        var isAdmin    = context.User.IsInRole("Admin");

        if (isSameUser || isAdmin)
        {
            context.Succeed(requirement);  // ✅ 授權成功
        }
        // 不呼叫 context.Fail()，讓其他 Handler 有機會處理

        return Task.CompletedTask;
    }
}
```

> **`context.Succeed()` vs `context.Fail()`：**
> - `Succeed()`：標記為通過，但其他 Handler 仍會繼續執行
> - `Fail()`：立即終止，即使其他 Handler 呼叫 `Succeed()` 也無效（謹慎使用）
> - 都不呼叫：這個 Handler 棄權，由其他 Handler 決定

#### Step 3：在 Program.cs 註冊 Handler

```csharp
// 自定義 Handler 必須手動註冊到 DI，框架才能自動找到並執行
builder.Services.AddScoped<IAuthorizationHandler, SameUserAuthorizationHandler>();
```

#### Step 4：在 Controller 中用 IAuthorizationService 觸發

```csharp
public class AuthController(
    IAuthService authService,
    IAuthorizationService authorizationService   // ← 注入（框架內建，不需額外註冊）
) : ControllerBase

[Authorize]
[HttpPut("{userId:guid}/profile")]
public async Task<IActionResult> UpdateProfile(Guid userId, [FromBody] string newUsername)
{
    // 把 resource（userId）傳進去，Handler 會拿它跟 JWT 中的 userId 比對
    var authResult = await authorizationService.AuthorizeAsync(
        User,                      // 目前登入的用戶（ClaimsPrincipal）
        userId,                    // Resource（執行期才知道的值）
        new SameUserRequirement()  // 要滿足的 Requirement
    );

    if (!authResult.Succeeded)
        return Forbid();  // 403

    var user = await authService.UpdateUsernameAsync(userId, newUsername);
    if (user is null) return NotFound("User not found.");

    return Ok(user);
}
```

---

### 完整流程圖

```
PUT /api/auth/{userId}/profile（userId = Bob 的 ID）
請求者：Alice

  [Authorize] → 確認 Alice 已登入 ✓
       ↓
  進入 UpdateProfile 方法
       ↓
  authorizationService.AuthorizeAsync(Alice的User, Bob的userId, SameUserRequirement)
       ↓
  框架找到 SameUserAuthorizationHandler
       ↓
  HandleRequirementAsync(context, requirement, Bob的userId)
       ↓
  currentUserId = Alice 的 ID
  isSameUser   = Alice == Bob？→ false
  isAdmin      = Alice 是 Admin？→ false
       ↓
  不呼叫 Succeed() → authResult.Succeeded = false
       ↓
  return Forbid() → 403 Forbidden
```

---

### Claim 的讀取方式

在 Handler 或 Controller 中，從 `ClaimsPrincipal` 讀取 Claim：

```csharp
// 讀取單個值
string? name   = context.User.FindFirstValue(ClaimTypes.Name);
string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

// 讀取所有同型別的 Claim（例如多重角色）
var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

// 讀取自定義 Claim
string? department = context.User.FindFirstValue("department");

// 檢查 Claim 是否存在
bool hasEmail = context.User.HasClaim(c => c.Type == ClaimTypes.Email);
```

---

## 測試 API 端點

**場景一：用戶更新自己的資料（應成功）**

```
PUT /api/auth/{alice_userId}/profile
Authorization: Bearer {alice_token}

"alice_new_username"
```
預期：`200 OK`

**場景二：用戶更新別人的資料（應失敗）**

```
PUT /api/auth/{bob_userId}/profile
Authorization: Bearer {alice_token}

"hacked_username"
```
預期：`403 Forbidden`

**場景三：Admin 更新任何人的資料（應成功）**

```
PUT /api/auth/{bob_userId}/profile
Authorization: Bearer {admin_token}

"admin_changed_name"
```
預期：`200 OK`（Handler 中 `isAdmin = true`）

---

## 最佳實踐

**1. 授權邏輯放 Handler，Controller 只做觸發**

```csharp
// ❌ 不好：邏輯混在 Controller
if (currentId != userId.ToString() && !User.IsInRole("Admin")) return Forbid();

// ✅ 好：Controller 只管觸發
var result = await authorizationService.AuthorizeAsync(User, userId, new SameUserRequirement());
if (!result.Succeeded) return Forbid();
```

**2. Handler 中用 `NameIdentifier` 而非 `Name`（Username）**

```csharp
// ✅ 好：ID 是不可變的
var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

// ❌ 不好：Username 可能被修改
var currentUser = context.User.FindFirstValue(ClaimTypes.Name);
```

---

## 常見問題

### Q: 這和 `[Authorize(Roles = "Admin")]` 差在哪裡？

`[Authorize]` 是**靜態授權**：授權條件在編譯期就固定了，無法傳入執行期的資料。`IAuthorizationService` 是**動態授權**：可以在方法執行中的任何時間點，把任何物件當作 Resource 傳進去判斷。

### Q: 可以有多個 Handler 處理同一個 Requirement 嗎？

可以。所有已註冊的 Handler 都會被執行，任一 Handler 呼叫 `Succeed()` 就算通過。這讓你可以針對不同條件分別寫 Handler，保持單一職責。

---

## 進階話題預告

本分支的核心是 **Requirement + Handler + Runtime 授權**，適合需要判斷資源所有權的場景。下一分支將介紹：
- 📋 `10_policy_based_authorization`：用 `AddAuthorization()` 把授權規則**命名化、集中管理**，讓整個團隊都能用 `[Authorize(Policy = "...")]` 一行字引用複雜規則

---

## 參考資源

- [ASP.NET Core 資源授權文檔](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased)
- [IAuthorizationRequirement 介面](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationrequirement)
- [AuthorizationHandler 文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.authorizationhandler-1)
