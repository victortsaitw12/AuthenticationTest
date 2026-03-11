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

        [Authorize]
        [HttpPut("{userId:guid}/profile")]
        public async Task<IActionResult> UpdateProfile(Guid userId, [FromBody] string newUsername)
        {
            var authResult = await authorizationService.AuthorizeAsync(
                User, userId, new SameUserRequirement());

            if (!authResult.Succeeded) return Forbid();

            var user = await authService.UpdateUsernameAsync(userId, newUsername);
            if (user is null) return NotFound("User not found.");

            return Ok(user);
        }

        // ── Policy-Based Authorization 示範端點 ──────────────────────────────

        // 使用具名 Policy：Manager 或以上等級才能訪問
        [Authorize(Policy = "ManagerOrAbove")]
        [HttpGet("reports")]
        public IActionResult GetReports()
        {
            return Ok("Reports are available for Manager and above.");
        }

        // 使用具名 Policy：Admin 才能訪問
        [Authorize(Policy = "AdminOnly")]
        [HttpGet("system-config")]
        public IActionResult GetSystemConfig()
        {
            return Ok("System configuration is only for Admins.");
        }

        // 使用組合 Policy：嚴格的 Admin 檢查（多個要求的 AND 組合）
        [Authorize(Policy = "StrictAdminOnly")]
        [HttpDelete("users/{userId:guid}")]
        public async Task<IActionResult> DeleteUser(Guid userId)
        {
            var user = await authService.DeleteUserAsync(userId);
            if (user is null) return NotFound("User not found.");
            return Ok($"User {user.Username} has been deleted.");
        }

        // 程式化 Policy 評估：在方法內部動態決定使用哪個 Policy
        [Authorize]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            // 根據用戶等級返回不同的儀表板內容
            var adminCheck = await authorizationService.AuthorizeAsync(User, "AdminOnly");
            if (adminCheck.Succeeded)
            {
                return Ok(new { level = "Admin", data = "Full system dashboard with all metrics." });
            }

            var managerCheck = await authorizationService.AuthorizeAsync(User, "ManagerOrAbove");
            if (managerCheck.Succeeded)
            {
                return Ok(new { level = "Manager", data = "Team performance dashboard." });
            }

            return Ok(new { level = "User", data = "Personal activity dashboard." });
        }
    }
}
