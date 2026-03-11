using AuthenticationTest.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace AuthenticationTest.Handlers
{
    /// <summary>
    /// 處理 MinimumRoleLevelRequirement：
    /// 檢查用戶的最高角色等級是否達到要求的最低等級
    /// </summary>
    public class MinimumRoleLevelAuthorizationHandler
        : AuthorizationHandler<MinimumRoleLevelRequirement>
    {
        // 角色名稱 → 等級的對應表
        private static readonly Dictionary<string, RoleLevel> RoleLevelMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "User",    RoleLevel.User    },
            { "Manager", RoleLevel.Manager },
            { "Admin",   RoleLevel.Admin   }
        };

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            MinimumRoleLevelRequirement requirement)
        {
            // 取出用戶擁有的所有 Role Claims
            var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

            // 將角色名稱轉換為等級，取出最高等級
            var highestLevel = userRoles
                .Where(role => RoleLevelMap.ContainsKey(role))
                .Select(role => RoleLevelMap[role])
                .DefaultIfEmpty(0)
                .Max();

            if ((int)highestLevel >= (int)requirement.MinimumLevel)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
