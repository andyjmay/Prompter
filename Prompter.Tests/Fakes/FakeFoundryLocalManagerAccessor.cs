using Microsoft.AI.Foundry.Local;
using Prompter.Services;

namespace Prompter.Tests.Fakes;

public class FakeFoundryLocalManagerAccessor : IFoundryLocalManagerAccessor
{
    public Task InitializeAsync(int idleTtlMinutes) => Task.CompletedTask;
    public Task InitializationCompleted => Task.CompletedTask;
    public FoundryLocalManager Manager => throw new NotImplementedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
