using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ����������� ������������
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // ��������� �������������� ��� � camelCase
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });


// ��������� �������������� � �������������� JWT
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,                // �������� �������� ������
            ValidateAudience = true,              // �������� ��������� ������
            ValidateLifetime = true,              // �������� ����� �������� ������
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes("c036fd2a60b5cb97b364182eff8123f6")), // ��������� ����
            ClockSkew = TimeSpan.Zero             // ���������� ��������������� ���������� ���� ��� �������
        };
    });

builder.Services.AddControllers();

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
