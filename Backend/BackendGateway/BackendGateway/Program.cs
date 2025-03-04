var builder = WebApplication.CreateBuilder(args);

// ����������� ������������ � ������� ��� �������� HTTP-��������
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // ��������� �������������� ��� � camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

builder.Services.AddHttpClient();

// ����������� ��������� ������������ Swagger � UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // ���������� ���� � XML-������������
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});


var app = builder.Build();

// � ������ ���������� ������������ Swagger UI ��� ������������ API
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
