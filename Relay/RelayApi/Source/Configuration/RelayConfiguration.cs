namespace Relay.Configuration;

public class RelayConfiguration
{
    public int MaxConversationHistory { get; set; } = 50;
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1";

    public bool EnableClassification { get; set; } = false;

    public int MaxKnowledgeResults { get; set; } = 10;

    public bool EnablePlanning { get; set; } = false;

    public int MaxPlanSteps { get; set; } = 15;
}
