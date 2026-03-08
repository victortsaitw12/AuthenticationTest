# JWT 認證系統教學 - 第六部分：基於角色的授權

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 ASP.NET Core 中的角色（Role）概念
2. 在 JWT Token 中添加 Role Claim
3. 實現基於角色的授權（Role-based Authorization）
4. 使用 [Authorize(Roles = "...")] 保護端點
5. 實現細粒度的權限控制

---

> **前置知識：** 請先完成前五個分支，特別是 `5_Secure_Endpoints` 分支，了解基本的認證和 [Authorize] 特性的工作原理。

---

## 第九步：基於角色的授權

本步驟講解如何在 JWT Token 中添加角色信息，並使用 [Authorize(Roles = "...")] 特性實現基於角色的端點保護。

### 什麼是角色（Role）？

角色是代表用戶權限級別的標籤。常見的角色包括：

| 角色 | 權限 | 示例端點 |
|------|------|--------|
| **Admin** | 最高權限，可管理系統 | DELETE /api/users/{id} |
| **Manager** | 中等權限，可管理內容 | PUT /api/users/{id} |
| **User** | 基本權限，只能訪問自己的資源 | GET /api/profile |
| **Guest** | 最低權限，僅讀取公開內容 | GET /api/public |

### 角色 vs 授權（Authorization）

**角色（Role）：**
- 是用戶的身份標籤
- 粗粒度的權限控制
- 簡單易懂，適合小規模應用
- 例如：[Authorize(Roles = "Admin")]

**授權（Authorization）：**
- 是精細化的權限控制
- 基於策略（Policy）的方式
- 靈活強大，適合複雜應用
- 例如：[Authorize(Policy = "AdminOnly")]

本分支主要講解基於角色的授權。

### 在 JWT Token 中添加角色

修改 `AuthService.cs` 的 `CreateToken()` 方法，添加角色 Claim：

```csharp
private string CreateToken(User user)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        // 新增：添加角色 Claim
        new Claim(ClaimTypes.Role, user.Role)
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(configuration.GetValue<string>("AppSettings:Token")!));

    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

    var tokenDescriptor = new JwtSecurityToken(
        issuer: configuration.GetValue<string>("AppSettings:Issuer"),
        audience: configuration.GetValue<string>("AppSettings:Audience"),
        claims: claims,
        expires: DateTime.UtcNow.AddDays(1),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
}
```

**關鍵改動：**

```csharp
new Claim(ClaimTypes.Role, user.Role)
```

- `ClaimTypes.Role` - 標準的角色 Claim 類型
- `user.Role` - 用戶的角色值（例如 "Admin"、"User" 等）

### 解析 Token 中的角色

當認證中間件驗證 Token 時，會自動從 Claim 中提取角色信息：

```
Token Payload
{
  "name": "alice",
  "nameid": "550e8400-e29b-41d4-a716-446655440000",
  "role": "Admin"        ← 角色信息
}
    ↓
HttpContext.User.IsInRole("Admin")  → true
HttpContext.User.FindFirst(ClaimTypes.Role).Value  → "Admin"
```

### [Authorize(Roles = "...")] 的工作原理

#### 基本用法

```csharp
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(Guid id)
{
    // 只有 Admin 角色的用戶可以訪問此端點
    await userService.DeleteUserAsync(id);
    return NoContent();
}
```

**工作流程：**

```
1. 請求到達 DeleteUser 端點
2. 認證中間件驗證 Token 和角色
3. 授權中間件檢查 [Authorize(Roles = "Admin")]
4. 檢查 HttpContext.User.IsInRole("Admin")
   ├─ True  → 繼續執行端點
   └─ False → 返回 403 Forbidden
```

#### 多個角色

允許多個角色訪問：

```csharp
[Authorize(Roles = "Admin,Manager")]
[HttpPut("{id}")]
public async Task<IActionResult> UpdateUser(Guid id, UserDto request)
{
    // Admin 或 Manager 角色可以訪問
    await userService.UpdateUserAsync(id, request);
    return Ok();
}
```

