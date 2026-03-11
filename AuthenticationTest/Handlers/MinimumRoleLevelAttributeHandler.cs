using AuthenticationTest.Requirements;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace AuthenticationTest.Handlers
{
    /// <summary>
    /// 處理 MinimumRoleLevelAttribute（IAuthorizationRequirementData 模式）。
    /// 邏輯與 MinimumRoleLevelAuthorizationHandler 相同，
    /// 但 TRequirement 改為 MinimumRoleLevelAttribute。
    /// </summary>
    public class MinimumRoleLevelAttributeHandler
        : AuthorizationHandler<MinimumRoleLevelAttribute>
    {
        private static readonly Dictionary<string, RoleLevel> RoleLevelMap =
            new(StringComparer.OrdinalIgnoreCase)
        {
            { "User",    RoleLevel.User    },
            { "Manager", RoleLevel.Manager },
            { "Admin",   RoleLevel.Admin   }
        };

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            MinimumRoleLevelAttribute requirement)   // requirement 就是那個 Attribute 本身
        {
            var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value);

            var highestLevel = userRoles
                .Where(r => RoleLevelMap.ContainsKey(r))
                .Select(r => RoleLevelMap[r])
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
