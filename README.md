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

## 第一步：安裝必要的 NuGet 套件

### 安裝 Scalar

**Scalar** 是一個現代化的 API 文檔工具，用來替代傳統的 Swagger UI。它提供更美觀的界面和更好的開發體驗。

```bash
dotnet add package Scalar.AspNetCore
```

在 `Program.cs` 中進行配置：

```csharp
// 添加 OpenAPI 支持
builder.Services.AddOpenApi();

// 在開發環境中啟用 OpenAPI 和 Scalar
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

### 安裝 BCrypt.Net-Next

**BCrypt** 是一個密碼雜湊庫，專門為安全存儲密碼而設計。與簡單的 SHA256 等算法不同，BCrypt 使用自適應的工作因子，隨著計算機性能提升而自動調整難度，確保長期安全性。

```bash
dotnet add package BCrypt.Net-Next
```

**為什麼使用 BCrypt？**
- ✅ 自動添加 salt，防止彩虹表攻擊
- ✅ 工作因子可調整，隨著硬件進步而增加難度
- ✅ 計算速度慢（意圖），使暴力攻擊不可行
- ❌ 不要使用：簡單的 MD5、SHA1 或 SHA256（無 salt）

### 安裝 JWT Token 相關套件

```bash
dotnet add package System.IdentityModel.Tokens.Jwt
```

**System.IdentityModel.Tokens.Jwt** 是 Microsoft 提供的 JWT 處理庫，用於生成和驗證 JWT Token。JWT（JSON Web Token）是一種無狀態的身份驗證方式，常用於 API 認證。

---

## 第二步：設計用戶數據模型

### User 實體類

`Entities/User.cs` - 代表數據庫中的用戶記錄：

```csharp
namespace AuthenticationTest.Entities
{
    public class User
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }
}
```

**重要概念：**
- `Username` - 用戶名，用於識別用戶
- `PasswordHash` - 密碼的雜湊值，**永遠不存儲明文密碼**

### UserDto（數據傳輸物件）

`Models/UserDto.cs` - 用於 API 請求的數據模型：

```csharp
namespace AuthenticationTest.Models
{
    public class UserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
```

**為什麼要分離 Entity 和 DTO？**
- 🔒 DTO 接收明文密碼，Entity 只存儲雜湊值
- 🛡️ 避免直接暴露數據庫結構給客戶端
- 🔄 在複雜系統中實現數據驗證和轉換的邏輯分離

---

## 第三步：實現註冊 API

### 註冊端點代碼

`Controllers/AuthController.cs`：

```csharp
[HttpPost("register")]
public ActionResult<User> Register(UserDto request)
{
    // 使用 BCrypt 將明文密碼轉換為雜湊值
    var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

    // 將用戶信息存儲到數據庫（此例中為靜態變數）
    user.Username = request.Username;
    user.PasswordHash = hashedPassword;

    // 返回成功響應
    return Ok(user);
}
```

### 使用示例

**使用 Scalar 或 cURL 測試：**

```bash
curl -X POST https://localhost:7XXX/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"john_doe","password":"SecurePassword123!"}'
```

**響應示例：**

```json
{
  "username": "john_doe",
  "passwordHash": "$2a$11$sR9t5PyQKu4aDO.65huXbOpS5PxLLlwAXyVVZ..."
}
```

### 密碼雜湊的原理

BCrypt 的 `HashPassword()` 方法會**自動處理 salt**：

```csharp
var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
// 這一行會自動：
// 1. 生成隨機 salt
// 2. 將 salt 與密碼組合並雜湊
// 3. 返回包含 salt 的完整雜湊字符串
```

**內部流程：**
1. **自動生成隨機 Salt** - BCrypt 在內部生成 16 字節的隨機 salt
2. **多輪迭代** - 默認 11 輪（成本因子），使計算變慢，防止暴力攻擊
3. **返回完整雜湊** - 格式為 `$2a$11$salt_hash_combined_value`

**格式解釋：**
```
$2a           <- BCrypt 版本
$11           <- 成本因子（工作輪數）
$sR9t5PyQKu4aDO.65huXb  <- Salt（22 個字符）
OpS5PxLLlwAXyVVZ...     <- 實際雜湊值
```

**關鍵點：Salt 已經包含在返回值中！**

**範例 - 同一密碼的兩次雜湊：**
```
明文密碼: "password123"
第一次雜湊: $2a$11$sR9t5PyQKu4aDO.65huXbOpS5PxLLlwAXyVVZ...
第二次雜湊: $2a$11$K4aTTOMfsVeFzT5lIB.XnuK1DkSGeHQjDw3QN...
（看起來完全不同，因為每次都生成不同的隨機 salt）
```

**為什麼相同密碼產生不同的雜湊值？**
- 每次調用 `HashPassword()` 都會生成不同的隨機 salt
- 即使密碼完全相同，salt 不同就會產生完全不同的雜湊值
- 這防止了「彩虹表」攻擊（預先計算的密碼雜湊表）

---

## 第四步：實現登入 API

### 登入端點代碼

```csharp
[HttpPost("login")]
public ActionResult<string> Login(UserDto request)
{
    // 驗證用戶名是否存在
    if (user.Username != request.Username)
    {
        return BadRequest("User not found.");
    }

    // 驗證密碼（不直接比較，使用 BCrypt.Verify）
    if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return BadRequest("Wrong password.");
    }

