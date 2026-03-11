using AuthenticationTest.Data;
using AuthenticationTest.Entities;
using AuthenticationTest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace AuthenticationTest.Services
{
    public class AuthService(UserDbContext context, IConfiguration configuration) : IAuthService
    {
        public async Task<TokenResponseDto?> LoginAsync(UserDto request)
        {
            var user = await context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user is null)
            {
                return null;
            }
            if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return null;
            }

            var response = new TokenResponseDto
            {
                AccessToken = CreateToken(user),
                RefreshToken = await GenerateAndSaveRefreshTokenAsync(user)
            };

            return response;

  
        }

        public async Task<User?> RegisterAsync(UserDto request)
        {
            if (await context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return null;
            }

            var user = new User();
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            user.Username = request.Username;
            user.PasswordHash = hashedPassword;

            context.Add(user);
            await context.SaveChangesAsync();

            return user;
        }

        public async Task<TokenResponseDto?> RefreshTokenAsync(RefreshTokenRequestDto request)
        {
            var user = await ValidateRefreshTokenAsync(request.UserId, request.RefreshToken);
            if (user is null) 
            { 
                return null; 
            }
            var response = new TokenResponseDto
            {
                AccessToken = CreateToken(user),
                RefreshToken = await GenerateAndSaveRefreshTokenAsync(user)
            };
            return response;
        }

        private async Task<User?> ValidateRefreshTokenAsync(Guid userId, string refreshToken)
        {
            var user = await context.Users.FindAsync(userId);
            if(user is null 
                || user.RefreshToken != refreshToken
                || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            {
                return null;
            }

            return user;
        }

        private string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private async Task<string> GenerateAndSaveRefreshTokenAsync(User user)
        {
            var refreshToken = GenerateRefreshToken();
            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await context.SaveChangesAsync();
            return refreshToken;
        }

        public async Task<User?> GrantRoleAsync(Guid userId, string role)
        {
            var user = await context.Users.FindAsync(userId);
            if (user is null) return null;

            var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(r => r.Trim())
                             .ToList();

            if (!roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                roles.Add(role);
                user.Role = string.Join(",", roles);
                await context.SaveChangesAsync();
            }

            return user;
        }

        public async Task<User?> RevokeRoleAsync(Guid userId, string role)
        {
            var user = await context.Users.FindAsync(userId);
            if (user is null) return null;

            var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(r => r.Trim())
                             .Where(r => !r.Equals(role, StringComparison.OrdinalIgnoreCase))
                             .ToList();

            user.Role = string.Join(",", roles);
            await context.SaveChangesAsync();

            return user;
        }

        public async Task<User?> UpdateUsernameAsync(Guid userId, string newUsername)
        {
            var user = await context.Users.FindAsync(userId);
            if (user is null) return null;

            user.Username = newUsername;
            await context.SaveChangesAsync();

            return user;
        }

        private string CreateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
            };

            // 支援多重角色：將逗號分隔的角色字串轉換為多個 Role Claims
            var roles = user.Role.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(r => r.Trim())
                             .Where(r => !string.IsNullOrEmpty(r));
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(configuration.GetValue<string>("AppSettings:Token")!));

            // the key must be at least 128 bits (16 bytes) for HmacSha512,
            // so ensure that the token in the configuration is sufficiently long
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

            var tokenDescriptor = new JwtSecurityToken(
                issuer: configuration.GetValue<string>("AppSettings:Issuer"),
                audience: configuration.GetValue<string>("AppSettings:Audience"),
                claims: claims,
                expires: DateTime.UtcNow.AddDays(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        }


    }
}
