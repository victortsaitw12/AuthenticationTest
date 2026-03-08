# JWT 認證系統教學 - 第七部分：刷新令牌（Refresh Token）

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 理解刷新令牌（Refresh Token）的目的和安全意義
2. 區分訪問令牌（Access Token）和刷新令牌（Refresh Token）
3. 實現刷新令牌的生成、儲存和驗證
4. 在 JWT 認證流程中集成刷新令牌機制
5. 實現令牌輪換（Token Rotation）安全模式

---

> **前置知識：** 請先完成前六個分支，特別是 `6_Add_Roles` 分支，了解基本的認證、授權和角色概念。

---

## 第十步：刷新令牌（Refresh Token）

本步驟講解如何實現刷新令牌機制，允許用戶在不重新登入的情況下獲取新的訪問令牌。

### 什麼是刷新令牌（Refresh Token）？

刷新令牌是一個長期有效的令牌，用於獲取新的短期訪問令牌。它是現代 JWT 認證系統的重要安全機制。

#### 基本特徵

| 特徵 | 訪問令牌 | 刷新令牌 |
|------|---------|---------|
| **用途** | 驗證請求身份 | 獲取新的訪問令牌 |
| **有效期** | 短期（通常 1-15 分鐘） | 長期（通常 7-30 天） |
| **存儲位置** | 內存或 localStorage | HTTP Only Cookie 或安全儲存 |
| **包含的信息** | 用戶身份、角色、權限等 | 通常只是令牌 ID |
| **發送方式** | 每個 API 請求中 | 只在需要刷新時發送 |
| **被盜風險** | 低風險（快速過期） | 中等風險（需要額外驗證） |

### 為什麼需要刷新令牌？

#### 場景 1：訪問令牌被竊取

```
場景：用戶在公共 WiFi 上使用應用，攻擊者竊取了訪問令牌

時間線：
├─ 11:00 → 令牌被竊取
├─ 11:05 → 攻擊者開始濫用令牌
├─ 11:15 → 訪問令牌過期（15 分鐘有效期）
└─ 11:16 → 攻擊者無法繼續使用舊令牌

✓ 損失最小化：只有 11 分鐘的風險窗口
```

#### 場景 2：用戶長時間活動

```
場景：用戶在應用中進行長時間操作，不希望中途登出

不使用刷新令牌：
├─ 11:00 → 登入，獲得 15 分鐘有效的訪問令牌
├─ 11:10 → 用戶正在操作
├─ 11:15 → 訪問令牌過期，用戶被強制登出
└─ 11:16 → 用戶必須重新登入

使用刷新令牌：
├─ 11:00 → 登入，獲得訪問令牌和刷新令牌
├─ 11:10 → 用戶正在操作
├─ 11:14 → 系統檢測到訪問令牌即將過期
├─ 11:14:30 → 使用刷新令牌自動獲取新的訪問令牌
└─ 11:15+ → 用戶無中斷繼續操作

✓ 改進用戶體驗：無縫會話延長
```

#### 安全優勢總結

| 優勢 | 說明 |
|------|------|
| **縮小攻擊窗口** | 訪問令牌快速過期，降低被盜後的危害 |
| **令牌輪換** | 每次刷新時發行新令牌，舊令牌自動失效 |
| **無縫用戶體驗** | 用戶無需頻繁重新登入 |
| **服務器控制** | 服務器可隨時撤銷刷新令牌 |
| **審計追蹤** | 可記錄所有刷新操作，檢測異常活動 |

### 訪問令牌 vs 刷新令牌

#### 生命週期對比

```
登入時刻（11:00）
├─ 發行訪問令牌（有效期到 11:15）
├─ 發行刷新令牌（有效期到 4 月 13 日）
└─ 存儲刷新令牌到數據庫

時間線：
11:00 ├─ AccessToken ──────── 15 分鐘 ────────── 11:15 ✗ 過期
      │
      └─ RefreshToken ───── 7 天 ────────────── 下週日 ✓ 有效

使用場景：
11:10 (有效期內)
└─ 使用訪問令牌訪問 API ✓

11:20 (訪問令牌已過期)
├─ 訪問令牌無效 ✗
└─ 使用刷新令牌換取新訪問令牌
   └─ 新訪問令牌（有效期到 11:35）✓

下週一 (刷新令牌已過期)
└─ 刷新令牌無效 ✗
   └─ 用戶必須重新登入
```