    // 登入成功，生成 JWT Token
    var token = CreateToken(user);
    return Ok(token);
}
```

### 使用示例

**正確的密碼：**

```bash
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john_doe","password":"SecurePassword123!"}'
```

**響應：** `200 OK` - 返回 JWT Token
```
eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy8yMDAzLzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiam9obl9kb2UiLCJpc3MiOiJNeUFwcCIsImF1ZCI6Ik15QXBwVXNlcnMiLCJleHAiOjE3MDk5NDEyMDB9.aBcDeFgHiJkLmNoPqRsTuVwXyZ...
```

**錯誤的密碼：**

```bash
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john_doe","password":"WrongPassword"}'
```

**響應：** `400 Bad Request - "Wrong password."`

### 密碼驗證的原理

BCrypt 的 `Verify()` 方法會從雜湊值中**自動提取 salt**：

```csharp
BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash)
// 這一行會：
// 1. 從 user.PasswordHash 中提取 salt
// 2. 用提取的 salt 對輸入密碼進行雜湊
// 3. 比較新雜湊值與存儲的雜湊值
```

**驗證流程圖：**
```
用戶登入輸入: "password123"
         ↓
   提取存儲的雜湊值中的 salt
         ↓
   用相同 salt 對新密碼進行雜湊
         ↓
   比較新雜湊值 vs 存儲的雜湊值
         ↓
   相等? ✅ 登入成功 : ❌ 密碼錯誤
```

**為什麼這個方法安全？**
- 不需要逆向雜湊值（在密碼學上是不可能的）
- 只需要對輸入密碼進行同樣的雜湊操作
- BCrypt 自動從儲存的值中提取 salt，確保兩次計算用的 salt 相同

**範例：**
```
存儲的雜湊: $2a$11$sR9t5PyQKu4aDO.65huXbOpS5PxLLlwAXyVVZ...
                       ↑ Salt 就包含在這裡

用戶輸入 "password123"
→ Verify() 提取 salt: sR9t5PyQKu4aDO.65huXb
→ 用這個 salt 對 "password123" 雜湊
→ 得到新雜湊: $2a$11$sR9t5PyQKu4aDO.65huXbOpS5PxLLlwAXyVVZ...
→ 與存儲值比較 → 完全相同 ✅ 成功！

