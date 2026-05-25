namespace Prompter.Services;

public record ChatMessage(string Role, string Content);

public interface IChatClient : IAsyncDisposable
{
    Task<string?> CompleteAsync(List<ChatMessage> messages, float temperature, CancellationToken ct);
}
