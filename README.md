# JWT 認證系統教學 - 第四部分：Service 層重構

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 MVC 分層架構中的 Service 層
2. 提取業務邏輯到 Service 層
3. 設計服務接口（IAuthService）
4. 實現依賴注入和服務註冊
5. 簡化 Controller 的職責

---

> **前置知識：** 請先完成前三個分支 (`1_Register_And_Login`、`2_Generate_JWT_Token`、`3_Connect_Database`)，了解基本的認證和數據庫實現。

---

## 第一步：Service 層重構

本步驟展示如何將業務邏輯從 Controller 提取到獨立的 Service 層，遵循 **SOLID 原則** 和 **關注點分離**。

### 為什麼需要 Service 層？

#### 問題分析

在前面的實現中，AuthController 直接包含所有業務邏輯：

```csharp
[HttpPost("login")]
public async Task<ActionResult<string>> Login(UserDto request)
{
    // ❌ 直接在 Controller 中進行業務邏輯
    var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

    if (user == null)
        return BadRequest("User not found.");

    if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return BadRequest("Wrong password.");

    var token = CreateToken(user);  // 還要生成 Token
    return Ok(token);
}
```

**存在的問題：**

| 問題 | 影響 |
|------|------|
| 業務邏輯混雜在 HTTP 層 | 難以單獨測試業務邏輯 |
| 認證邏輯無法復用 | 其他 Controller 無法使用 |
| 職責不清 | Controller 負責太多事情 |
| 難以擴展 | 添加新功能需要修改 Controller |
| 測試困難 | 無法使用 Mock 進行單元測試 |

#### 解決方案

創建 **Service 層** 來封裝業務邏輯：

```csharp
// ✅ Service 層獨立業務邏輯
var token = await authService.LoginAsync(request);
if (token == null)
    return BadRequest("Invalid username or password.");
return Ok(token);
```

**Service 層的優勢：**

✅ **單責原則** - 每個類只負責一件事
✅ **可測試性** - 可以為 Service 創建 Mock
✅ **可復用性** - 多個 Controller 可以使用同一個 Service
✅ **可維護性** - 業務邏輯集中在一個地方
✅ **可擴展性** - 易於添加新功能

### 設計 Service 接口

服務接口定義了業務邏輯層的契約。

**Services/IAuthService.cs**：

```csharp
using AuthenticationTest.Entities;
using AuthenticationTest.Models;

namespace AuthenticationTest.Services
{
    public interface IAuthService
    {
        // 註冊新用戶
        // 返回 User? - 成功返回 User，失敗返回 null
        Task<User?> RegisterAsync(UserDto request);

        // 用戶登入
        // 返回 string? - 成功返回 JWT Token，失敗返回 null
        Task<string?> LoginAsync(UserDto request);
    }
}
```

**接口設計要點：**

1. **可空返回類型 (T?)**
   ```csharp
   Task<User?>    // 返回用戶或 null
   Task<string?>  // 返回 Token 或 null
   ```
   - 使用可空類型明確表示可能失敗
   - 無需拋出異常處理

2. **非同步方法**
   ```csharp
   Task<T>        // 標準非同步模式
   ```
   - 適應非同步數據庫操作
   - 提高應用程式性能

3. **簡潔的方法簽名**
   - 只暴露必要的方法
   - 隱藏實現細節

### 實現 Service 類

**Services/AuthService.cs**：

```csharp
using AuthenticationTest.Data;
using AuthenticationTest.Entities;
using AuthenticationTest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthenticationTest.Services
{
    public class AuthService(UserDbContext context, IConfiguration configuration) : IAuthService
    {
        // 用戶登入
        public async Task<string?> LoginAsync(UserDto request)
        {
            // 1. 從數據庫查詢用戶
            var user = await context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user is null)
                return null;  // 用戶不存在

            // 2. 驗證密碼
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return null;  // 密碼錯誤

            // 3. 生成 JWT Token
            return CreateToken(user);
        }

        // 用戶註冊
        public async Task<User?> RegisterAsync(UserDto request)
        {
            // 1. 檢查用戶名是否已存在
            if (await context.Users.AnyAsync(u => u.Username == request.Username))
                return null;  // 用戶名已存在

            // 2. 創建新用戶
            var user = new User();
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            user.Username = request.Username;
            user.PasswordHash = hashedPassword;

            // 3. 保存到數據庫
            context.Add(user);
            await context.SaveChangesAsync();

            return user;
        }

        // 生成 JWT Token
        private string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                // 新增：包含用戶 ID 在 Token 中
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
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
    }
}
```