如果用戶輸入 "wrongpassword"
→ Verify() 提取相同的 salt
→ 用同樣 salt 對 "wrongpassword" 雜湊
→ 得到不同雜湊: $2a$11$sR9t5PyQKu4aDO.65huXbDifferentHashValue...
→ 與存儲值比較 → 不同 ❌ 失敗！
```

---

## 第五步：生成 JWT Token

### 什麼是 JWT？

**JWT（JSON Web Token）** 是一種緊湊、自包含的令牌格式，用於安全傳輸用戶身份信息。JWT 由三個部分組成，用點號（.）分隔：

```
[Header].[Payload].[Signature]
```

**範例：**
```
eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9
.
eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy8yMDAzLzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiam9obiIsImlzcyI6Ik15QXBwIn0
.
aBcDeFgHiJkLmNoPqRsTuVwXyZ1a2b3c4d5e6f7g8h9i0jk1lm2no3pq4rs5tu6vw7xy8z
```

**三個部分的含義：**

1. **Header（標頭）** - Base64 編碼的 JSON
   ```json
   {
     "alg": "HS512",      // 簽名算法
     "typ": "JWT"         // Token 類型
   }
   ```

2. **Payload（負載）** - Base64 編碼的 JSON，包含 Claims（聲明）
   ```json
   {
     "http://schemas.xmlsoap.org/2003/05/identity/claims/name": "john",
     "iss": "MyApp",                  // Issuer（發行者）
     "aud": "MyAppUsers",             // Audience（受眾）
     "exp": 1709941200               // Expiration（過期時間，Unix 時間戳）
   }
   ```

3. **Signature（簽名）** - 用密鑰和算法對前兩部分進行簽名
   - 確保 Token 未被篡改
   - 只有知道密鑰的服務器才能生成有效的簽名

### JWT Token 配置（appsettings.json）

```json
{
  "AppSettings": {
    "Token": "YourSecretKeyMustBeAtLeast64BytesLongForHmacSha512AlgorithmSuperSecure!",
    "Issuer": "MyApp",
    "Audience": "MyAppUsers"
  }
}
```

**配置說明：**
- **Token** - 用於簽名的密鑰，必須足夠長（HmacSha512 至少 64 字節）
  - ⚠️ **重要：** 在生產環境中不應該寫在代碼中，應使用環境變數或 Secrets Manager
- **Issuer** - 發行 Token 的應用程式名稱
- **Audience** - 這個 Token 的接收方

### CreateToken 方法的實現

`Controllers/AuthController.cs` - Token 生成方法：

```csharp
private string CreateToken(User user)
{
    // 1. 創建 Claims（聲明），存儲用戶身份信息
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.Name, user.Username)
    };

    // 2. 從配置讀取密鑰
    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(configuration.GetValue<string>("AppSettings:Token")!));

    // 3. 創建簽名憑證（指定算法：HmacSha512）
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

    // 4. 構建 Token 對象
    var tokenDescriptor = new JwtSecurityToken(
        issuer: configuration.GetValue<string>("AppSettings:Issuer"),
        audience: configuration.GetValue<string>("AppSettings:Audience"),
        claims: claims,
        expires: DateTime.UtcNow.AddDays(1),          // Token 有效期：1 天
        signingCredentials: creds                       // 簽名憑證
    );

    // 5. 序列化為 JWT 字符串
    return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
}
```

### 工作原理詳解

**第一步：創建 Claims**
```csharp
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.Username)
};
```
- Claims 是 Payload 中的鍵值對，存儲用戶信息
- `ClaimTypes.Name` 是用戶名的標準聲明類型
- 未來可添加其他 Claims：角色、權限、郵件等

**第二步：讀取密鑰並創建簽名憑證**
```csharp
var key = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(configuration.GetValue<string>("AppSettings:Token")!));
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
```
- 密鑰用於簽名，確保 Token 來自可信源
- HmacSha512 是加密安全的簽名算法

**第三步：構建並簽名 Token**
```csharp
var tokenDescriptor = new JwtSecurityToken(
    issuer: "MyApp",
    audience: "MyAppUsers",
    claims: claims,
    expires: DateTime.UtcNow.AddDays(1),
    signingCredentials: creds
);
```
- 設置發行者、受眾、Claims 和過期時間
- 使用簽名憑證對 Token 進行簽名

**第四步：序列化為字符串**
```csharp
return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
```
- 轉換為 `header.payload.signature` 格式的字符串
- 客戶端在後續請求中發送這個 Token

### Token 流程圖

```
用戶登入
   ↓
