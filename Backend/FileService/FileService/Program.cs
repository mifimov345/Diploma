var builder = WebApplication.CreateBuilder(args);

// Регистрация контроллеров
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Отключаем преобразование имён в camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });


// Регистрация генерации документации Swagger и UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Определяем путь к XML-документации
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

// Настройка CORS: разрешены запросы с любых источников, методов и заголовков
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// В режиме разработки активируется Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.UseRouting();
app.UseEndpoints(endpoints => endpoints.MapControllers());

app.Run();

public partial class Program { }
