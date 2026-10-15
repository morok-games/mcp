using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Platform.Api.Mcp;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Admin;
using Platform.Api.Assistant;
using Platform.Api.Catalog.Checkout;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Ingestion;
using Platform.Api.Catalog.Search;
using Platform.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// API controllers + MVC views for the admin panel
builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<CatalogDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Catalog"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddCatalogIngestion();

// Admin accounts live in the same database but in their own context and migrations
builder.Services.AddDbContext<AdminDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Catalog"))
    .UseSnakeCaseNamingConvention());
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddScoped<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/account/login";
        options.LogoutPath = "/admin/account/logout";
        options.Cookie.Name = "aicommerce.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// Swap implementations here to change how questions are understood and how the catalog is searched
builder.Services.AddScoped<IQueryParser, LlmQueryParser>();
builder.Services.AddScoped<ICatalogSearch, PostgresCatalogSearch>();
builder.Services.AddScoped<IAnswerComposer, LlmAnswerComposer>();
builder.Services.AddScoped<AssistantService>();

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<CheckoutOptions>(builder.Configuration.GetSection(CheckoutOptions.SectionName));
builder.Services.AddScoped<CheckoutTracker>();

// Remote MCP server for external AI assistants (ChatGPT, Claude, Perplexity); stateless Streamable HTTP at /mcp
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "ai-commerce-catalog", Title = "AI Commerce Catalog", Version = "1.0.0" };
        options.ServerInstructions =
            """
            Catalog of businesses and their offerings (currently online figure skating training programs, e.g. Victory Skating / VSA).
            Use search_offerings for any question about what a business offers, prices, schedules, coaches or levels.
            Answer only from tool results; never invent prices, dates or links.
            When the user wants to join or buy, call create_checkout and give them the returned checkout link with the price.
            """;
    })
    .WithHttpTransport()
    .WithTools<CatalogMcpTools>(new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
    {
        Converters = { new JsonStringEnumConverter() }
    });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ModelCircuitBreaker>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<ILlmClient, GeminiClient>((sp, http) =>
{
    var baseUrl = builder.Configuration[$"{GeminiOptions.SectionName}:BaseUrl"] ?? new GeminiOptions().BaseUrl;
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(120); // safety net only: each attempt has its own RequestTimeoutSeconds
});

var app = builder.Build();

await app.SeedAdminAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// wwwroot/index.html is the chat UI
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "admin/{controller=Businesses}/{action=Index}/{id?}");
app.MapControllers();
app.MapMcp("/mcp");

app.Run();