驗證用戶名和密碼
   ↓
密碼正確
   ↓
生成 Claims（包含用戶信息）
   ↓
使用密鑰和 HmacSha512 算法簽名
   ↓
返回 JWT Token 字符串給客戶端
   ↓
客戶端存儲 Token（通常在 localStorage 或 Cookie）
   ↓
後續請求在 Authorization Header 中發送 Token
   ↓
服務器驗證簽名，確認 Token 有效且未被篡改
```

### Claims 的概念

Claims 是 JWT 中存儲的身份信息，格式為 `Key: Value`：

**常見 Claims（標準類型）：**
| ClaimTypes | 含義 |
|-----------|------|
| `ClaimTypes.Name` | 用戶名 |
| `ClaimTypes.Email` | 郵件地址 |
| `ClaimTypes.Role` | 用戶角色 |
| `ClaimTypes.DateOfBirth` | 出生日期 |

**在本例中：**
```csharp
new Claim(ClaimTypes.Name, user.Username)
```
- Key：`http://schemas.xmlsoap.org/2003/05/identity/claims/name`（ClaimTypes.Name 的完整值）
- Value：`john`（用戶的用戶名）

**Token 過期時間的重要性：**
```csharp
expires: DateTime.UtcNow.AddDays(1)
```
- Token 有效期設為 1 天
- 客户端無法延長 Token 有效期（Token 中包含過期時間，無法修改而不破壞簽名）
- 當 Token 過期時，用戶需要重新登入或使用刷新 Token（後續分支會講解）

### 密鑰長度的重要性

```csharp
// HmacSha512 對密鑰長度有要求
// 至少 128 位（16 字節）用於 HS256
// 至少 256 位（32 字節）用於 HS384
// 至少 512 位（64 字節）用於 HS512
```

**本專案配置：**
- 使用 HmacSha512，要求密鑰至少 64 字節
- appsettings.json 中的 Token 值正好符合此要求

---

## 測試 API 端點

### 使用 Scalar 界面測試

1. 啟動應用程序
2. 導航到 `https://localhost:7XXX` 查看 Scalar 文檔
3. 在 UI 中找到 `/api/auth/register` 和 `/api/auth/login`
4. 點擊「Try it」按鈕進行互動式測試

### 完整測試流程

**第一步：註冊用戶**

```json
POST /api/auth/register
{
  "username": "alice",
  "password": "MyPassword123!"
}
```

**第二步：嘗試用正確密碼登入**

```json
POST /api/auth/login
{
  "username": "alice",
  "password": "MyPassword123!"
}
```

**預期結果：** ✅ `200 OK` - 返回 JWT Token
```
eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy8yMDAzLzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiYWxpY2UiLCJpc3MiOiJNeUFwcCIsImF1ZCI6Ik15QXBwVXNlcnMiLCJleHAiOjE3MDk5NDEyMDB9.aBcDeFgHiJkLmNoPqRsTuVwXyZ1a2b3c4d5e6f7g8h9i0jk1lm2no3pq4rs5tu6vw7xy8z
```

**Token 構成說明：**
- 前 2 部分用點號分隔：`[Header].[Payload]`
- 最後一部分是簽名，確保 Token 未被篡改
- 客戶端應將這個 Token 存儲，用於後續請求的身份驗證

