namespace InsuranceClaimsTracker.Infrastructure.Ai;

public class AiServiceSettings
{
    public const string SectionName = "AiService";

    public string Provider { get; set; } = "Gemini";
    public string ModelId { get; set; } = "gemini-2.5-flash";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 2;

    public string EffectiveModel => !string.IsNullOrWhiteSpace(ModelId) ? ModelId : Model;
}
