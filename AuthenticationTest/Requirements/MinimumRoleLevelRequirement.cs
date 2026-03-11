using Microsoft.AspNetCore.Authorization;

namespace AuthenticationTest.Requirements
{
    /// <summary>
    /// 角色等級定義：數字越大，權限越高
    /// </summary>
    public enum RoleLevel
    {
        User    = 1,
        Manager = 2,
        Admin   = 3
    }

    /// <summary>
    /// 要求：用戶的角色等級必須達到指定的最低等級
    /// </summary>
    public class MinimumRoleLevelRequirement : IAuthorizationRequirement
    {
        public RoleLevel MinimumLevel { get; }

        public MinimumRoleLevelRequirement(RoleLevel minimumLevel)
        {
            MinimumLevel = minimumLevel;
        }
    }
}
