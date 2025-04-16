using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using FileService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using Microsoft.AspNetCore.Authorization;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// --- Service Registration ---
builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });
builder.Services.AddSingleton<IFileMetadataService, FileMetadataService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddLogging();

// Swagger
builder.Services.AddSwaggerGen(options => {
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
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
        Console.WriteLine($"Including XML comments from: {xmlPath}");
    }
    else
    {
        Console.WriteLine($"XML comments file not found at: {xmlPath}");
    }
});

// CORS
builder.Services.AddCors(options => {
    options.AddPolicy("AllowFrontend", policy => {
        policy.WithOrigins("http://127.0.0.1:8080")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// JWT AuthN & AuthZ
var jwtKey = builder.Configuration["Jwt:Key"];
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

Console.WriteLine("--- FileService JWT Config Loaded ---"); // Лог для проверки
Console.WriteLine($"Jwt:Key = {(string.IsNullOrEmpty(jwtKey) ? "NULL" : jwtKey.Substring(0, 5))}...");
Console.WriteLine($"Jwt:Issuer = {jwtIssuer ?? "NULL"}");
Console.WriteLine($"Jwt:Audience = {jwtAudience ?? "NULL"}");
Console.WriteLine("------------------------------------");

if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32) { throw new ArgumentException("JWT Key..."); }

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options => {
        Console.WriteLine("--- FileService Configuring JwtBearer ---"); // Лог для проверки
        Console.WriteLine($"ValidIssuer = {jwtIssuer}");
        Console.WriteLine($"ValidAudience = {jwtAudience}");
        Console.WriteLine($"ClockSkew = 30 seconds");
        Console.WriteLine("---------------------------------------");

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
            OnAuthenticationFailed = context => {
                Console.WriteLine($"!!! FileService JWT Auth FAILED: {context.Exception.GetType().Name} - {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context => {
                Console.WriteLine($"--- FileService JWT Token VALIDATED for user: {context.Principal?.Identity?.Name}");
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }