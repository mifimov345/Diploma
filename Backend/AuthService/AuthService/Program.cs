using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestHeadersTotalSize = 65536;
    Console.WriteLine("--- Kestrel Limits Configured (AuthService) ---");
});

System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Services.AddControllers(options => {
})
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddLogging();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header. Example: \"Authorization: Bearer {token}\""
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement {
        { new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }, new string[] {} }
    });
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) { options.IncludeXmlComments(xmlPath); }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy => {
        policy.WithOrigins("http://127.0.0.1:8080")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<IUserService, UserService>();

var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

Console.WriteLine("--- AuthService JWT Config Loaded ---");
Console.WriteLine($"Jwt:Key = {(string.IsNullOrEmpty(jwtKey) ? "NULL" : jwtKey.Substring(0, 5))}...");
Console.WriteLine($"Jwt:Issuer = {jwtIssuer ?? "NULL"}");
Console.WriteLine($"Jwt:Audience = {jwtAudience ?? "NULL"}");
Console.WriteLine("-------------------------------------");

if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32) { throw new ArgumentException("JWT Key..."); }

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        Console.WriteLine("--- AuthService Configuring JwtBearer (Manual Token Extraction) ---");
        Console.WriteLine($"ValidIssuer = {jwtIssuer}");
        Console.WriteLine($"ValidAudience = {jwtAudience}");
        Console.WriteLine($"ClockSkew = 30 seconds");
        Console.WriteLine("-----------------------------------------------------------------");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context => {
                string? authorization = context.Request.Headers["Authorization"].FirstOrDefault();

                if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = authorization.Substring("Bearer ".Length).Trim();
                    Console.WriteLine($"--- OnMessageReceived: Token Manually Set. Length: {context.Token?.Length ?? 0}");
                }
                else
                {
                    context.NoResult();
                    Console.WriteLine("--- OnMessageReceived: Token NOT found or invalid format in Authorization header.");
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context => {
                Console.WriteLine($"!!! AuthService JWT Auth FAILED: {context.Exception.GetType().Name} - {context.Exception.Message}");
                if (context.Exception is SecurityTokenInvalidIssuerException issuerEx) { Console.WriteLine($"!!! Invalid Issuer: '{issuerEx.InvalidIssuer}'"); }
                else if (context.Exception is SecurityTokenInvalidAudienceException audienceEx) { Console.WriteLine($"!!! Invalid Audience: '{audienceEx.InvalidAudience}'"); }
                else if (context.Exception is SecurityTokenExpiredException) { Console.WriteLine($"!!! Token expired."); }
                else if (context.Exception is SecurityTokenInvalidSignatureException) { Console.WriteLine($"!!! Invalid signature (Key mismatch?)."); }
                else if (context.Exception is SecurityTokenMalformedException malformedEx) { Console.WriteLine($"!!! Token malformed: {malformedEx.Message}"); }
                return Task.CompletedTask;
            },
            OnTokenValidated = context => {
                Console.WriteLine($"--- AuthService JWT Token VALIDATED for user: {context.Principal?.Identity?.Name}");
                return Task.CompletedTask;
            },
            OnChallenge = context => {
                Console.WriteLine($"!!! AuthService JWT Challenge triggered. Error: {context.Error}, Desc: {context.ErrorDescription}");
                context.HandleResponse();
                if (!context.Response.HasStarted) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options => {
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
       .RequireAuthenticatedUser().Build();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("AllowFrontend");

//app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }