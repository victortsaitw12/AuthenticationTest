# JWT 認證系統教學 - 第十一部分：IAuthorizationRequirementData 自訂授權特性

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 Branch 10 「具名 Policy 字串」帶來的問題
2. 使用 `IAuthorizationRequirementData`（.NET 8+ 新介面）讓 Attribute 直接攜帶授權資料
3. 實作同時繼承 `Attribute`、`IAuthorizationRequirement`、`IAuthorizationRequirementData` 的自訂特性
4. 撰寫對應的 `AuthorizationHandler<TAttribute>`
5. 使用 `AllowMultiple = true` 堆疊多個特性實現 AND 邏輯

---

> **前置知識：** 請先完成 `10_policy_based_authorization`，了解具名 Policy 和 `AddPolicy()` 的工作原理。本分支示範一種更進階的替代方案。

---

## 第十四步：IAuthorizationRequirementData 自訂授權特性

### Branch 10 留下的問題：魔術字串

Branch 10 的具名 Policy 把規則集中在 `Program.cs`，是很大的進步。但仍有一個痛點：**Policy 名稱是字串**。

```csharp
// Program.cs 定義
options.AddPolicy("ManagerOrAbove", policy =>
    policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

// Controller 使用
[Authorize(Policy = "ManagerOrAbove")]  // ← 這個字串必須和 Program.cs 完全一致
```

**這帶來幾個問題：**

| 問題 | 描述 |
|------|------|
| 魔術字串 | 字串拼錯不會有編譯錯誤，只有 Runtime 才會發現 |
| 雙重維護 | 每加一條規則，要同時改 Program.cs（定義）和 Controller（使用） |
| 命名爆炸 | 規則一多，`AddAuthorization()` 裡就塞滿了 `AddPolicy("...", ...)` |
| 跨越兩個地方 | 看到 Controller 上的 `[Authorize(Policy = "ManagerOrAbove")]`，要跑到 Program.cs 才知道這個 Policy 實際上做什麼 |

### IAuthorizationRequirementData 的解法

.NET 8 引入的 `IAuthorizationRequirementData` 介面，讓 Attribute 同時扮演三個角色：

```
一個 Attribute  ═══════════════════════════════════════
  │  繼承 Attribute              → 可以標在 Class/Method 上
  │  實作 IAuthorizationRequirement → 本身就是 Requirement，攜帶參數
  └  實作IAuthorizationRequirementData → 告訴框架「這個 Attribute 帶著哪些 Requirement」
```

**使用效果：**

```csharp
// ✅ 不需要在 Program.cs 定義 Policy
// ✅ 不需要字串，改用強型別的 enum
// ✅ 看到 Attribute 就知道規則是什麼

[MinimumRoleLevel(RoleLevel.Manager)]
[HttpGet("reports")]
public IActionResult GetReports() { ... }
```

---

## 實作步驟

### Step 1：定義 Attribute（同時是 Requirement 和 RequirementData）

**`Requirements/MinimumRoleLevelAttribute.cs`**（新建）

```csharp
using Microsoft.AspNetCore.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class MinimumRoleLevelAttribute
    : Attribute, IAuthorizationRequirement, IAuthorizationRequirementData
{
    public RoleLevel MinimumLevel { get; }

    public MinimumRoleLevelAttribute(RoleLevel minimumLevel)
    {
        MinimumLevel = minimumLevel;
    }

    // IAuthorizationRequirementData 的唯一方法：
    // 告訴框架「這個 Attribute 攜帶哪些 Requirement」
    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return this;  // Attribute 本身就是 Requirement
    }
}
```

**三個角色的職責：**

| 介面/基底類別 | 職責 |
|---|---|
| `Attribute` | 讓這個類別可以標在 Class 或 Method 上 |
| `IAuthorizationRequirement` | 讓它能作為授權需求，攜帶 `MinimumLevel` 參數 |
| `IAuthorizationRequirementData` | 讓框架知道「這個 Attribute 帶著哪些 Requirement」，不需要 `AddPolicy()` |