**實現要點：**

1. **依賴注入構造函數**
   ```csharp
   public class AuthService(UserDbContext context, IConfiguration configuration) : IAuthService
   ```
   - 通過構造函數注入依賴
   - 符合依賴注入模式

2. **方法實現**
   - `LoginAsync()` - 查詢、驗證、生成 Token
   - `RegisterAsync()` - 檢查、創建、保存用戶
   - `CreateToken()` - 生成 JWT Token

3. **改進的 Claims**
   ```csharp
   new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
   ```
   - 除了用戶名，還包含用戶 ID
   - 方便後續識別用戶身份

### 簡化 Controller

移除業務邏輯，只保留 HTTP 端點的職責。

**Controllers/AuthController.cs**：

```csharp
using AuthenticationTest.Entities;
using AuthenticationTest.Models;
using AuthenticationTest.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuthenticationTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IAuthService authService) : ControllerBase
    {
        [HttpPost("register")]
        public async Task<ActionResult<User>> Register(UserDto request)
        {
            // 1. 調用 Service 進行註冊
            var user = await authService.RegisterAsync(request);

            // 2. 檢查結果
            if (user is null)
                return BadRequest("Username already existed.");

            // 3. 返回結果
            return Ok(user);
        }

        [HttpPost("login")]
        public async Task<ActionResult<string>> Login(UserDto request)
        {
            // 1. 調用 Service 進行登入
            var token = await authService.LoginAsync(request);

            // 2. 檢查結果
            if (token is null)
                return BadRequest("Invalid username or password.");

            // 3. 返回 Token
            return Ok(token);
        }
    }
}
```

**改進對比：**

| 項目 | 原始版本 | 重構後 |
|------|--------|------|
| 行數 | 70 行 | 46 行 |
| 職責 | 業務邏輯 + HTTP 層 | 只負責 HTTP 層 |
| 可測試性 | 困難 | 容易（可 Mock Service） |
| 代碼復用 | 不可能 | 支持多個 Controller 使用 |

### 註冊服務到依賴注入容器

在 **Program.cs** 中註冊 Service：

```csharp
using AuthenticationTest.Data;
using AuthenticationTest.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 註冊 DbContext
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 📌 新增：註冊 Service
builder.Services.AddScoped<IAuthService, AuthService>();

var app = builder.Build();
// ...
```

**AddScoped 說明：**

```csharp
builder.Services.AddScoped<IAuthService, AuthService>();
```

- **AddScoped** - 每個 HTTP 請求創建一個新實例
- **<IAuthService, AuthService>** - 將接口映射到實現類
- 依賴注入容器自動解析依賴

**Service 生命周期詳解：**

| 生命周期 | 說明 | 使用場景 |
|---------|------|---------|
| `AddSingleton` | 全應用程式只有一個實例 | 配置、緩存 |
| `AddScoped` | 每個 HTTP 請求一個實例 | Service、DbContext |
| `AddTransient` | 每次注入創建新實例 | 臨時對象 |

本例使用 `AddScoped` 因為：
- 每個 HTTP 請求應該有獨立的 DbContext
- Service 需要訪問 DbContext 進行數據操作

### Service 層的依賴流程

```
HTTP 請求
    ↓
AuthController (HTTP 層)
    ↓ 注入 IAuthService
AuthService (業務邏輯層)
    ↓ 注入 UserDbContext
UserDbContext (數據訪問層)
    ↓
SQL Server 數據庫
```

### 分層架構好處

**1. 關注點分離（Separation of Concerns）**
- Controller: 處理 HTTP 請求和響應
- Service: 實現業務邏輯
- DbContext: 進行數據訪問

**2. 易於測試**
```csharp
// 可以輕易創建 Mock Service 進行單元測試
var mockAuthService = new Mock<IAuthService>();
mockAuthService.Setup(s => s.LoginAsync(It.IsAny<UserDto>()))
    .ReturnsAsync("fake-token");

// 然後測試 Controller
var controller = new AuthController(mockAuthService.Object);
```

**3. 代碼復用**
```csharp
// AdminController 也可以使用相同的 Service
public class AdminController(IAuthService authService) : ControllerBase
{
    // 可以調用 authService 的方法
}
```

**4. 易於擴展**
```csharp
// 可以創建多個 Service 實現
public class LdapAuthService : IAuthService
{
    // 使用 LDAP 進行認證
}

// 只需要改變依賴注入的配置
builder.Services.AddScoped<IAuthService, LdapAuthService>();
```

### 常見設計模式