#### 無需認證的端點

```csharp
[HttpGet("public")]
public IActionResult GetPublicData()
{
    // 無 [Authorize] 特性，所有用戶都能訪問
    return Ok("This is public data");
}
```

### 完整的角色授權流程

#### 場景：使用者嘗試刪除用戶

**步驟 1：用戶使用管理員帳號登入**

```
POST /api/auth/login
{"username": "admin", "password": "adminpass"}

Response: JWT Token (包含 role: "Admin")
```

**步驟 2：用戶使用 Token 訪問受保護端點**

```
DELETE /api/users/550e8400-e29b-41d4-a716-446655440000
Authorization: Bearer eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9...
```

**步驟 3：認證中間件驗證 Token**

```
✓ 簽名有效
✓ Token 未過期
✓ Issuer 匹配
✓ Audience 匹配
✓ 提取 Claims，包括 role: "Admin"
```

**步驟 4：授權中間件檢查角色**

```
查詢 [Authorize(Roles = "Admin")]
檢查 HttpContext.User.IsInRole("Admin")  → True
✓ 允許訪問
```

**步驟 5：端點執行並返回**

```
200 OK
User deleted successfully
```

#### 場景：普通用戶嘗試刪除用戶

**步驟 1-2：同上，但 Token 包含 role: "User"**

**步驟 3-4：認證和授權**

```
✓ Token 有效
✓ 已認證，但 IsInRole("Admin") → False
✗ 角色不符合要求
```

**步驟 5：返回 403**

```
403 Forbidden
User does not have permission to delete users
```

### HttpContext.User 的角色檢查

在端點中檢查用戶的角色：

```csharp
[Authorize]
[HttpGet("dashboard")]
public IActionResult GetDashboard()
{
    // 檢查是否是 Admin
    if (HttpContext.User.IsInRole("Admin"))
    {
        return Ok(new { message = "Welcome Admin", data = GetAdminData() });
    }

    // 檢查是否是 Manager
    if (HttpContext.User.IsInRole("Manager"))
    {
        return Ok(new { message = "Welcome Manager", data = GetManagerData() });
    }

    // 默認 User 角色
    return Ok(new { message = "Welcome User", data = GetUserData() });
}
```

**HttpContext.User 相關方法：**

| 方法 | 說明 |
|------|------|
| `IsInRole(role)` | 檢查用戶是否具有指定角色 |
| `FindFirst(ClaimTypes.Role)` | 獲取第一個角色 Claim |
| `FindAll(ClaimTypes.Role)` | 獲取所有角色 Claim（支持多角色） |
| `Claims` | 獲取所有 Claims |

### 多角色支持

用戶可以擁有多個角色（例如既是 Admin 又是 Manager）：

```csharp
// 在 AuthService.CreateToken() 中添加多個角色
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.Username),
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Role, "Admin"),
    new Claim(ClaimTypes.Role, "Manager")  // 第二個角色
};
```

檢查多角色：

```csharp
[Authorize]
[HttpGet("report")]
public IActionResult GetReport()
{
    var roles = HttpContext.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

    if (roles.Contains("Admin") || roles.Contains("Manager"))
    {
        return Ok(GetReportData());
    }

    return Forbid();
}
```

### 與 [Authorize] 相比

| 特性 | [Authorize] | [Authorize(Roles = "...")] |
|------|-------------|---------------------------|
| 檢查內容 | 是否已認證 | 是否已認證且具有指定角色 |
| 未認證時 | 返回 401 | 返回 401 |
| 無角色時 | N/A | 返回 403 |
| 用途 | 基本認證 | 基於角色的授權 |

### 最佳實踐

**1. 在數據庫中存儲角色**
```csharp
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; }
    public string PasswordHash { get; set; }
    public string Role { get; set; }  // 存儲角色
}
```

