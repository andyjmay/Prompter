namespace Prompter.Services;

public interface IFoundryLocalManagerAccessor : IAsyncDisposable
{
    Task InitializeAsync(int idleTtlMinutes);
    Task InitializationCompleted { get; }
    Microsoft.AI.Foundry.Local.FoundryLocalManager Manager { get; }
}