**第三步：嘗試用錯誤密碼登入**

```json
POST /api/auth/login
{
  "username": "alice",
  "password": "WrongPassword"
}
```

**預期結果：** ❌ `400 Bad Request - "Wrong password."`

---

## 關鍵概念總結

| 概念 | 說明 |
|------|------|
| **密碼雜湊** | 將密碼轉換為不可逆的字符串，只能驗證而不能還原 |
| **Salt** | 隨機數據，用於確保相同密碼產生不同的雜湊值 |
| **BCrypt** | 專為密碼安全設計的雜湊算法，包含自適應成本因子 |
| **DTO** | 數據傳輸物件，用於分離 API 請求模型和數據庫實體模型 |
| **Scalar** | 現代化的 API 文檔和測試工具 |
| **JWT Token** | JSON Web Token，由 Header、Payload、Signature 三部分組成的身份驗證令牌 |
| **Claims** | JWT 中存儲的身份信息（如用戶名、角色等）的鍵值對 |
| **Issuer** | 發行 JWT Token 的應用程式（通常是自己的後端服務） |
| **Audience** | JWT Token 的預期接收方 |
| **Signature** | JWT 的簽名部分，用密鑰和算法對前兩部分進行簽名，確保 Token 未被篡改 |
| **Entity Framework Core** | .NET 的 ORM（物件關聯映射）框架，簡化資料庫操作 |
| **DbContext** | EF Core 的核心類，代表與資料庫的連接會話 |
| **DbSet<T>** | 代表資料庫中的一個表，提供查詢和修改操作 |
| **Migration** | EF Core 的遷移機制，用於管理資料庫結構的版本控制 |
| **主鍵（Primary Key）** | 唯一標識實體的欄位（本例中使用 Guid） |
| **LINQ** | Language Integrated Query，在 C# 中進行資料庫查詢的語言特性 |
| **非同步操作** | 使用 `async/await` 進行非阻塞的資料庫操作，提高應用程式效能 |
| **Service 層** | 封裝業務邏輯的層級，位於 Controller 和 DbContext 之間 |
| **依賴注入** | 由容器管理物件的創建和生命周期，而不是手動創建 |
| **接口隔離** | 使用接口定義服務契約，面向接口編程而不是實現 |
| **可空類型** | 使用 `T?` 表示可能為 null 的值類型，提高代碼安全性 |
| **分層架構** | 將應用程式分為多個層級（Controller、Service、DbContext），各司其職 |
| **Scoped 生命周期** | 每個 HTTP 請求創建一個新的服務實例，請求結束後銷毀 |
| **SOLID 原則** | 軟體設計的五個原則，提高代碼可維護性和可擴展性 |

---

## 第六步：連接資料庫（Entity Framework Core）

本分支的重點是用 **Entity Framework Core** 替代之前使用的靜態變數，實現真正的資料庫持久化。

### 為什麼需要資料庫？

在之前的分支中，用戶數據只存儲在靜態變數中：
```csharp
public static User user = new User();  // ❌ 不能持久化
```

這存在以下問題：
- ❌ 應用程式重啟後數據丟失
- ❌ 只能存儲一個用戶
- ❌ 不支持多用戶並發
- ❌ 無法進行複雜的數據查詢

使用資料庫可以解決所有問題：
- ✅ 數據持久化（應用程式重啟後保留）
- ✅ 支持多用戶存儲
- ✅ 支持複雜查詢和過濾
- ✅ 生產環境的最佳實踐

### 安裝 Entity Framework Core

```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools
```

**三個套件的用途：**

| 套件 | 用途 |
|------|------|
| `Microsoft.EntityFrameworkCore` | EF Core 核心庫，提供 DbContext 和基本功能 |
| `Microsoft.EntityFrameworkCore.SqlServer` | SQL Server 提供者，用於連接 SQL Server |
| `Microsoft.EntityFrameworkCore.Tools` | 命令行工具，用於生成遷移和更新資料庫 |

