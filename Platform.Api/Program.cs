using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Platform.Api.Mcp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Platform.Api;
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
        // API callers (the chat page's fetch) get 401/403 instead of an HTML login redirect
        options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
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
    })
    // tools/list: the search tool description names the businesses currently in the catalog
    .WithRequestFilters(filters => filters.AddListToolsFilter(next => async (context, ct) =>
    {
        var result = await next(context, ct);
        var descriptions = context.Services!.GetRequiredService<McpToolDescriptions>();
        return await descriptions.ApplyAsync(result, ct);
    }));
builder.Services.AddMemoryCache();
builder.Services.AddScoped<McpToolDescriptions>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ModelCircuitBreaker>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<ILlmClient, GeminiClient>((sp, http) =>
{
    var baseUrl = builder.Configuration[$"{GeminiOptions.SectionName}:BaseUrl"] ?? new GeminiOptions().BaseUrl;
    http.BaseAddress = new Uri(baseUrl);
    http.Timeout = TimeSpan.FromSeconds(120); // safety net only: each attempt has its own RequestTimeoutSeconds
});

// Behind nginx: trust X-Forwarded-For/Proto from the local proxy so the app sees the real client IP and https scheme
// (otherwise tracked /go links would be built as http://127.0.0.1 and HTTPS redirection would loop)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// Persist cookie encryption keys so admins stay signed in across service restarts
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("ai-commerce");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

// Public endpoints get per-IP limits: MCP calls and admin login attempts
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimits.Mcp, context => RateLimits.PerIp(context, permitPerMinute: 60));
    options.AddPolicy(RateLimits.Login, context => RateLimits.PerIp(context, permitPerMinute: 10));
});

var app = builder.Build();

// "Platform.Api --migrate" applies database migrations for both contexts and exits (used on deployment)
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AdminDbContext>().Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations applied");
    return;
}

await app.SeedAdminAsync();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapAreaControllerRoute(
    name: "admin",
    areaName: "Admin",
    pattern: "admin/{controller=Businesses}/{action=Index}/{id?}");
app.MapControllers();
app.MapMcp("/mcp").RequireRateLimiting(RateLimits.Mcp);

// The site root opens the admin panel's start page (the businesses list)
app.MapGet("/", () => Results.Redirect("/admin"));

app.Run();

static Task ApiAwareRedirect(RedirectContext<CookieAuthenticationOptions> context, int apiStatusCode)
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.StatusCode = apiStatusCode;
    else
        context.Response.Redirect(context.RedirectUri);

    return Task.CompletedTask;
}
