using AuthenticationTest.Data;
using AuthenticationTest.Handlers;
using AuthenticationTest.Requirements;
using AuthenticationTest.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["AppSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["AppSettings:Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["AppSettings:Token"]!))
        };
    });

builder.Services.AddScoped<IAuthService, AuthService>();

// 註冊 Authorization Handlers
builder.Services.AddScoped<IAuthorizationHandler, SameUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, MinimumRoleLevelAuthorizationHandler>();

// 定義具名授權策略（Policy-Based Authorization）
builder.Services.AddAuthorization(options =>
{
    // 策略一：要求用戶已登入（最低門檻）
    options.AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());

    // 策略二：要求具備 Manager 或以上等級
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

    // 策略三：要求具備 Admin 等級
    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));

    // 策略四：組合多個要求（AND 邏輯）- 必須是 Admin 且 Token 包含 Name Claim
    options.AddPolicy("StrictAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Name);
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin));
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