#### 職責划分

**訪問令牌（Access Token）：**
- 代表用戶當前會話的身份證明
- 包含用戶的身份信息（Username、ID、Role 等）
- 用於 API 請求的驗證
- **短暫有效**（1-15 分鐘），防止被盜後長期濫用

**刷新令牌（Refresh Token）：**
- 代表用戶與應用的信任關係
- 用於獲取新的訪問令牌
- **長期有效**（7-30 天），可延長會話
- 存儲在數據庫中，服務器可隨時撤銷

#### 數據庫角度

```
User Table:
┌──────────┬──────────┬──────────────┬─────────────────────────┐
│ Id       │ Username │ Role         │ RefreshToken            │
├──────────┼──────────┼──────────────┼─────────────────────────┤
│ abc-123  │ alice    │ Admin        │ xY7kZ9mQ2p... (256-bit) │
│ def-456  │ bob      │ User         │ aB3cD5eF7g... (256-bit) │
└──────────┴──────────┴──────────────┴─────────────────────────┘

訪問令牌只存在于 API 請求的 Authorization Header，不存儲在數據庫。
刷新令牌存儲在數據庫中，用於驗證和撤銷。
```

### 刷新令牌驗證流程

#### 完整的刷新流程

```
客户端                          服務器（API）                    數據庫

11:00 └─ 登入請求 ──────────────────────→ ✓ 驗證用戶
                                        ├─ 生成訪問令牌（15分鐘）
                                        ├─ 生成刷新令牌（256位隨機數）
                                        └─ 存儲刷新令牌 ──────────→ RefreshToken = "xY7kZ9..."
        ← ─ 返回兩個令牌 ─────────────────

應用內存:
├─ AccessToken: "eyJhbGc..." (時效: 11:15)
└─ RefreshToken: "xY7kZ9..." (時效: 4月13日)

11:14 └─ API 請求
        Header: Authorization: Bearer eyJhbGc...
        ✓ 令牌有效，執行操作

11:20 └─ API 請求
        Header: Authorization: Bearer eyJhbGc...
        ✗ 令牌已過期，返回 401 Unauthorized

應用捕捉 401 並自動刷新:
11:20 └─ 刷新令牌請求 ───────────────────→ 驗證過程：
   POST /api/auth/refresh-token           1. 查找用戶
   {                                       ├─ 用戶 ID = abc-123
     "userId": "abc-123",                  └─ ✓ 用戶存在
     "refreshToken": "xY7kZ9..."
   }                                       2. 檢查刷新令牌
                                           ├─ 數據庫查詢:
                                           │  RefreshToken = "xY7kZ9..."
                                           └─ ✓ 令牌匹配

                                           3. 檢查有效期
                                           ├─ 當前時間: 2026-03-10
                                           ├─ 過期時間: 2026-04-13
                                           └─ ✓ 未過期

                                           4. ✓ 驗證成功
                                           ├─ 生成新訪問令牌（15分鐘）
                                           ├─ 生成新刷新令牌（7天）
                                           └─ 更新數據庫 ──→ RefreshToken = "aB3cD5..."
        ← ─ 返回新的兩個令牌 ───────────────

應用內存更新:
├─ AccessToken: "eyJuZW..." (新的，時效: 11:35)
└─ RefreshToken: "aB3cD5..." (新的，時效: 4月20日)

11:21+ └─ API 請求
        Header: Authorization: Bearer eyJuZW...
        ✓ 新令牌有效，繼續執行操作

失敗場景 1：刷新令牌無效
11:25 └─ 刷新令牌請求 ───────────────────→ 驗證：
   POST /api/auth/refresh-token           ├─ 用戶存在 ✓
   {                                       ├─ 令牌匹配 ✗
     "userId": "abc-123",                  │  (數據庫: "aB3cD5..." vs 請求: "xY7kZ9...")
     "refreshToken": "xY7kZ9..." (舊的)    └─ ✗ 驗證失敗
   }
        ← ─ 返回 401 Unauthorized ────────

失敗場景 2：刷新令牌已過期
4月14日 └─ 刷新令牌請求 ─────────────────→ 驗證：
   POST /api/auth/refresh-token           ├─ 用戶存在 ✓
   {                                       ├─ 令牌匹配 ✓
     "userId": "abc-123",                  ├─ 有效期檢查 ✗
     "refreshToken": "aB3cD5..."           │  (當前: 4月14日 > 過期: 4月13日)
   }                                       └─ ✗ 驗證失敗
        ← ─ 返回 401 Unauthorized ────────

應用捕捉錯誤 → 重定向用戶到登入頁面
```