---

### Step 2：撰寫對應的 Handler

**`Handlers/MinimumRoleLevelAttributeHandler.cs`**（新建）

```csharp
using AuthenticationTest.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

public class MinimumRoleLevelAttributeHandler
    : AuthorizationHandler<MinimumRoleLevelAttribute>  // TRequirement = Attribute 本身
{
    private static readonly Dictionary<string, RoleLevel> RoleLevelMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "User",    RoleLevel.User    },
        { "Manager", RoleLevel.Manager },
        { "Admin",   RoleLevel.Admin   }
    };

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumRoleLevelAttribute requirement)  // requirement 就是那個 Attribute 本身
    {
        var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

        var highestLevel = userRoles
            .Where(r => RoleLevelMap.ContainsKey(r))
            .Select(r => RoleLevelMap[r])
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

**與 Branch 10 的 Handler 比較：**

```csharp
// Branch 10：Handler 處理 MinimumRoleLevelRequirement（純 Requirement 類別）
public class MinimumRoleLevelAuthorizationHandler
    : AuthorizationHandler<MinimumRoleLevelRequirement>

// Branch 11：Handler 處理 MinimumRoleLevelAttribute（Attribute 同時是 Requirement）
public class MinimumRoleLevelAttributeHandler
    : AuthorizationHandler<MinimumRoleLevelAttribute>
```

邏輯完全相同，只有 `TRequirement` 的型別不同。

---

### Step 3：在 Program.cs 註冊 Handler

```csharp
// 不需要 AddPolicy()！只需要註冊 Handler
builder.Services.AddScoped<IAuthorizationHandler, MinimumRoleLevelAttributeHandler>();
```

**與 Branch 10 的對比：**

```csharp
// Branch 10：需要在 AddAuthorization() 中定義每一條 Policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));
    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));
    // ... 每增加一條規則就要加一行
});

// Branch 11：只需要一行，規則直接定義在 Attribute 上
builder.Services.AddScoped<IAuthorizationHandler, MinimumRoleLevelAttributeHandler>();
```

---

### Step 4：在 Controller 使用 Attribute

```csharp
// 使用具名 Policy（Branch 10 的方式）
[Authorize(Policy = "ManagerOrAbove")]   // 字串，可能打錯
[HttpGet("reports")]
public IActionResult GetReports() { ... }

// 使用 IAuthorizationRequirementData Attribute（Branch 11 的方式）
[MinimumRoleLevel(RoleLevel.Manager)]    // 強型別 enum，編譯期檢查
[HttpGet("reports-v2")]
public IActionResult GetReportsV2() { ... }
```

---

## 框架的處理流程

當請求到達 `[MinimumRoleLevel(RoleLevel.Manager)]` 標注的端點時：

```
1. ASP.NET Core 讀取 Method 上的所有 Attribute
2. 發現 MinimumRoleLevelAttribute 實作了 IAuthorizationRequirementData
3. 呼叫 attribute.GetRequirements() → 返回 [ attribute 本身 ]
4. 把這些 Requirement 加入此次請求的授權評估中
5. 找到能處理 MinimumRoleLevelAttribute 的 Handler（MinimumRoleLevelAttributeHandler）
6. 呼叫 HandleRequirementAsync(context, attribute)
7. Handler 評估通過 → Succeed → 允許進入
```

不需要 Policy 名稱字串作為中間人。

---

## AllowMultiple = true：堆疊多個特性

因為宣告時加了 `AllowMultiple = true`，可以在同一個 Method 上堆疊多個 `[MinimumRoleLevel]`：

```csharp
// 堆疊 = AND 邏輯：兩個 Requirement 都必須通過
[MinimumRoleLevel(RoleLevel.Manager)]  // Requirement 1
[MinimumRoleLevel(RoleLevel.Admin)]    // Requirement 2
[HttpGet("strict-admin-v2")]
public IActionResult GetStrictAdminV2() { ... }
```

**執行邏輯：**

```
請求到達
  ├── Framework 找到兩個 MinimumRoleLevelAttribute
  ├── 兩個各自呼叫一次 HandleRequirementAsync
  ├── Requirement 1（Manager）：highestLevel >= Manager → Succeed
  └── Requirement 2（Admin）：highestLevel >= Admin → 只有 Admin 才能 Succeed
      ↓
  兩個都必須 Succeed，缺一則 403
