# JWT 認證系統教學 - 第五部分：端點安全保護

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解 ASP.NET Core 的認證系統架構
2. 實現和配置 JWT Bearer 認證方案
3. 設置 Token 驗證參數
4. 使用 [Authorize] 特性保護端點
5. 完整的認證和授權流程

---

> **前置知識：** 請先完成前四個分支 (`1_Register_And_Login`、`2_Generate_JWT_Token`、`3_Connect_Database`、`4_Refactor_Services`)，了解基本的認證和 Service 層設計。

---

## 第一步：端點安全保護（Authentication Scheme 和 [Authorize]）

本步驟講解如何使用 JWT Bearer 認證方案保護 API 端點，實現完整的認證和授權流程。

### ASP.NET Core 認證系統架構

ASP.NET Core 的認證系統分為兩個核心概念：

**1. Authentication（認證）- 驗證用戶身份**
   - 問題：你是誰？
   - 過程：驗證用戶的 Credentials（用戶名、密碼、Token 等）
   - 結果：建立用戶的身份（Identity）

**2. Authorization（授權）- 檢查用戶權限**
   - 問題：你可以做什麼？
   - 過程：根據用戶身份檢查是否有訪問資源的權限
   - 結果：允許或拒絕訪問

### Authentication Scheme（認證方案）

#### 什麼是 Authentication Scheme？

Authentication Scheme 是 ASP.NET Core 認證系統中的核心概念。它定義了：

1. **如何識別用戶** - 從請求中提取用戶信息
2. **如何驗證身份** - 確認用戶信息的有效性
3. **如何建立身份** - 創建 ClaimsPrincipal 對象

#### JWT Bearer Scheme 的工作原理

在 `Program.cs` 中配置：

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["AppSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["AppSettings:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["AppSettings:Token"]!))
        };
    });
```

**代碼解釋：**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
```

- `AddAuthentication()` - 註冊認證服務
- `JwtBearerDefaults.AuthenticationScheme` - 設置默認認證方案為 JWT Bearer
- 這意味著所有 `[Authorize]` 特性默認使用 JWT Bearer 認證

```csharp
.AddJwtBearer(options => { ... })
```

- `.AddJwtBearer()` - 添加 JWT Bearer 認證方案的實現
- `options` - 配置 Token 驗證參數

#### Token 驗證參數詳解

| 參數 | 說明 | 驗證內容 |
|------|------|--------|
| **ValidateIssuer** | 是否驗證 Issuer Claim | 檢查 Token 是由授權的應用程式頒發 |
| **ValidIssuer** | 允許的 Issuer 值 | 必須與 Token 中的 "iss" Claim 匹配 |
| **ValidateAudience** | 是否驗證 Audience Claim | 檢查 Token 是為正確的接收方 |
| **ValidAudience** | 允許的 Audience 值 | 必須與 Token 中的 "aud" Claim 匹配 |
| **ValidateLifetime** | 是否檢查過期時間 | 驗證 Token 的 "exp" Claim 未超期 |
| **ValidateIssuerSigningKey** | 是否驗證簽名 | 使用 IssuerSigningKey 驗證 Token 簽名 |
| **IssuerSigningKey** | 用於驗證簽名的密鑰 | SymmetricSecurityKey，必須與頒發密鑰相同 |

#### Authentication Scheme 的完整工作流程

