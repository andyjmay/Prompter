namespace Prompter.Services;

public interface IHotkeyService : IAsyncDisposable
{
    void Initialize(string modifiers, string key);
    void UpdateHotkey(string modifiers, string key);
    void Unregister();

    event Action? RecordingStarted;
    event Action? RecordingStopped;
}
