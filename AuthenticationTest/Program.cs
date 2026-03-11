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

// 註冊 Claim-Based Authorization Handler
builder.Services.AddScoped<IAuthorizationHandler, SameUserAuthorizationHandler>();

// 定義使用 Claim 的具名策略（特性方式）
builder.Services.AddAuthorization(options =>
{
    // 要求 Token 必須包含 NameIdentifier Claim（已登入且有 UserId）
    options.AddPolicy("HasUserId", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.NameIdentifier));

    // 要求 Role Claim 值為 "Admin"（等同於 [Authorize(Roles = "Admin")]，但用 Claim 角度理解）
    options.AddPolicy("RequireAdminClaim", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admin"));
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
