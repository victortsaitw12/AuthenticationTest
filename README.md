# JWT 認證系統教學 - 第二部分：生成 JWT Token

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 JWT（JSON Web Token）的結構和原理
2. 配置 JWT Token 的密鑰和參數
3. 在登入時生成 JWT Token
4. 理解 Claims 在 Token 中的角色
5. 使用 JWT Token 進行身份驗證

---

> **前置知識：** 請先完成前一個分支 (`1_Register_And_Login`)，了解用戶註冊和登入的基本實現。

---

## 第一步：生成 JWT Token

本步驟講解如何實現 JWT Token 生成，並將其集成到登入流程中。

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

### 安裝 JWT Token 相關套件

```bash
dotnet add package System.IdentityModel.Tokens.Jwt
```

**System.IdentityModel.Tokens.Jwt** 是 Microsoft 提供的 JWT 處理庫，用於生成和驗證 JWT Token。

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
- 客戶端無法延長 Token 有效期（Token 中包含過期時間，無法修改而不破壞簽名）
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

### 更新登入端點以返回 Token

修改 `AuthController.cs` 的 `Login` 方法，在登入成功時返回 Token：

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

    // ✅ 新增：登入成功，生成 JWT Token
    var token = CreateToken(user);
    return Ok(token);
}
```

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

### 使用 cURL 測試

```bash
# 登入並獲取 Token
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"alice","password":"MyPassword123!"}'
```

## Token 解析和驗證

### 在線 JWT 解析工具

可以使用 [jwt.io](https://jwt.io) 來解析和查看 Token 的內容：

1. 複製生成的 Token
2. 粘貼到 jwt.io 的 Encoded 部分
3. 查看 Header、Payload 和 Signature 的詳細內容

### Token 驗證要點

生成的 Token 應該包含以下驗證信息：

**Header**
```json
{
  "alg": "HS512",
  "typ": "JWT"
}
```

**Payload**
```json
{
  "http://schemas.xmlsoap.org/2003/05/identity/claims/name": "alice",
  "iss": "MyApp",
  "aud": "MyAppUsers",
  "exp": 1709941200,
  "iat": 1709854800
}
```

---

## 最佳實踐

**1. 使用足夠長的密鑰**
```csharp
// ✅ 好：密鑰長度足夠（至少 64 字節）
"Token": "YourSecretKeyMustBeAtLeast64BytesLongForHmacSha512AlgorithmSuperSecure!"

// ❌ 不好：密鑰太短
"Token": "short_key"
```

**2. 設置合理的過期時間**
```csharp
// ✅ 好：設置短期過期時間
expires: DateTime.UtcNow.AddDays(1)

// ❌ 不好：過期時間太長
expires: DateTime.UtcNow.AddDays(365)
```

**3. 使用強簽名算法**
```csharp
// ✅ 好：使用 HmacSha512（強加密）
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

// ❌ 不好：使用弱算法
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
```

**4. 不要在 Token 中存儲敏感信息**
```csharp
// ✅ 好：只存儲公開的身份信息
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.Username),
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
};

// ❌ 不好：存儲密碼或其他敏感信息
var claims = new List<Claim>
{
    new Claim("password", user.PasswordHash)  // 不要這樣做
};
```

**5. 將密鑰存儲在配置中，不要硬編碼**
```csharp
// ✅ 好：從配置讀取
var token = configuration.GetValue<string>("AppSettings:Token");

// ❌ 不好：硬編碼
var token = "YourSecretKey";
```

---

## 常見問題

### Q: Token 如何被驗證？

Token 驗證包括三個步驟：
1. **簽名驗證** - 使用公開的密鑰驗證簽名是否有效
2. **Issuer 驗證** - 檢查 Token 是由授權的應用程式頒發
3. **Audience 驗證** - 檢查 Token 是給予此應用程式的

### Q: Token 可以被修改嗎？

不可以。如果修改 Token 的任何部分（Header、Payload 或 Signature），簽名驗證就會失敗。因為簽名是基於原始的 Header 和 Payload 計算的。

### Q: 為什麼需要密鑰？

密鑰用於簽名 Token，只有知道密鑰的服務器才能生成有效的簽名。這確保了 Token 的真實性和完整性。

### Q: Token 過期後怎麼辦？

當 Token 過期時，客戶端需要重新登入獲取新的 Token，或者使用刷新 Token（如果實現了刷新機制）來獲取新的訪問 Token。

---

## 安全性提示

⚠️ **重要注意事項：**

1. **永遠不要存儲明文密碼** - 即使是系統管理員也不應該看到用戶密碼
2. **使用 HTTPS** - 在生產環境中必須使用 HTTPS 加密傳輸
3. **實現速率限制** - 防止暴力攻擊登入接口
4. **添加日誌和監控** - 記錄失敗的登入嘗試
5. **保護密鑰** - 生產環境使用環境變數或 Secrets Manager，不要提交到 Git

---

## 進階話題預告

本分支涵蓋了 JWT Token 的生成。後續分支將涵蓋：
- 💾 數據庫集成（使用 EF Core 替代靜態變數）
- 🏗️ Service 層重構（業務邏輯分離）
- 🛡️ API 認證中間件（驗證 Token）
- 👤 角色和授權（Role-based Authorization）
- 🔐 刷新 Token 機制（防止 Token 過期）

---

## 參考資源

### JWT 和安全
- [JWT 規範 RFC 7519](https://tools.ietf.org/html/rfc7519)
- [JWT.io 工具](https://jwt.io)
- [OWASP 認證速查表](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP JWT 安全指南](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html)

### ASP.NET Core
- [System.IdentityModel.Tokens.Jwt NuGet](https://www.nuget.org/packages/System.IdentityModel.Tokens.Jwt/)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [ASP.NET Core Configuration](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)

### 加密和簽名
- [HMAC-SHA512 算法](https://en.wikipedia.org/wiki/HMAC)
- [對稱加密密鑰管理](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
