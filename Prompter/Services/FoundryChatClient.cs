using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace Prompter.Services;

public class FoundryChatClient : IChatClient
{
    private readonly Microsoft.AI.Foundry.Local.OpenAIChatClient _client;

    public FoundryChatClient(Microsoft.AI.Foundry.Local.OpenAIChatClient client)
    {
        _client = client;
    }

    public async Task<string?> CompleteAsync(List<ChatMessage> messages, float temperature, CancellationToken ct)
    {
        var openAiMessages = messages.Select(m => new Betalgo.Ranul.OpenAI.ObjectModels.RequestModels.ChatMessage(m.Role, m.Content)).ToList();
        var originalTemperature = _client.Settings.Temperature;
        try
        {
            _client.Settings.Temperature = temperature;
            var response = await _client.CompleteChatAsync(openAiMessages, ct);
            return response?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        finally
        {
            _client.Settings.Temperature = originalTemperature;
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
