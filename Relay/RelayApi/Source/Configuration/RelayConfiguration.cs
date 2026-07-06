namespace Relay.Configuration;

public class RelayConfiguration
{
    public int MaxConversationHistory { get; set; } = 50;
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.1";
}