#### 驗證邏輯詳解

**步驟 1：驗證用戶存在**

```csharp
var user = await context.Users.FindAsync(userId);
if (user is null)
{
    return null;  // 用戶不存在，拒絕刷新
}
```

**步驟 2：驗證刷新令牌匹配**

```csharp
if (user.RefreshToken != refreshToken)
{
    return null;  // 令牌不匹配，可能是過期的舊令牌或偽造的令牌
}
```

**步驟 3：驗證有效期**

```csharp
if (user.RefreshTokenExpiryTime <= DateTime.UtcNow)
{
    return null;  // 令牌已過期
}
```

**步驟 4：驗證成功 - 發行新令牌**

```csharp
// 如果通過所有檢查，則：
// 1. 生成新的訪問令牌
var newAccessToken = CreateToken(user);

// 2. 生成新的刷新令牌
var newRefreshToken = GenerateRefreshToken();  // 新的 256 位隨機數

// 3. 更新數據庫
user.RefreshToken = newRefreshToken;
user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
await context.SaveChangesAsync();

// 4. 返回新的令牌對
return new TokenResponseDto
{
    AccessToken = newAccessToken,
    RefreshToken = newRefreshToken
};
```

### 數據模型變更

#### User 實體擴展

```csharp
public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // 新增：刷新令牌相關
    public string? RefreshToken { get; set; }              // 刷新令牌（Base64）
    public DateTime? RefreshTokenExpiryTime { get; set; }  // 刷新令牌過期時間
}
```

#### 新增 DTO

**TokenResponseDto** - 登入和刷新時的響應

```csharp
public class TokenResponseDto
{
    public required string AccessToken { get; set; }   // JWT 訪問令牌
    public required string RefreshToken { get; set; }  // 刷新令牌
}
```

**RefreshTokenRequestDto** - 刷新令牌請求

```csharp
public class RefreshTokenRequestDto
{
    public Guid UserId { get; set; }              // 用戶 ID
    public required string RefreshToken { get; set; }  // 當前刷新令牌
}
```

### API 端點

#### 登入端點（已更新）

```
POST /api/auth/login
Content-Type: application/json

請求：
{
  "username": "alice",
  "password": "password123"
}

響應 (200 OK)：
{
  "accessToken": "eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "xY7kZ9mQ2p3rS5tU7vW9xYzAbCdEfGhIjKlMnOpQ="
}
```

#### 新增：刷新令牌端點

```
POST /api/auth/refresh-token
Content-Type: application/json

請求：
{
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "refreshToken": "xY7kZ9mQ2p3rS5tU7vW9xYzAbCdEfGhIjKlMnOpQ="
}

響應 (200 OK)：
{
  "accessToken": "eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9...",  // 新令牌
  "refreshToken": "aB3cD5eF7gH9iJkLmNoPqRsTuVwXyZaBcDeFgH="    // 新令牌
}

失敗 (401 Unauthorized)：
"Invalid refresh token"
```

### 實現細節

#### 刷新令牌生成

```csharp
private string GenerateRefreshToken()
{
    // 生成 32 個隨機位元組 (256 位)
    var randomNumber = new byte[32];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(randomNumber);

    // 轉換為 Base64 字符串便於存儲和傳輸
    return Convert.ToBase64String(randomNumber);
}
```

