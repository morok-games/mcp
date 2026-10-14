using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Assistant;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Ingestion;
using Platform.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<CatalogDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Catalog"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddCatalogIngestion();

// Swap the implementation here to change how user questions are understood
builder.Services.AddScoped<IQueryParser, LlmQueryParser>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ModelCircuitBreaker>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<ILlmClient, GeminiClient>((sp, http) =>
{
    var baseUrl = builder.Configuration[$"{GeminiOptions.SectionName}:BaseUrl"] ?? new GeminiOptions().BaseUrl;
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// wwwroot/index.html is the chat UI
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.Run();
