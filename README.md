# JWT 認證系統教學 - 第一部分：用戶註冊與登入

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 安裝並配置必要的 NuGet 套件
2. 設計用戶數據模型
3. 實現安全的密碼管理（密碼雜湊）
4. 構建用戶註冊和登入 API 端點
5. 生成 JWT Token 進行身份驗證

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

---

## 安全性提示

⚠️ **重要注意事項：**

1. **永遠不要存儲明文密碼** - 即使是系統管理員也不應該看到用戶密碼
2. **永遠不要從雜湊值還原密碼** - 這在密碼學上是不可能的（理論上）
3. **使用 HTTPS** - 在生產環境中必須使用 HTTPS 加密傳輸
4. **實現速率限制** - 防止暴力攻擊登入接口
5. **添加日誌和監控** - 記錄失敗的登入嘗試

---

## 進階話題預告

本分支涵蓋了基本的用戶認證和 JWT Token 生成。後續分支將涵蓋：
- 🛡️ API 認證中間件的實現（驗證 Token）
- 💾 持久化數據存儲（數據庫集成，目前使用靜態變數）
- 🔐 刷新 Token 機制（防止 Token 過期頻繁要求重新登入）
- 👤 擴展 Claims 和授權（添加用戶角色、權限等）
- 📝 輸入驗證和錯誤處理的增強

---

## 參考資源

- [BCrypt.Net-Next GitHub](https://github.com/BcryptNet/bcrypt.net-next)
- [OWASP 密碼安全指南](https://owasp.org/www-project-cheat-sheets/cheatsheets/Authentication_Cheat_Sheet)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [Scalar API 文檔](https://scalar.com/)