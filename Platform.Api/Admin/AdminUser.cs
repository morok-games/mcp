namespace Platform.Api.Admin;

public class AdminUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Login name, stored lowercased.</summary>
    public required string Username { get; set; }

    /// <summary>ASP.NET Core Identity PasswordHasher format (PBKDF2), never the plain password.</summary>
    public string PasswordHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();
}
