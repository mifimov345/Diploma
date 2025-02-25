using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,                // Проверка издателя токена
            ValidateAudience = true,              // Проверка аудитории токена
            ValidateLifetime = true,              // Проверка срока действия токена
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes("c036fd2a60b5cb97b364182eff8123f6")), // Секретный ключ
            ClockSkew = TimeSpan.Zero            // Нет дополнительного времени для токенов (опционально)
        };
    });

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