```
HTTP 請求到達
    ↓
檢查 Authorization Header
    ├─ 格式：Authorization: Bearer <token>
    ├─ 提取 Token（移除 "Bearer " 前綴）
    └─ 如果無 Header，跳過認證

驗證簽名
    ├─ 使用 IssuerSigningKey 驗證
    ├─ 失敗：拒絕 Token，返回 401
    └─ 成功：繼續

驗證 Issuer
    ├─ 檢查 Token 中的 "iss" Claim
    ├─ 失敗：拒絕 Token，返回 401
    └─ 成功：繼續

驗證 Audience
    ├─ 檢查 Token 中的 "aud" Claim
    ├─ 失敗：拒絕 Token，返回 401
    └─ 成功：繼續

驗證過期時間
    ├─ 檢查 Token 中的 "exp" Claim
    ├─ 過期：拒絕 Token，返回 401
    └─ 未過期：繼續

解析 Claims
    ├─ 從 Token Payload 提取所有 Claims
    ├─ 創建 ClaimsPrincipal 對象
    └─ 將其設置到 HttpContext.User

標記認證成功
    ├─ HttpContext.User.Identity.IsAuthenticated = true
    └─ HttpContext.User 包含 Claims 信息
```

### [Authorize] 特性（授權）

#### [Authorize] 的作用

`[Authorize]` 是一個 ASP.NET Core 特性，用於標記需要認證的端點。

```csharp
[Authorize]
[HttpGet]
public IActionResult AuthenticatedOnlyEndpoint()
{
    return Ok("You are authenticated.");
}
```

#### [Authorize] 的工作原理

當請求到達標有 `[Authorize]` 的端點時：

**第 1 步：檢查認證狀態**
```csharp
if (!HttpContext.User.Identity?.IsAuthenticated ?? false)
{
    // 用戶未認證
    return Unauthorized();  // 返回 401
}
```

**第 2 步：執行端點邏輯**
```csharp
if (已認證)
{
    // 執行端點方法
    return Ok("You are authenticated.");
}
```

#### [Authorize] 和認證中間件的關係

```
HTTP 請求
    ↓
Authentication Middleware（認證中間件）
    ├─ 設置 HttpContext.User
    ├─ 驗證 Token（如果存在）
    └─ 返回 ClaimsPrincipal

Authorization Middleware（授權中間件）
    ├─ 檢查 [Authorize] 特性
    ├─ 檢查 HttpContext.User.Identity.IsAuthenticated
    ├─ 如果未認證 → 返回 401
    └─ 如果已認證 → 繼續執行

端點方法（Controller Action）
    ├─ 執行業務邏輯
    └─ 返回響應
```

#### 可空性和認證狀態

在 ASP.NET Core 中：

```csharp
// 未認證時
HttpContext.User == AnonymousPrincipal
HttpContext.User.Identity == ClaimsIdentity（空）
HttpContext.User.Identity.IsAuthenticated == false

// 已認證時
HttpContext.User == ClaimsPrincipal
HttpContext.User.Identity == ClaimsIdentity（包含 Claims）
HttpContext.User.Identity.IsAuthenticated == true
HttpContext.User.FindFirst(ClaimTypes.Name).Value == "alice"
```

### 完整的認證和授權流程

#### 場景 1：未提供 Token

```
1. 客戶端請求：
   GET /api/auth
   (無 Authorization Header)

2. 認證中間件：
   • 未找到 Authorization Header
   • HttpContext.User = AnonymousPrincipal
   • HttpContext.User.Identity.IsAuthenticated = false

3. 授權中間件：
   • 檢查 [Authorize] 特性
   • 發現 IsAuthenticated = false
   • 返回 401 Unauthorized

4. 客戶端收到：
   401 Unauthorized
```

#### 場景 2：提供無效 Token

```
1. 客戶端請求：
   GET /api/auth
   Authorization: Bearer invalid_token_xyz

2. 認證中間件：
   • 提取 Token：invalid_token_xyz
   • 嘗試驗證簽名 → 失敗
   • 返回 401 Unauthorized

3. 授權中間件：
   • 未執行（已在認證階段失敗）

4. 客戶端收到：
   401 Unauthorized
```

#### 場景 3：提供有效 Token

