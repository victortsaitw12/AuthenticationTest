# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **teaching project** for .NET engineers to learn JWT authentication implementation in ASP.NET Core. The repository uses a **branching strategy** where each branch represents a progressive stage of the authentication system:

- **Branch: `1_Register_And_Login`** (current) - User registration and login with password hashing
- Future branches will add JWT tokens, authorization, database integration, etc.

**Target Framework:** .NET 9.0

## Building and Running

### Prerequisites
- .NET 9.0 SDK installed
- Visual Studio 2022 or VS Code with C# extension

### Build
```bash
dotnet build
```

### Run the Project
```bash
dotnet run
```

The API will start at `https://localhost:7XXX`. Scalar API documentation is available at the root URL in development mode.

## Project Structure

```
AuthenticationTest/
├── Controllers/
│   └── AuthController.cs       # API endpoints (register, login)
├── Entities/
│   └── User.cs                 # Database entity with Username and PasswordHash
├── Models/
│   └── UserDto.cs              # DTO for API requests (receives plain password)
├── Program.cs                  # ASP.NET Core configuration and middleware setup
└── AuthenticationTest.csproj   # Project file with NuGet dependencies
```

### Key Architectural Pattern

**Entity vs. DTO Separation:**
- `User` (Entity): Stored in persistence layer with `PasswordHash`
- `UserDto` (Model): Received from API requests with plain `Password`
- This ensures plain passwords are never persisted; they're immediately hashed

## Important Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| BCrypt.Net-Next | 4.1.0 | Password hashing with automatic salt generation |
| Scalar.AspNetCore | 2.13.1 | Modern API documentation UI (OpenAPI) |
| Microsoft.AspNetCore.OpenApi | 9.0.9 | OpenAPI specification support |

## API Endpoints

### POST /api/auth/register
**Request:**
```json
{
  "username": "john_doe",
  "password": "SecurePassword123!"
}
```
**Response:** Returns the User entity with hashed password (200 OK)

### POST /api/auth/login
**Request:**
```json
{
  "username": "john_doe",
  "password": "SecurePassword123!"
}
```
**Response:** Returns success message or error (200 OK or 400 Bad Request)

## Testing the API

### Using Scalar UI
1. Run `dotnet run`
2. Open browser to `https://localhost:7XXX`
3. Use the interactive "Try it" buttons to test endpoints

### Using cURL
```bash
# Register
curl -X POST https://localhost:7XXX/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"password123"}'

# Login
curl -X POST https://localhost:7XXX/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"password123"}'
```

## Code Patterns to Follow

### Password Handling
- **Always** use `BCrypt.Net.BCrypt.HashPassword()` to hash passwords during registration
- **Never** store or log plain text passwords
- **Always** use `BCrypt.Net.BCrypt.Verify()` to validate passwords during login
- BCrypt automatically handles salt generation and storage in the hash string

### Controller Pattern
- Controllers inherit from `ControllerBase`
- Methods are marked with HTTP verb attributes (`[HttpPost]`, `[HttpGet]`, etc.)
- Return `ActionResult<T>` for proper HTTP status code handling
- Use `Ok()` for 200 responses, `BadRequest()` for 400 responses

### Data Validation
Currently minimal - future branches should implement:
- Username validation (not empty, length constraints)
- Password strength validation
- Duplicate username checking
- Rate limiting on login attempts

## Git Workflow

This is a **teaching repository** with a branching strategy. When creating new branches:

1. Branch names follow pattern: `N_FeatureName` (e.g., `2_JWT_Tokens`, `3_Database_Integration`)
2. Commit messages are in **Traditional Chinese** (繁體中文)
3. README.md is updated with comprehensive teaching content for each branch
4. Each branch represents a complete, self-contained teaching unit

## Common Development Tasks

### Adding a New API Endpoint
1. Create method in `AuthController.cs`
2. Mark with appropriate `[HttpPost]`, `[HttpGet]`, etc. attribute
3. Define request/response DTOs if needed
4. Update README.md with endpoint documentation

### Updating Password Hashing Logic
- All password operations are in `AuthController.cs`
- `Register()` method handles hashing
- `Login()` method handles verification
- Both use BCrypt.Net-Next library

### Testing Changes
- Use Scalar UI at `https://localhost:7XXX` after running
- Test both success and error cases
- Example: test login with wrong password to ensure it returns 400 Bad Request

## Notes for Future Branches

When extending this project:
- **Persistence:** Currently uses in-memory static User. Future branches should add EF Core and database
- **Authentication:** Current branch uses basic validation. Next branch should implement JWT tokens
- **Authorization:** Future branches should implement role-based or policy-based authorization
- **Error Handling:** Enhance with more specific error codes and messages
- **Logging:** Add structured logging for debugging and security auditing

## Security Considerations

This is a **teaching project** with simplified implementation. Production deployments should:
- Add HTTPS certificate validation
- Implement rate limiting on login endpoints
- Add CORS policies
- Use secrets management for sensitive configuration
- Implement proper database with Entity Framework Core
- Add audit logging for security events
- Implement JWT refresh token rotation
- Use secure session management