### 創建 DbContext

**Entity Framework Core** 的核心是 **DbContext** 類，它代表與資料庫的連接。

`Data/UserDbContext.cs`：

```csharp
using AuthenticationTest.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationTest.Data
{
    public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
    {
        // 這個 DbSet 代表資料庫中的 Users 表
        public DbSet<User> Users { get; set; }
    }
}
```

**DbContext 的重要概念：**

- **DbContext** - 代表與資料庫的會話
- **DbSet<T>** - 代表資料庫中的一個表，可進行查詢和修改
- `public DbSet<User> Users { get; set; }` - 定義 Users 表，對應 User 實體

**DbContext 的工作流程：**

```
DbContext 實例
    ↓
定義與資料庫的連接
    ↓
追蹤實體更改
    ↓
生成 SQL 查詢
    ↓
執行 CRUD 操作
```

### 更新 User 實體

User 實體需要添加主鍵（Primary Key）：

```csharp
namespace AuthenticationTest.Entities
{
    public class User
    {
        public Guid Id { get; set; }                    // 主鍵，唯一標識用戶
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
    }
}
```

**為什麼需要主鍵？**
- EF Core 需要每個實體有唯一的標識
- `Guid` 是一個 128 位的唯一識別碼，比整數更適合分佈式系統
- 主鍵用於在資料庫中唯一標識每個用戶

### 配置資料庫連接字符串

在 `appsettings.json` 中添加連接字符串：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=AuthenticationTestDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

**連接字符串參數說明：**

| 參數 | 說明 |
|------|------|
| `Server` | 資料庫服務器位置。`(localdb)\\mssqllocaldb` 是本地開發用的 SQL Server LocalDB |
| `Database` | 資料庫名稱，EF Core 會自動建立 |
| `Trusted_Connection` | 使用 Windows 認證（開發環境推薦） |
| `MultipleActiveResultSets` | 允許多個活動結果集，提高效能 |

### 在 Program.cs 中註冊 DbContext

```csharp
using AuthenticationTest.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 添加服務
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// 註冊 DbContext 並配置 SQL Server
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
// ...
```

**代碼解釋：**

```csharp
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

- `AddDbContext<UserDbContext>()` - 在依賴注入容器中註冊 DbContext
- `options.UseSqlServer()` - 指定使用 SQL Server 作為資料庫提供者
- `builder.Configuration.GetConnectionString("DefaultConnection")` - 從 appsettings.json 讀取連接字符串

> **💡 提示：** 完成此步驟後，就可以在 Visual Studio 的套件管理器控制台中使用 EF Core 命令（如 `Add-Migration` 和 `Update-Database`）來管理資料庫遷移。

### 創建資料庫遷移

**遷移（Migration）** 是 EF Core 用來管理資料庫結構變化的機制。

#### 方法 1：使用命令行工具

在終端/PowerShell 中執行：

```bash
dotnet ef migrations add Initial
```

#### 方法 2：使用 Visual Studio 套件管理器控制台（推薦）

**優勢：**
- 在 IDE 內部執行，無需切換到外部工具
- 自動識別項目上下文
- 視覺化反饋

**步驟：**

1. **打開套件管理器控制台**
   - 菜單：`Tools` → `NuGet Package Manager` → `Package Manager Console`
   - 或按快捷鍵：`Ctrl + ~` （在 Visual Studio 中）

2. **在控制台輸入遷移命令**

```powershell
Add-Migration Initial
```

**命令詳解：**
- `Add-Migration` - 創建新遷移
- `Initial` - 遷移的名稱（可自定義，通常第一個取名 `Initial`）

**完整的遷移命令參考：**

| 命令 | 說明 |
|------|------|
| `Add-Migration [Name]` | 創建新遷移（[Name] 替換為遷移名稱） |
| `Update-Database` | 將遷移應用到資料庫 |
| `Update-Database -Migration [MigrationName]` | 回滾到指定的遷移 |
| `Get-Migrations` | 列出所有遷移 |
| `Remove-Migration` | 刪除最後一個遷移（只能在未應用到資料庫前刪除） |
| `Update-Database 0` | 刪除所有遷移並清空資料庫 |

**套件管理器控制台中的常見操作流程：**

```powershell
# 1. 創建初始遷移
Add-Migration Initial

