var builder = WebApplication.CreateBuilder(args);

// ����������� ������������
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // ��������� �������������� ��� � camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });


// ����������� ��������� ������������ Swagger � UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // ���������� ���� � XML-������������
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = System.IO.Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});

// ��������� CORS: ��������� ������� � ����� ����������, ������� � ����������
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

// � ������ ���������� ������������ Swagger UI
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