**為什麼是 32 字節（256 位）？**
- 256 位提供足夠的隨機性，難以被暴力破解（2^256 種可能）
- 符合行業標準（OAuth 2.0 推薦至少 128 位）
- 轉換為 Base64 約 44 字符，易於存儲和傳輸

#### 登入響應包含刷新令牌

```csharp
public async Task<TokenResponseDto?> LoginAsync(UserDto request)
{
    var user = await context.Users.FirstOrDefaultAsync(
        u => u.Username == request.Username);

    if (user is null ||
        !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
    {
        return null;
    }

    // 登入成功 → 返回訪問令牌和刷新令牌
    return new TokenResponseDto
    {
        AccessToken = CreateToken(user),
        RefreshToken = await GenerateAndSaveRefreshTokenAsync(user)
    };
}
```

#### 刷新令牌驗證和更新

```csharp
public async Task<TokenResponseDto?> RefreshTokenAsync(
    RefreshTokenRequestDto request)
{
    // 驗證刷新令牌
    var user = await ValidateRefreshTokenAsync(
        request.UserId,
        request.RefreshToken);

    if (user is null)
    {
        return null;  // 驗證失敗
    }

    // 驗證成功 → 發行新的令牌對（令牌輪換）
    return new TokenResponseDto
    {
        AccessToken = CreateToken(user),
        RefreshToken = await GenerateAndSaveRefreshTokenAsync(user)
    };
}

private async Task<User?> ValidateRefreshTokenAsync(
    Guid userId,
    string refreshToken)
{
    var user = await context.Users.FindAsync(userId);

    // 檢查 1：用戶存在
    // 檢查 2：令牌匹配
    // 檢查 3：未過期
    if (user is null
        || user.RefreshToken != refreshToken
        || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
    {
        return null;
    }

    return user;
}
```

### 客户端實現模式

#### 原始實現（手動處理）

```javascript
// 登入
const loginResponse = await fetch('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({username, password})
});

const {accessToken, refreshToken} = await loginResponse.json();
localStorage.setItem('accessToken', accessToken);
localStorage.setItem('refreshToken', refreshToken);
localStorage.setItem('userId', userId);

// API 請求
const apiResponse = await fetch('/api/users', {
    headers: {
        'Authorization': `Bearer ${localStorage.getItem('accessToken')}`
    }
});

if (apiResponse.status === 401) {
    // 訪問令牌過期 → 刷新
    const refreshResponse = await fetch('/api/auth/refresh-token', {
        method: 'POST',
        body: JSON.stringify({
            userId: localStorage.getItem('userId'),
            refreshToken: localStorage.getItem('refreshToken')
        })
    });

    const {accessToken: newAccessToken, refreshToken: newRefreshToken}
        = await refreshResponse.json();

    // 更新存儲的令牌
    localStorage.setItem('accessToken', newAccessToken);
    localStorage.setItem('refreshToken', newRefreshToken);

    // 重試原始請求
    return fetch('/api/users', {
        headers: {
            'Authorization': `Bearer ${newAccessToken}`
        }
    });
}
```

#### 進階實現（自動攔截）

```javascript
// 設置 Axios 攔截器自動處理刷新
axios.interceptors.response.use(
    response => response,
    async error => {
        const originalRequest = error.config;

        // 如果返回 401 且尚未重試
        if (error.response?.status === 401 && !originalRequest._retry) {
            originalRequest._retry = true;

            try {
                const {accessToken, refreshToken} =
                    await axios.post('/api/auth/refresh-token', {
                        userId: localStorage.getItem('userId'),
                        refreshToken: localStorage.getItem('refreshToken')
                    });

                localStorage.setItem('accessToken', accessToken);
                localStorage.setItem('refreshToken', refreshToken);

                // 使用新令牌重試
                originalRequest.headers['Authorization'] = `Bearer ${accessToken}`;
                return axios(originalRequest);
            } catch (refreshError) {
                // 刷新失敗 → 重定向到登入
                location.href = '/login';
            }
        }

        return Promise.reject(error);
    }
);
```

