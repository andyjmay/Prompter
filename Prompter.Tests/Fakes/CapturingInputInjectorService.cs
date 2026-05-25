using Prompter.Services;

namespace Prompter.Tests.Fakes;

public enum InjectionMode
{
    TypeText,
    ClipboardPaste,
    SendKeys
}

public record InjectionCall(string Text, InjectionMode Mode);

public class CapturingInputInjectorService : IInputInjectorService
{
    public List<InjectionCall> Calls { get; } = new();

    public void TypeText(string text) => Calls.Add(new(text, InjectionMode.TypeText));

    public void SimulatePaste() => Calls.Add(new("", InjectionMode.ClipboardPaste));

    public void SendKeys(string expansion) => Calls.Add(new(expansion, InjectionMode.SendKeys));

    public void ValidateExpansion(string expansion) { }

    public bool ContainsKeyTokens(string expansion) => expansion.Contains('{') && expansion.Contains('}');
}
