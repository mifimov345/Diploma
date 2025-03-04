using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Регистрация контроллеров
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Отключаем преобразование имён в camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });


// Настройка аутентификации с использованием JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,                // Проверка издателя токена
            ValidateAudience = true,              // Проверка аудитории токена
            ValidateLifetime = true,              // Проверка срока действия токена
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes("c036fd2a60b5cb97b364182eff8123f6")), // Секретный ключ
            ClockSkew = TimeSpan.Zero             // Отсутствие дополнительного временного окна для токенов
        };
    });

builder.Services.AddControllers();

// Регистрация генерации документации Swagger и UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Определяем путь к XML-документации
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});
var app = builder.Build();

// В режиме разработки активируется Swagger UI для тестирования API
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
