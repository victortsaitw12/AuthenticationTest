# JWT 認證系統教學 - 第一部分：用戶註冊與登入

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 安裝並配置必要的 NuGet 套件
2. 設計用戶數據模型
3. 實現安全的密碼管理（密碼雜湊）
4. 構建用戶註冊和登入 API 端點

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

    // 登入成功
    return Ok("Login successful.");
}
```

### 使用示例

**正確的密碼：**

```bash
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"john_doe","password":"SecurePassword123!"}'
```

**響應：** `200 OK - "Login successful."`

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

**預期結果：** ✅ `200 OK - "Login successful."`

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

本分支是 JWT 認證系統的第一部分。後續分支將涵蓋：
- 🔑 JWT token 的生成和驗證
- 🛡️ API 認證中間件的實現
- 💾 持久化數據存儲（數據庫集成）
- 🔐 刷新 token 機制
- 👤 用戶聲明（Claims）和授權

---

## 參考資源

- [BCrypt.Net-Next GitHub](https://github.com/BcryptNet/bcrypt.net-next)
- [OWASP 密碼安全指南](https://owasp.org/www-project-cheat-sheets/cheatsheets/Authentication_Cheat_Sheet)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [Scalar API 文檔](https://scalar.com/)