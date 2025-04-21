using SearchService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient("SearchServiceClient", client => {});
builder.Services.AddSingleton<ITextExtractorService, TextExtractorService>();
builder.Services.AddSingleton<IIndexService, IndexService>();
builder.Services.AddSingleton<ISearcherService, SearcherService>();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => { });
    logger.LogInformation("Swagger UI enabled at service root.");
}

// app.UseHttpsRedirection();

app.UseRouting();

// app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/searchtest", () => {
    logger.LogCritical("!!!!!! Minimal API /api/searchtest REACHED !!!!!!");
    return Results.Ok(new { TestMessage = "Minimal API endpoint hit!" });
});

logger.LogInformation("SearchService application starting...");
app.Run();