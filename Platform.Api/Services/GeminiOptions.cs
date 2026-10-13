namespace Platform.Api.Services;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Key from https://aistudio.google.com/apikey. Do not store it in appsettings.json; use user-secrets.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "gemini-2.5-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
}
