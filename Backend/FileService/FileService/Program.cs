using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using FileService.Services;           // Добавлено
using System.Text.Json.Serialization; // Для игнорирования циклов

var builder = WebApplication.CreateBuilder(args);

// --- Добавление сервисов ---

// Добавляем логгирование из конфигурации
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Игнорирование циклов при сериализации (если вдруг появятся сложные связи)
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Оставляем имена свойств как в C# (PascalCase) - ваш предыдущий вариант
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// Регистрация FileMetadataService (in-memory)
builder.Services.AddSingleton<IFileMetadataService, FileMetadataService>();

// --- Добавляем HttpClientFactory ---
builder.Services.AddHttpClient();
// ---------------------------------

// Чтение конфигурации JWT
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];

// --- Расширенное логирование конфигурации JWT ---
var logger = LoggerFactory.Create(config => { config.AddConsole(); }).CreateLogger("Program");
logger.LogInformation("--- FileService JWT Config Loaded ---");
logger.LogInformation("Jwt:Key = {Key}", string.IsNullOrEmpty(jwtKey) ? "MISSING" : $"{jwtKey.Substring(0, Math.Min(5, jwtKey.Length))}..."); // Не логгируем весь ключ
logger.LogInformation("Jwt:Issuer = {Issuer}", jwtIssuer ?? "MISSING");
logger.LogInformation("Jwt:Audience = {Audience}", jwtAudience ?? "MISSING");
logger.LogInformation("------------------------------------");
// --- Конец логирования JWT ---


if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    throw new ArgumentNullException(nameof(jwtKey), "JWT Key is missing or too short (min 32 chars) in configuration.");
}

// Настройка аутентификации JWT
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
    // Увеличим допустимое расхождение времени, чтобы избежать проблем с синхронизацией в Docker
    var clockSkewSeconds = 30; // 30 секунд
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds) // Допустимое расхождение времени
    };
    logger.LogInformation("ClockSkew = {Skew} seconds", clockSkewSeconds);
    logger.LogInformation("---------------------------------------");

    // Добавляем логирование ошибок аутентификации
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

// Добавляем поддержку авторизации
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Добавление поддержки JWT в Swagger UI
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey, // Или Http с BearerFormat = "JWT" и Scheme = "bearer"
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

    // Включение XML-комментариев (если есть)
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
        // Укажите URL вашего фронтенда (из docker-compose это обычно http://localhost:8080)
        // Или используйте '*' для разработки, но будьте осторожны
        var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:8080";
        logger.LogInformation("Configuring CORS for Frontend URL: {FrontendUrl}", frontendUrl);
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Может понадобиться для некоторых сценариев
    });
});


var app = builder.Build();

// --- Настройка Pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // Настройки Swagger UI
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FileService API V1");
        c.RoutePrefix = string.Empty; // Доступ к Swagger UI по корневому пути сервиса
    });
    logger.LogInformation("Swagger UI enabled at service root.");
}

app.UseRouting();

// Применяем CORS ПЕРЕД UseAuthentication/UseAuthorization
app.UseCors("AllowFrontend");

//app.UseHttpsRedirection(); // Отключено для Docker

app.UseAuthentication(); // Включаем аутентификацию
app.UseAuthorization(); // Включаем авторизацию

app.MapControllers();

app.Run();

// Для интеграционных тестов
public partial class Program { }