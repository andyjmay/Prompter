namespace Prompter.Services;

public interface IInputInjectorService
{
    void TypeText(string text);
    void SimulatePaste();
}