### 最佳實踐

**1. 使用正確的有效期**
```csharp
// ✓ 好：訪問令牌短期（1-15 分鐘）
var accessToken = new JwtSecurityToken(
    ...,
    expires: DateTime.UtcNow.AddMinutes(15)  // 15 分鐘
);

// ✓ 好：刷新令牌長期（7-30 天）
user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);  // 7 天
```

**2. 令牌輪換（Token Rotation）**
```csharp
// ✓ 好：每次刷新時都發行新的刷新令牌
public async Task<TokenResponseDto?> RefreshTokenAsync(...)
{
    var user = await ValidateRefreshTokenAsync(...);
    if (user is null) return null;

    // 發行新的刷新令牌，舊的自動失效
    var newRefreshToken = GenerateRefreshToken();
    user.RefreshToken = newRefreshToken;  // 覆蓋舊令牌
    await context.SaveChangesAsync();

    return new TokenResponseDto
    {
        AccessToken = CreateToken(user),
        RefreshToken = newRefreshToken
    };
}
```

**3. 安全存儲刷新令牌**
```javascript
// ❌ 不好：存儲在 localStorage，易受 XSS 攻擊
localStorage.setItem('refreshToken', token);

// ✓ 好：存儲在 HTTP Only Cookie，由瀏覽器自動發送
// 服務器設置 Cookie:
// Set-Cookie: refreshToken=...; HttpOnly; Secure; SameSite=Strict
```

**4. 檢測令牌重用攻擊**
```csharp
// ✓ 進階：如果舊刷新令牌再次被使用，表示可能被盜
// 應立即撤銷該用戶的所有令牌並要求重新登入
private async Task<User?> ValidateRefreshTokenAsync(Guid userId, string refreshToken)
{
    var user = await context.Users.FindAsync(userId);

    // 檢查令牌是否與數據庫中的匹配
    if (user?.RefreshToken != refreshToken)
    {
        // ⚠️ 令牌不匹配 → 可能的安全事件
        // 選擇 1：撤銷所有該用戶的令牌
        if (user != null)
        {
            user.RefreshToken = null;
            await context.SaveChangesAsync();
        }
        return null;
    }

    if (user.RefreshTokenExpiryTime <= DateTime.UtcNow)
    {
        return null;
    }

    return user;
}
```

**5. 記錄刷新操作**
```csharp
// ✓ 好：審計日誌可幫助檢測異常活動
private async Task<string> GenerateAndSaveRefreshTokenAsync(User user)
{
    var refreshToken = GenerateRefreshToken();
    user.RefreshToken = refreshToken;
    user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

    // 記錄刷新操作
    var auditLog = new AuditLog
    {
        UserId = user.Id,
        Action = "TokenRefreshed",
        Timestamp = DateTime.UtcNow,
        IpAddress = GetClientIp()  // 記錄來源 IP
    };
    context.AuditLogs.Add(auditLog);

    await context.SaveChangesAsync();
    return refreshToken;
}
```

### 常見錯誤

**1. 未驗證所有條件**
```csharp
// ❌ 錯誤：只檢查用戶存在，未檢查令牌有效性
if (user is null) return null;
// 應該還要檢查 RefreshToken 和 RefreshTokenExpiryTime！
```

**2. 訪問令牌有效期過長**
```csharp
// ❌ 錯誤：訪問令牌有效期太長，降低安全性
expires: DateTime.UtcNow.AddDays(7)

// ✓ 正確：訪問令牌應短期有效
expires: DateTime.UtcNow.AddMinutes(15)
```

**3. 未實施令牌輪換**
```csharp
// ❌ 錯誤：每次刷新時返回相同的刷新令牌
return new TokenResponseDto
{
    AccessToken = newAccessToken,
    RefreshToken = oldRefreshToken  // 重用舊令牌
};

// ✓ 正確：每次刷新都發行新的刷新令牌
return new TokenResponseDto
{
    AccessToken = newAccessToken,
    RefreshToken = newRefreshToken  // 新令牌，舊令牌失效
};
```

