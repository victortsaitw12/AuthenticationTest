using AuthenticationTest.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace AuthenticationTest.Handlers
{
    /// <summary>
    /// 處理 SameUserRequirement：
    /// 允許條件：操作者是資源擁有者本人，或操作者是 Admin
    /// </summary>
    public class SameUserAuthorizationHandler : AuthorizationHandler<SameUserRequirement, Guid>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SameUserRequirement requirement,
            Guid resourceUserId)
        {
            var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

            // 條件一：操作的用戶 ID 等於資源擁有者的 ID
            var isSameUser = currentUserId == resourceUserId.ToString();

            // 條件二：操作者是 Admin，可以代替任何人操作
            var isAdmin = context.User.IsInRole("Admin");

            if (isSameUser || isAdmin)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
