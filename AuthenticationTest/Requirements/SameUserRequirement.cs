using Microsoft.AspNetCore.Authorization;

namespace AuthenticationTest.Requirements
{
    /// <summary>
    /// 要求：只有資源擁有者本人（或 Admin）才能執行此操作
    /// </summary>
    public class SameUserRequirement : IAuthorizationRequirement { }
}