**4. 儲存敏感信息在刷新令牌中**
```csharp
// ❌ 不好：刷新令牌包含用戶信息（可被破譯）
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, user.Username),  // 不應包含
    new Claim("RefreshToken", token)
};

// ✓ 好：刷新令牌只是隨機數，信息存儲在服務器
var refreshToken = GenerateRefreshToken();  // 純隨機數
user.RefreshToken = refreshToken;           // 服務器驗證
```

### 安全考量

⚠️ **重要注意事項：**

1. **刷新令牌必須存儲在服務器** - 只有服務器知道有效的令牌，防止偽造

2. **使用 HTTPS** - 防止中間人攻擊竊取令牌

3. **HTTP Only Cookie** - 如果可能，將刷新令牌存儲在 HTTP Only Cookie 中，防止 JavaScript 訪問（XSS 防護）

4. **令牌輪換** - 每次刷新時發行新令牌，舊令牌自動失效，限制令牌被盜後的影響時間

5. **異常檢測** - 監控異常的刷新模式（如短時間內多次刷新失敗），可能表示被盜或攻擊

6. **有效期設置** - 訪問令牌應短期（防止被盜後長期濫用），刷新令牌應長期（提供足夠的會話延長時間）

### 測試刷新令牌流程

#### 使用 Scalar UI

1. 登入端點
   ```
   POST /api/auth/login
   {
     "username": "test",
     "password": "password123"
   }
   ```
   複製返回的 `refreshToken` 值

2. 刷新令牌端點
   ```
   POST /api/auth/refresh-token
   {
     "userId": "複製自登入響應的用戶ID",
     "refreshToken": "複製上方得到的 refreshToken"
   }
   ```
   驗證返回新的訪問令牌和刷新令牌

#### 使用 cURL

```bash
# 登入
RESPONSE=$(curl -s -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"password123"}')

# 提取令牌
ACCESS_TOKEN=$(echo $RESPONSE | jq -r '.accessToken')
REFRESH_TOKEN=$(echo $RESPONSE | jq -r '.refreshToken')
USER_ID=$(echo $RESPONSE | jq -r '.id')

# 刷新令牌
curl -s -X POST https://localhost:7XXX/api/auth/refresh-token \
  -H "Content-Type: application/json" \
  -d "{\"userId\":\"$USER_ID\",\"refreshToken\":\"$REFRESH_TOKEN\"}" \
  | jq
```

---

## 關鍵概念總結

| 概念 | 說明 |
|------|------|
| **Access Token** | 短期令牌，代表用戶身份，用於 API 請求驗證 |
| **Refresh Token** | 長期令牌，用於獲取新的訪問令牌 |
| **Token Rotation** | 每次刷新時發行新令牌，舊令牌失效 |
| **有效期** | 訪問令牌短期（1-15分鐘），刷新令牌長期（7-30天） |
| **服務器驗證** | 刷新令牌必須存儲在服務器，防止偽造 |
| **無縫體驗** | 用戶無需頻繁重新登入，會話可自動延長 |
| **安全窗口** | 被盜令牌的危害時間受訪問令牌有效期限制 |

---

## 進階話題預告

後續分支將涵蓋：
- 🔑 基於策略的授權（Policy-based Authorization）
- 🔐 令牌黑名單（Token Blacklist）
- 📊 刷新令牌輪換的進階策略
- 🛡️ 異常檢測與安全審計
- 👥 用戶會話管理

---

## 參考資源

### JWT 和刷新令牌
- [JWT 規範 RFC 7519](https://tools.ietf.org/html/rfc7519)
- [OAuth 2.0 授權框架](https://tools.ietf.org/html/rfc6749)
- [JSON Web Token 最佳實踐](https://tools.ietf.org/html/draft-ietf-oauth-jwt-bcp-07)

### ASP.NET Core 認證
- [ASP.NET Core JWT Bearer 認證](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn)
- [ASP.NET Core 認證與授權](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)

### 安全性
- [OWASP 認證速查表](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP 令牌儲存速查表](https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Web_Storage_Cheat_Sheet.html)
