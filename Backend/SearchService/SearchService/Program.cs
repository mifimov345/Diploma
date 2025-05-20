using SearchService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IIndexService, IndexService>();
builder.Services.AddScoped<ISearcherService, SearcherService>();
builder.Services.AddScoped<ITextExtractorService, TextExtractorService>();
builder.Services.AddSingleton<MinioService>();


builder.Services.AddControllers();

builder.Services.AddHttpClient();

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();
app.Run();
