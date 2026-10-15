namespace Platform.Api.Admin;

/// <summary>
/// Bootstrap admin, created on startup only while the admin table is empty.
/// Keep the password in user-secrets or environment variables, never in appsettings.json.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";
}
