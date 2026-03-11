using AuthenticationTest.Entities;
using AuthenticationTest.Models;
using AuthenticationTest.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AuthenticationTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IAuthService authService) : ControllerBase
    {
        [HttpPost("register")]
        public async Task<ActionResult<User>> Register(UserDto request)
        {
            var user = await authService.RegisterAsync(request);
            if (user is null)
            {
                return BadRequest("Username already existed.");
            }
            return Ok(user);
        }

        [HttpPost("login")]
        public async Task<ActionResult<TokenResponseDto>> Login(UserDto request)
        {
            var result = await authService.LoginAsync(request);
            if (result is null)
            {
                return BadRequest("Invalid username or password.");
            }

            return Ok(result);
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<TokenResponseDto>> RefreshToken(RefreshTokenRequestDto request)
        {
            var result = await authService.RefreshTokenAsync(request);

            if (result is null
                || result.AccessToken is null
                || result.RefreshToken is null)
            {
                return Unauthorized("Invalid refresh token.");
            }

            return Ok(result);
        }

        // 任何已認證用戶皆可訪問
        [Authorize]
        [HttpGet]
        public IActionResult AuthenticatedOnlyEndpoint()
        {
            return Ok("You are authenticated.");
        }

        // 只有 Admin 可以訪問
        [Authorize(Roles = "Admin")]
        [HttpGet("admin-only")]
        public IActionResult AdminOnlyEndpoint()
        {
            return Ok("You are an Admin.");
        }

        // Admin 或 Manager 皆可訪問（逗號代表 OR 邏輯）
        [Authorize(Roles = "Admin,Manager")]
        [HttpGet("management")]
        public IActionResult ManagementEndpoint()
        {
            // 使用 User.IsInRole() 進行程式化角色檢查
            var message = User.IsInRole("Admin")
                ? "You are an Admin accessing management."
                : "You are a Manager accessing management.";

            return Ok(message);
        }

        // 用戶個人資料端點：顯示目前登入用戶的角色資訊
        [Authorize]
        [HttpGet("profile")]
        public IActionResult GetProfile()
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            return Ok(new
            {
                Username = username,
                UserId = userId,
                Roles = roles
            });
        }

        // Admin 才能授予角色
        [Authorize(Roles = "Admin")]
        [HttpPut("{userId:guid}/grant-role")]
        public async Task<ActionResult<User>> GrantRole(Guid userId, [FromBody] string role)
        {
            var user = await authService.GrantRoleAsync(userId, role);
            if (user is null)
            {
                return NotFound("User not found.");
            }
            return Ok(user);
        }

        // Admin 才能撤銷角色
        [Authorize(Roles = "Admin")]
        [HttpDelete("{userId:guid}/revoke-role/{role}")]
        public async Task<ActionResult<User>> RevokeRole(Guid userId, string role)
        {
            var user = await authService.RevokeRoleAsync(userId, role);
            if (user is null)
            {
                return NotFound("User not found.");
            }
            return Ok(user);
        }
    }
}
