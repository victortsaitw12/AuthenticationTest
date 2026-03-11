using Microsoft.AspNetCore.Authorization;

namespace AuthenticationTest.Requirements
{
    /// <summary>
    /// 自定義授權特性：實作 IAuthorizationRequirementData，
    /// 讓 Attribute 本身就攜帶授權資料，
    /// 使用時不需要在 AddAuthorization() 中預先登記 Policy 名稱。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class MinimumRoleLevelAttribute : Attribute, IAuthorizationRequirement, IAuthorizationRequirementData
    {
        public RoleLevel MinimumLevel { get; }

        public MinimumRoleLevelAttribute(RoleLevel minimumLevel)
        {
            MinimumLevel = minimumLevel;
        }

        // IAuthorizationRequirementData 介面唯一的方法：
        // 告訴框架「這個 Attribute 帶著哪些 Requirement」
        public IEnumerable<IAuthorizationRequirement> GetRequirements()
        {
            yield return this; // Attribute 本身就是 Requirement
        }
    }
}
