using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeChatClient : IChatClient
{
    private readonly Func<List<ChatMessage>, CancellationToken, string?>? _handler;

    public FakeChatClient(string? fixedResult)
    {
        _handler = (_, _) => fixedResult;
    }

    public FakeChatClient(Func<List<ChatMessage>, CancellationToken, string?> handler)
    {
        _handler = handler;
    }

    public Task<string?> CompleteAsync(List<ChatMessage> messages, float temperature, CancellationToken ct)
    {
        if (_handler == null)
            return Task.FromResult<string?>(null);
        return Task.FromResult(_handler(messages, ct));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
