# JWT 認證系統教學 - 第三部分：連接資料庫

## 專案概述

本專案是一個針對初級 .NET 工程師的教學範例，展示如何在 ASP.NET Core Web API 中實現用戶認證系統。本分支重點講解如何：

1. 安裝 Entity Framework Core 和 SQL Server 相關套件
2. 設計 DbContext 連接資料庫
3. 配置資料庫連接字符串
4. 創建數據庫遷移（Migrations）
5. 更新 API 端點以使用資料庫持久化

---

> **前置知識：** 請先完成前兩個分支 (`1_Register_And_Login` 和 `2_Generate_JWT_Token`)，了解基本的用戶認證實現。

---

## 第一步：連接資料庫（Entity Framework Core）

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

### 常見的 LINQ 查詢模式

在使用 DbContext 時常見的查詢操作：

```csharp
// 查詢單個用戶（非同步）
var user = await dbContext.Users
    .FirstOrDefaultAsync(u => u.Username == "alice");

// 檢查用戶是否存在
var exists = await dbContext.Users
    .AnyAsync(u => u.Username == "alice");

// 查詢所有用戶
var allUsers = await dbContext.Users.ToListAsync();

// 添加新用戶
var newUser = new User { Username = "bob", PasswordHash = "..." };
dbContext.Users.Add(newUser);
await dbContext.SaveChangesAsync();

// 更新用戶
user.Username = "alice_updated";
dbContext.Users.Update(user);
await dbContext.SaveChangesAsync();

// 刪除用戶
dbContext.Users.Remove(user);
await dbContext.SaveChangesAsync();
```

---

## 最佳實踐

**1. 使用 async/await 進行數據庫操作**
```csharp
// ✅ 好：使用非同步操作
public async Task<User?> GetUserAsync(string username)
{
    return await dbContext.Users
        .FirstOrDefaultAsync(u => u.Username == username);
}

// ❌ 不好：同步操作會阻塞線程
public User? GetUser(string username)
{
    return dbContext.Users.FirstOrDefault(u => u.Username == username);
}
```

**2. 使用 FirstOrDefaultAsync 而非 ToList()**
```csharp
// ✅ 好：只查詢一條記錄
var user = await dbContext.Users
    .FirstOrDefaultAsync(u => u.Username == username);

// ❌ 不好：加載所有用戶再篩選
var user = dbContext.Users.ToList()
    .FirstOrDefault(u => u.Username == username);
```

**3. 及時保存更改**
```csharp
// ✅ 好：每次修改後保存
dbContext.Users.Add(user);
await dbContext.SaveChangesAsync();

// ❌ 不好：忘記保存
dbContext.Users.Add(user);  // 不會持久化
```

**4. 正確處理連接字符串**
```csharp
// ✅ 好：從配置讀取
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ❌ 不好：硬編碼連接字符串
options.UseSqlServer("Server=...;Database=...;");
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

本分支涵蓋了 Entity Framework Core 和資料庫連接。後續分支將涵盖：
- 🏗️ Service 層重構（業務邏輯分離）
- 🛡️ API 認證中間件的實現（驗證 Token）
- 👤 角色和授權（Role-based Authorization）
- 🔐 刷新 Token 機制（防止 Token 過期）
- 📊 高級查詢和性能優化

---

## 參考資源

### Entity Framework Core
- [Entity Framework Core 官方文檔](https://docs.microsoft.com/en-us/ef/core/)
- [EF Core 遷移指南](https://docs.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 查詢基礎](https://docs.microsoft.com/en-us/ef/core/querying/)
- [SQL Server LocalDB](https://docs.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)

### 認證與安全
- [BCrypt.Net-Next GitHub](https://github.com/BcryptNet/bcrypt.net-next)
- [OWASP 密碼安全指南](https://owasp.org/www-project-cheat-sheets/cheatsheets/Authentication_Cheat_Sheet)
- [ASP.NET Core 安全最佳實踐](https://docs.microsoft.com/en-us/aspnet/core/security/)

### .NET 最佳實踐
- [依賴注入在 ASP.NET Core 中的使用](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection)
- [非同步程式設計最佳實踐](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [LINQ 基礎](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/)
