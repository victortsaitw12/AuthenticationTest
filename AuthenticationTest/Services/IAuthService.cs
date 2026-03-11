using AuthenticationTest.Entities;
using AuthenticationTest.Models;

namespace AuthenticationTest.Services
{
    public interface IAuthService
    {
        Task<User?> RegisterAsync(UserDto request);
        Task<TokenResponseDto?> LoginAsync(UserDto request);
        Task<TokenResponseDto?> RefreshTokenAsync(RefreshTokenRequestDto request);
        Task<User?> GrantRoleAsync(Guid userId, string role);
        Task<User?> RevokeRoleAsync(Guid userId, string role);
    }
}