# 2. 查看所有遷移
Get-Migrations

# 3. 應用遷移到資料庫
Update-Database

# 4. 修改模型後，創建新遷移
Add-Migration AddNewColumn

# 5. 應用新遷移
Update-Database

# 6. 如果需要回滾到上一個遷移
Update-Database -Migration Initial
```

**控制台輸出示例：**

```
PM> Add-Migration Initial
To undo this action, use Remove-Migration.

PM> Update-Database
Done. 0.123s

PM>
```

這會在 `Migrations/` 目錄下生成以下文件：

```
Migrations/
├── [Timestamp]_Initial.cs          # 前向遷移（創建表）
├── [Timestamp]_Initial.Designer.cs # 遷移的元數據
└── UserDbContextModelSnapshot.cs   # 當前模型的快照
```

**遷移文件詳解：**

生成的遷移文件類似這樣：

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Users",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
            PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_Users", x => x.Id);
        });
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropTable(name: "Users");
}
```

- **Up()** - 創建表的 SQL（應用遷移時執行）
- **Down()** - 刪除表的 SQL（回滾遷移時執行）

### 應用遷移到資料庫

#### 方法 1：使用命令行工具

```bash
dotnet ef database update
```

#### 方法 2：使用 Visual Studio 套件管理器控制台

在套件管理器控制台中輸入：

```powershell
Update-Database
```

**執行結果：**

無論使用哪種方法，都會完成以下步驟：
1. 連接到 `appsettings.json` 中指定的資料庫
2. 創建資料庫（如不存在）
3. 執行遷移中的 SQL 命令
4. 創建 Users 表
5. 更新 `__EFMigrationsHistory` 表（追蹤已應用的遷移）

### 更新 AuthController 以使用資料庫

