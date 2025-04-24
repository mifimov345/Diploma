using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using FileService.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddSingleton<IFileMetadataService, FileMetadataService>();

builder.Services.AddHttpClient();

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];

var logger = LoggerFactory.Create(config => { config.AddConsole(); }).CreateLogger("Program");
logger.LogInformation("--- FileService JWT Config Loaded ---");
logger.LogInformation("Jwt:Key = {Key}", string.IsNullOrEmpty(jwtKey) ? "MISSING" : $"{jwtKey.Substring(0, Math.Min(5, jwtKey.Length))}..."); // Не логгируем весь ключ
logger.LogInformation("Jwt:Issuer = {Issuer}", jwtIssuer ?? "MISSING");
logger.LogInformation("Jwt:Audience = {Audience}", jwtAudience ?? "MISSING");
logger.LogInformation("------------------------------------");


if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    throw new ArgumentNullException(nameof(jwtKey), "JWT Key is missing or too short (min 32 chars) in configuration.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    logger.LogInformation("--- FileService Configuring JwtBearer ---");
    logger.LogInformation("ValidIssuer = {Issuer}", jwtIssuer);
    logger.LogInformation("ValidAudience = {Audience}", jwtAudience);
    var clockSkewSeconds = 30;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds)
    };
    logger.LogInformation("ClockSkew = {Skew} seconds", clockSkewSeconds);
    logger.LogInformation("---------------------------------------");

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            logger.LogError(context.Exception, "!!! FileService JWT Auth FAILED: {ExceptionType} - {Message}", context.Exception.GetType().Name, context.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var username = context.Principal.Identity?.Name ?? "N/A";
            logger.LogInformation("--- FileService JWT Token VALIDATED for user: {Username}", username);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
     {
        {
          new Microsoft.OpenApi.Models.OpenApiSecurityScheme
          {
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
              {
                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                Id = "Bearer"
              },
              Scheme = "oauth2",
              Name = "Bearer",
              In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            },
            new List<string>()
          }
        });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
        logger.LogInformation("Including XML comments from: {XmlPath}", xmlPath);
    }
    else
    {
        logger.LogWarning("XML comment file not found at: {XmlPath}", xmlPath);
    }
});

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:8080";
        logger.LogInformation("Configuring CORS for Frontend URL: {FrontendUrl}", frontendUrl);
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FileService API V1");
        c.RoutePrefix = string.Empty;
    });
    logger.LogInformation("Swagger UI enabled at service root.");
}

app.UseRouting();

// Применяем CORS ПЕРЕД UseAuthentication/UseAuthorization
app.UseCors("AllowFrontend");

//app.UseHttpsRedirection(); // Отключено для Docker

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Для интеграционных тестов
public partial class Program { }