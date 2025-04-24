using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddDebug();

builder.Services.AddHttpContextAccessor();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendSpecific", policy =>
    {
        var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://127.0.0.1:8080";
        policy.WithOrigins(frontendUrl)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // ����� ��� �������� ��� ��� ���������� �����������
    });
});


var app = builder.Build();



if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting(); // ������������� ����� ��� YARP

app.UseCors("AllowFrontendSpecific"); //  CORS


// ���������� ��������� YARP
app.MapReverseProxy();


app.Run();

// ������� public partial class Program { } ���� �� ��� ����� ������ ��� ������