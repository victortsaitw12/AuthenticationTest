using AuthenticationTest.Entities;
using AuthenticationTest.Models;

namespace AuthenticationTest.Services
{
    public interface IAuthService
    {
        Task<User?> RegisterAsync(UserDto request);
        Task<string?> LoginAsync(UserDto request);
    }
}