```
1. 客戶端請求：
   GET /api/auth
   Authorization: Bearer eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9...

2. 認證中間件：
   • 提取 Token
   • 驗證簽名 ✓
   • 驗證 Issuer ✓
   • 驗證 Audience ✓
   • 驗證過期時間 ✓
   • 解析 Claims：
     ├─ iss: "MyApp"
     ├─ aud: "MyAppUsers"
     ├─ name: "alice"
     └─ nameid: "550e8400-e29b-41d4-a716-446655440000"
   • HttpContext.User = ClaimsPrincipal（包含 Claims）
   • HttpContext.User.Identity.IsAuthenticated = true

3. 授權中間件：
   • 檢查 [Authorize] 特性
   • 發現 IsAuthenticated = true
   • 繼續執行端點

4. 端點方法執行：
   return Ok("You are authenticated.");

5. 客戶端收到：
   200 OK
   "You are authenticated."
```

### 存取認證用戶信息

在已認證的端點中，可以存取用戶信息：

```csharp
[Authorize]
[HttpGet("profile")]
public IActionResult GetProfile()
{
    // 獲取用戶名
    var username = HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;

    // 獲取用戶 ID
    var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // 獲取所有 Claims
    var allClaims = HttpContext.User.Claims;

    return Ok(new
    {
        username = username,
        userId = userId,
        claims = allClaims
    });
}
```

**說明：**

- `HttpContext.User` - 當前認證用戶（包含 Claims）
- `FindFirst(claimType)` - 查找特定類型的 Claim
- `Claims` - 獲取所有 Claims

### Token 驗證失敗的情況

| 失敗原因 | HTTP 狀態碼 | 說明 |
|---------|-----------|------|
| 簽名無效 | 401 | Token 被篡改 |
| Issuer 不匹配 | 401 | Token 不是由授權的應用程式頒發 |
| Audience 不匹配 | 401 | Token 不是給予此應用程式 |
| Token 過期 | 401 | Token 的 exp Claim 已超期 |
| 無 Token | 401 | Authorization Header 缺失且端點需要認證 |
| Token 格式錯誤 | 401 | Authorization Header 不是 "Bearer <token>" 格式 |

### 配置多個認證方案

ASP.NET Core 支持多個認證方案。可以配置多種認證方式：

```csharp
// 配置 JWT Bearer
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { ... })
    .AddCookie("Cookies", options => { ... })  // 也支持 Cookie 認證
    .AddOpenIdConnect("OpenIdConnect", options => { ... });  // 或 OpenID Connect

// 默認使用 JWT Bearer，但其他端點可以指定不同方案
[Authorize(AuthenticationSchemes = "Cookies")]
public IActionResult CookieBasedEndpoint() { ... }
```

### 最佳實踐

**1. 始終驗證所有參數**
```csharp
ValidateIssuer = true,
ValidateAudience = true,
ValidateLifetime = true,
ValidateIssuerSigningKey = true
```

**2. 使用 HTTPS**
- 生產環境必須使用 HTTPS
- JWT Token 應通過安全通道傳輸

**3. 合理設置過期時間**
```csharp
expires: DateTime.UtcNow.AddDays(1)  // 1 天過期
```

**4. 在端點級別控制授權**
```csharp
[Authorize]  // 保護此端點
[HttpGet]
public IActionResult Protected() { ... }

[HttpPost]  // 此端點無需認證
public IActionResult UnProtected() { ... }
```

**5. 提供清晰的錯誤信息**
```csharp
// 好的做法
return Unauthorized("Invalid token");

// 避免洩露敏感信息
// return Unauthorized("Token signature invalid");  // ❌ 過於詳細
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
- 👤 基於角色的授權（Role-based Authorization）
- 🔄 刷新 Token 機制（防止 Token 過期頻繁要求重新登入）
- 🔑 基於策略的授權（Policy-based Authorization）
- 📊 動態角色管理
- 🛡️ 審計日誌

---

## 參考資源

### 認證與授權
- [ASP.NET Core 授權](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/)
- [JWT Bearer 認證](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn)
- [ASP.NET Core 認證與授權](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)

### JWT 和安全
- [JWT 規範 RFC 7519](https://tools.ietf.org/html/rfc7519)
- [OWASP 認證速查表](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)