**1. 依賴注入（Dependency Injection）**
```csharp
// Controller 不自己創建 Service，而是請求注入
public class AuthController(IAuthService authService) : ControllerBase
{
    // authService 由依賴注入容器提供
}
```

**2. 接口隔離原則（Interface Segregation Principle）**
```csharp
// Service 通過接口暴露，Controller 面向接口編程
public interface IAuthService
{
    Task<User?> RegisterAsync(UserDto request);
    Task<string?> LoginAsync(UserDto request);
}
```

**3. 控制反轉（Inversion of Control）**
```csharp
// 不是 Controller 主動控制 Service 的生命周期
// 而是由依賴注入容器管理
builder.Services.AddScoped<IAuthService, AuthService>();
```

---

## 最佳實踐

**1. Service 層只應包含業務邏輯**
```csharp
// ✅ 好：Service 層專注於業務邏輯
public async Task<string?> LoginAsync(UserDto request)
{
    var user = await context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return null;
    return CreateToken(user);
}

// ❌ 不好：Service 層不應該處理 HTTP 相關邏輯
public IActionResult LoginEndpoint(UserDto request)
{
    var token = LoginAsync(request).Result;
    return Ok(token);  // 這應該在 Controller 中
}
```

**2. Controller 只應該處理 HTTP 相關邏輯**
```csharp
// ✅ 好：Controller 專注於 HTTP 邏輯
[HttpPost("login")]
public async Task<ActionResult<string>> Login(UserDto request)
{
    var token = await authService.LoginAsync(request);
    if (token is null)
        return BadRequest("Invalid username or password.");
    return Ok(token);
}

// ❌ 不好：不要在 Controller 中包含業務邏輯
[HttpPost("login")]
public async Task<ActionResult<string>> Login(UserDto request)
{
    var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
    if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return BadRequest("...");
    // ... 更多業務邏輯
}
```

**3. 使用可空返回類型表示失敗**
```csharp
// ✅ 好：用 null 表示失敗
public async Task<User?> RegisterAsync(UserDto request)
{
    if (await context.Users.AnyAsync(u => u.Username == request.Username))
        return null;  // 用戶名已存在
    // ...
}

// ⚠️ 可以接受但不如上面清晰：拋出異常
public async Task<User> RegisterAsync(UserDto request)
{
    if (await context.Users.AnyAsync(u => u.Username == request.Username))
        throw new InvalidOperationException("Username already exists");
    // ...
}
```

**4. Service 接口應該簡潔**
```csharp
// ✅ 好：接口簡潔，只暴露必要的方法
public interface IAuthService
{
    Task<User?> RegisterAsync(UserDto request);
    Task<string?> LoginAsync(UserDto request);
}

// ❌ 不好：接口過大，暴露太多實現細節
public interface IAuthService
{
    Task<User?> RegisterAsync(UserDto request);
    Task<string?> LoginAsync(UserDto request);
    Task<User?> GetUserByIdAsync(Guid id);
    Task UpdateUserAsync(User user);
    Task DeleteUserAsync(Guid id);
    Task<List<User>> GetAllUsersAsync();
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
    string CreateToken(User user);
}
```

---

## 安全性提示

⚠️ **重要注意事項：**

1. **永遠不要存儲明文密碼** - 即使是系統管理員也不應該看到用戶密碼
2. **永遠不要從雜湊值還原密碼** - 這在密碼學上是不可能的（理論上）
3. **使用 HTTPS** - 在生產環境中必須使用 HTTPS 加密傳輸
4. **實現速率限制** - 防止暴力攻擊登入接口
5. **添加日誌和監控** - 記錄失敗的登入嘗試
6. **保護連接字符串** - 生產環境使用環境變數或 Azure Key Vault，不要提交到 Git

---

## 進階話題預告

後續分支將涵蓋：
- 🛡️ API 認證中間件的實現（驗證 Token）
- 🔐 角色和授權（Role-based Authorization）
- 📊 刷新 Token 機制（防止 Token 過期）
- 🔑 策略性授權（Policy-based Authorization）
- 🧪 單元測試和 Mock（測試 Service 層）

---

## 參考資源

### 設計模式和原則
- [SOLID 原則](https://en.wikipedia.org/wiki/SOLID)
- [依賴注入模式](https://martinfowler.com/articles/injection.html)
- [ASP.NET Core 依賴注入](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)

### 認證與安全
- [BCrypt.Net-Next GitHub](https://github.com/BcryptNet/bcrypt.net-next)
- [OWASP 認證速查表](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)

### 非同步程式設計
- [async/await 最佳實踐](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Task 基礎知識](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task)
