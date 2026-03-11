using AuthenticationTest.Entities;
using AuthenticationTest.Models;
using AuthenticationTest.Requirements;
using AuthenticationTest.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AuthenticationTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController(IAuthService authService, IAuthorizationService authorizationService) : ControllerBase
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

        [Authorize]
        [HttpGet]
        public IActionResult AuthenticatedOnlyEndpoint()
        {
            return Ok("You are authenticated.");
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin-only")]
        public IActionResult AdminOnlyEndpoint()
        {
            return Ok("You are an Admin.");
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpGet("management")]
        public IActionResult ManagementEndpoint()
        {
            var message = User.IsInRole("Admin")
                ? "You are an Admin accessing management."
                : "You are a Manager accessing management.";

            return Ok(message);
        }

        [Authorize]
        [HttpGet("profile")]
        public IActionResult GetProfile()
        {
            var username = User.FindFirstValue(ClaimTypes.Name);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

            return Ok(new { Username = username, UserId = userId, Roles = roles });
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{userId:guid}/grant-role")]
        public async Task<ActionResult<User>> GrantRole(Guid userId, [FromBody] string role)
        {
            var user = await authService.GrantRoleAsync(userId, role);
            if (user is null) return NotFound("User not found.");
            return Ok(user);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{userId:guid}/revoke-role/{role}")]
        public async Task<ActionResult<User>> RevokeRole(Guid userId, string role)
        {
            var user = await authService.RevokeRoleAsync(userId, role);
            if (user is null) return NotFound("User not found.");
            return Ok(user);
        }

        // ── Claim-Based Authorization 示範端點 ──────────────────────────────

        /// <summary>
        /// 用戶只能更新自己的資料，Admin 可以更新任何人的資料。
        /// 使用 IAuthorizationService 進行資源授權：需在執行期傳入 resource（userId），
        /// 靜態特性（[Authorize]）無法做到這點，因為特性在編譯期就已確定。
        /// </summary>
        [Authorize]
        [HttpPut("{userId:guid}/profile")]
        public async Task<IActionResult> UpdateProfile(Guid userId, [FromBody] string newUsername)
        {
            // 使用 IAuthorizationService 進行資源授權：
            // 傳入 resource = userId，讓 Handler 決定此用戶是否有權限操作
            var authResult = await authorizationService.AuthorizeAsync(
                User,
                userId,
                new SameUserRequirement());

            if (!authResult.Succeeded)
            {
                return Forbid();
            }

            // 授權成功，執行更新邏輯
            var user = await authService.UpdateUsernameAsync(userId, newUsername);
            if (user is null) return NotFound("User not found.");

            return Ok(user);
        }
    }
}