```

實際上因為兩者取最高者，Manager 等級無法通過 Admin Requirement，所以效果等於只有 Admin 才能訪問。

---

## 完整端點對照（本分支新增的 v2 端點）

| 端點 | 授權方式 | 可訪問角色 |
|------|---------|-----------|
| `GET /api/auth/reports` | `[Authorize(Policy = "ManagerOrAbove")]` | Manager, Admin |
| `GET /api/auth/reports-v2` | `[MinimumRoleLevel(RoleLevel.Manager)]` | Manager, Admin |
| `GET /api/auth/system-config` | `[Authorize(Policy = "AdminOnly")]` | Admin |
| `GET /api/auth/system-config-v2` | `[MinimumRoleLevel(RoleLevel.Admin)]` | Admin |
| `GET /api/auth/strict-admin-v2` | `[MinimumRoleLevel(Manager)][MinimumRoleLevel(Admin)]` | Admin |

`reports` 和 `reports-v2` 的行為完全相同，只是授權宣告方式不同。

---

## AddPolicy vs IAuthorizationRequirementData 比較

| 面向 | `AddPolicy()` | `IAuthorizationRequirementData` |
|------|--------------|--------------------------------|
| 定義位置 | `Program.cs` 集中定義 | Attribute 本身攜帶 |
| 使用方式 | `[Authorize(Policy = "字串")]` | `[MinimumRoleLevel(RoleLevel.Manager)]` |
| 型別安全 | ❌ 字串，打錯不報錯 | ✅ 強型別 enum，編譯期檢查 |
| 可讀性 | 需跳到 Program.cs 查定義 | 看 Attribute 即知規則 |
| 可重用性 | ✅ Policy 名稱可跨多個 Controller | ✅ Attribute 可標在任何 Method |
| 組合邏輯 | 在 Policy 內用多個 `AddRequirements` | 堆疊多個 Attribute |
| 適合場景 | 複雜的多 Requirement 組合、需命名的業務規則 | 單一參數化規則、希望強型別、規則較少 |

**選擇建議：**

- 規則簡單、單一參數 → 用 `IAuthorizationRequirementData`
- 規則複雜（多個 Requirement AND 組合）→ 用具名 Policy
- 兩者可以**同時存在**，不互相排斥

---

## 測試 API 端點

準備三個用戶：`alice`（User）、`bob`（Manager）、`charlie`（Admin）

**`GET /api/auth/reports-v2` - MinimumRoleLevel(Manager)**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `200 OK` |
| charlie | `200 OK` |

**`GET /api/auth/system-config-v2` - MinimumRoleLevel(Admin)**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `403 Forbidden` |
| charlie | `200 OK` |

**`GET /api/auth/strict-admin-v2` - 堆疊兩個 Attribute**

| 用戶 | 預期 |
|------|------|
| alice | `403 Forbidden` |
| bob | `403 Forbidden` |
| charlie | `200 OK` |

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
| 9 | Claim-Based 授權 | Requirement + Handler + 資源授權（Runtime） |
| 10 | Policy-Based 授權 | 集中命名、多條件組合、FallbackPolicy |
| **11** | **IAuthorizationRequirementData** | **Attribute 攜帶授權資料、強型別、無需 Policy 字串** |

---

## 參考資源

- [ASP.NET Core 自訂授權原則](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [IAuthorizationRequirementData 介面文檔](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authorization.iauthorizationrequirementdata)
- [ASP.NET Core 授權概覽](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/introduction)