現在需要更新 AuthController，使用 DbContext 替代靜態變數：

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController(IConfiguration configuration, UserDbContext dbContext) : ControllerBase
{
    // 移除: public static User user = new User();

    [HttpPost("register")]
    public async Task<ActionResult<User>> Register(UserDto request)
    {
        // 檢查用戶名是否已存在
        var existingUser = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (existingUser != null)
        {
            return BadRequest("Username already exists.");
        }

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = request.Username,
            PasswordHash = hashedPassword
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return Ok(user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<string>> Login(UserDto request)
    {
        // 從資料庫查詢用戶
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username);

        if (user == null)
        {
            return BadRequest("User not found.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return BadRequest("Wrong password.");
        }

        var token = CreateToken(user);
        return Ok(token);
    }

    private string CreateToken(User user)
    {
        // ... CreateToken 實現保持不變
    }
}
```

**關鍵變化：**

1. **構造函數注入** - 添加 `UserDbContext dbContext` 參數
2. **非同步操作** - 使用 `async/await` 處理資料庫操作
3. **資料庫查詢** - 使用 LINQ 查詢（例如 `FirstOrDefaultAsync()`）
4. **添加實體** - `dbContext.Users.Add(user)`
5. **保存更改** - `await dbContext.SaveChangesAsync()`

### 測試資料庫連接

使用 Scalar 或 cURL 測試 API：

**第一步：註冊用戶**
```bash
curl -X POST https://localhost:7XXX/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"alice","password":"SecurePassword123!"}'
```

**第二步：重啟應用程式後再次註冊相同用戶**
- ✅ 現在應該返回 "Username already exists." 錯誤
- （之前會成功，因為數據沒有持久化）

**第三步：用之前註冊的用戶登入**
```bash
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"alice","password":"SecurePassword123!"}'
```

- ✅ 應該返回有效的 JWT Token
- ✅ 應用程式重啟後仍然可以登入

### 遷移工作流程

當修改實體模型時的完整工作流程：

```
1. 修改 User 實體
         ↓
2. dotnet ef migrations add [MigrationName]
   （生成遷移文件）
         ↓
3. dotnet ef database update
   （應用遷移到資料庫）
         ↓
4. 測試 API
```

**常見遷移命令對照表：**

| 操作 | 命令行工具 | 套件管理器控制台 |
|------|----------|----------------|
| 列出所有遷移 | `dotnet ef migrations list` | `Get-Migrations` |
| 創建遷移 | `dotnet ef migrations add [Name]` | `Add-Migration [Name]` |
| 應用遷移 | `dotnet ef database update` | `Update-Database` |
| 回滾到指定遷移 | `dotnet ef database update [Name]` | `Update-Database -Migration [Name]` |
| 刪除最新遷移 | `dotnet ef migrations remove` | `Remove-Migration` |
| 清空資料庫 | `dotnet ef database update 0` | `Update-Database -Migration 0` |

**命令行工具示例：**
```bash
# 列出所有遷移
dotnet ef migrations list

# 回滾最後一個遷移
dotnet ef database update [PreviousMigrationName]

# 刪除最新遷移（未應用到資料庫）
dotnet ef migrations remove

# 刪除所有遷移並清空資料庫
dotnet ef database update 0
```

**套件管理器控制台示例：**
```powershell
# 列出所有遷移
Get-Migrations

# 回滾到指定遷移
Update-Database -Migration Initial

# 刪除最新遷移
Remove-Migration

# 清空資料庫
Update-Database -Migration 0
```

### DbContext 與資料庫的映射

EF Core 自動將實體類映射到資料庫表：

| C# 代碼 | 資料庫 |
|--------|------|
| `DbSet<User> Users` | Users 表 |
| `public Guid Id` | Id 列 (uniqueidentifier, PRIMARY KEY) |
| `public string Username` | Username 列 (nvarchar(max)) |
| `public string PasswordHash` | PasswordHash 列 (nvarchar(max)) |

---

## 第七步：Service 層重構

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

本分支涵蓋了 Service 層的設計和實現，完成了基本的三層架構（Controller、Service、DbContext）。後續分支將涵蓋：
- 🛡️ API 認證中間件的實現（驗證 Token）
- 🔐 刷新 Token 機制（防止 Token 過期頻繁要求重新登入）
- 👤 擴展 Claims 和授權（添加用戶角色、權限等）
- 📝 輸入驗證和錯誤處理的增強
- 🗄️ 更複雜的資料庫操作（關聯表、索引等）
- 🧪 單元測試和 Mock（測試 Service 層的業務邏輯）
- 🔄 多個 Service 實現（OAuth、LDAP 等)

---

## 參考資源

### 認證與密碼安全
- [BCrypt.Net-Next GitHub](https://github.com/BcryptNet/bcrypt.net-next)
- [OWASP 密碼安全指南](https://owasp.org/www-project-cheat-sheets/cheatsheets/Authentication_Cheat_Sheet)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)

### API 文檔和工具
- [Scalar API 文檔](https://scalar.com/)
- [OpenAPI 規範](https://www.openapis.org/)

### Entity Framework Core
- [Entity Framework Core 官方文檔](https://docs.microsoft.com/en-us/ef/core/)
- [EF Core 遷移指南](https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 查詢基礎](https://docs.microsoft.com/en-us/ef/core/querying/)
- [SQL Server LocalDB](https://docs.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)

### .NET 最佳實踐
- [依賴注入在 ASP.NET Core 中的使用](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- [非同步程式設計最佳實踐](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)