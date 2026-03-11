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

// 新增：註冊 IAuthorizationRequirementData 模式的 Handler
builder.Services.AddScoped<IAuthorizationHandler, MinimumRoleLevelAttributeHandler>();

// 定義具名授權策略（Policy-Based Authorization）
builder.Services.AddAuthorization(options =>
{
    // 策略一：要求具備 Manager 或以上等級
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Manager)));

    // 策略二：要求具備 Admin 等級
    options.AddPolicy("AdminOnly", policy =>
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin)));

    // 策略三：組合多個要求（AND 邏輯）
    options.AddPolicy("StrictAdminOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Name);
        policy.AddRequirements(new MinimumRoleLevelRequirement(RoleLevel.Admin));
    });

    // FallbackPolicy：未標記任何 [Authorize] 的端點，也預設要求已登入
    // 標記 [AllowAnonymous] 的端點可明確豁免
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
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
