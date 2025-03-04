var builder = WebApplication.CreateBuilder(args);

// Регистрация контроллеров и клиента для отправки HTTP-запросов
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Отключаем преобразование имён в camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddHttpClient();

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