**2. 在端點級別強制執行角色**
```csharp
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(Guid id) { ... }
```

**3. 提供清晰的錯誤信息**
```csharp
if (!HttpContext.User.IsInRole("Admin"))
{
    return Forbid("You do not have permission to perform this action");
}
```

**4. 避免在業務邏輯中檢查角色**
```csharp
// ❌ 不好：在 Service 層檢查角色
public class UserService
{
    public async Task DeleteUserAsync(Guid id, string userRole)
    {
        if (userRole != "Admin") throw new UnauthorizedAccessException();
        // ...
    }
}

// ✅ 好：在 Controller 層使用 [Authorize(Roles = ...)]
[Authorize(Roles = "Admin")]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(Guid id)
{
    // 直接調用 Service，因為授權已經在 Controller 層檢查
    await userService.DeleteUserAsync(id);
    return NoContent();
}
```

**5. 使用常量定義角色名稱**
```csharp
public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string User = "User";
    public const string Guest = "Guest";
}

// 使用常量而非字符串字面量
[Authorize(Roles = Roles.Admin)]
[HttpDelete("{id}")]
public async Task<IActionResult> DeleteUser(Guid id) { ... }
```

### 常見錯誤

**1. 忘記在 Token 中添加角色 Claim**
```csharp
// ❌ 錯誤：未添加角色
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.Username)
    // 缺少 Role Claim
};
```

**2. 角色名稱拼寫不一致**
```csharp
// ❌ 錯誤：Token 中是 "admin"，檢查時是 "Admin"
[Authorize(Roles = "Admin")]  // 大寫
// 但 Token 中是 role: "admin"  // 小寫
```

**3. 在 Roles 參數中使用不存在的角色**
```csharp
// ❌ 錯誤：角色名稱不匹配
[Authorize(Roles = "SuperAdmin")]
// 但系統中只有 "Admin"、"Manager" 等
```

---

## 關鍵概念總結

| 概念 | 說明 |
|------|------|
| **Role（角色）** | 代表用戶權限級別的標籤 |
| **Claim（聲明）** | JWT Token 中的鍵值對，包括角色信息 |
| **ClaimTypes.Role** | 標準的角色 Claim 類型 |
| **[Authorize(Roles = "...")]** | 基於角色的授權特性 |
| **IsInRole(role)** | 檢查用戶是否具有指定角色的方法 |
| **403 Forbidden** | 用戶已認證但無權訪問的 HTTP 狀態碼 |
| **多角色支持** | 用戶可以同時擁有多個角色 |
| **細粒度授權** | 在端點級別指定所需的角色 |

---

## 安全性提示

⚠️ **重要注意事項：**

1. **驗證角色的來源** - 僅信任 JWT Token 中的角色信息，不要信任客戶端發送的角色
2. **避免角色提升** - 防止普通用戶升級自己的角色
3. **記錄授權決策** - 記錄所有訪問受保護資源的嘗試
4. **定期審查權限** - 定期檢查用戶的角色分配
5. **最少權限原則** - 只授予用戶完成工作所需的最少角色

---

## 進階話題預告

後續分支將涵蓋：
- 🔑 基於策略的授權（Policy-based Authorization）
- 🔐 組合授權（多個條件組合）
- 📊 動態角色管理（從數據庫動態加載）
- 🛡️ 審計日誌（記錄所有授權決策）
- 👥 用戶管理系統（添加、刪除、修改角色）

---

## 參考資源

### 認證與授權
- [ASP.NET Core 授權](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/)
- [基於角色的授權](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [基於聲明的授權](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/claims)

### JWT 和安全
- [JWT 規範](https://tools.ietf.org/html/rfc7519)
- [OWASP 認證速查表](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)

### 最佳實踐
- [NIST 密碼指南](https://pages.nist.gov/800-63-3/sp800-63b.html)
- [應用安全檢查清單](https://cheatsheetseries.owasp.org/cheatsheets/Application_Security_Verification_Standard_Checklist.html)
