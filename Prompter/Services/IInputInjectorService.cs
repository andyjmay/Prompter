namespace Prompter.Services;

public interface IInputInjectorService
{
    void TypeText(string text);
    void SimulatePaste();
    void SendKeys(string expansion);
    void ValidateExpansion(string expansion);
    bool ContainsKeyTokens(string expansion);
}
