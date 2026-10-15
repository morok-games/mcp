using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Platform.Api.Admin;

public static class AdminSeeder
{
    private const int MinPasswordLength = 8;

    /// <summary>Creates the first admin from configuration if there are no admins yet.</summary>
    public static async Task SeedAdminAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value;
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminUser>>();
        var logger = app.Logger;

        try
        {
            if (await db.Admins.AnyAsync())
                return;

            if (string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
            {
                logger.LogWarning("No admins exist and {Section}:Username/Password are not configured: the admin panel is not accessible",
                    AdminOptions.SectionName);
                return;
            }

            if (options.Password.Length < MinPasswordLength)
            {
                logger.LogWarning("Bootstrap admin was not created: {Section}:Password must be at least {Length} characters",
                    AdminOptions.SectionName, MinPasswordLength);
                return;
            }

            var admin = new AdminUser
            {
                Username = AdminUser.Normalize(options.Username),
                CreatedAt = DateTimeOffset.UtcNow
            };
            admin.PasswordHash = hasher.HashPassword(admin, options.Password);

            db.Admins.Add(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Bootstrap admin {Username} created", admin.Username);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The app still serves chat and API; only the admin panel needs this table
            logger.LogWarning("Admin table does not exist: run 'dotnet dotnet-ef database update --context AdminDbContext'");
        }
    }
}
