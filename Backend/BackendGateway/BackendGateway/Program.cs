using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole(options => options.FormatterName = "simple");
builder.Logging.AddDebug();


builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();

// Настройка CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendSpecific", policy =>
    {
        policy.WithOrigins("http://127.0.0.1:8080")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddLogging();


var app = builder.Build();

app.Use(async (context, next) => {
    context.Request.EnableBuffering();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogDebug("Request body buffering enabled for path: {Path}", context.Request.Path);
    await next.Invoke();
});


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.UseCors("AllowFrontendSpecific");

// HTTPS Redirection - отключен
// app.UseHttpsRedirection();

// Аутентификация/Авторизация - отключены на шлюзе
// app.UseAuthentication();
// app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }