namespace Platform.Api.Services;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Key from https://aistudio.google.com/apikey. Do not store it in appsettings.json; use user-secrets.</summary>
    public string ApiKey { get; set; } = "";

    public string Model { get; set; } = "gemini-3.5-flash";

    /// <summary>Models tried in order when the primary one is overloaded, rate-limited or retired.</summary>
    public string[] FallbackModels { get; set; } = [];

    /// <summary>How long a failed model is skipped before it is tried again.</summary>
    public int CircuitBreakSeconds { get; set; } = 300;

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    public string SystemPrompt { get; set; } = "You are a helpful assistant.";
}
