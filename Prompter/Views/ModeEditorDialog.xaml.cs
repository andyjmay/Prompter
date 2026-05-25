using System.Windows;
using Prompter.Models;

namespace Prompter.Views;

public partial class ModeEditorDialog : Window
{
    public ModeConfig? Result { get; private set; }

    private readonly bool _isBuiltIn;
    private readonly string? _existingId;

    public ModeEditorDialog(ModeConfig? mode = null)
    {
        InitializeComponent();

        if (mode != null)
        {
            _isBuiltIn = mode.IsBuiltIn;
            _existingId = mode.Id;
            Title = $"Edit Mode — {mode.Name}";
            NameTextBox.Text = mode.Name;
            NameTextBox.IsReadOnly = _isBuiltIn;
            PromptTextBox.Text = mode.SystemPrompt;
            SkipFormattingCheckBox.IsChecked = mode.SkipFormatting;
            ShowDiagnosticCheckBox.IsChecked = mode.ShowDiagnosticOutput;
        }
        else
        {
            Title = "Create New Mode";
            _isBuiltIn = false;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a mode name.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var prompt = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show("Please enter a system prompt.", "Invalid Prompt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var id = _existingId ?? GenerateIdFromName(name);

        Result = new ModeConfig
        {
            Id = id,
            Name = name,
            SystemPrompt = prompt,
            SkipFormatting = SkipFormattingCheckBox.IsChecked == true,
            ShowDiagnosticOutput = ShowDiagnosticCheckBox.IsChecked == true,
            IsBuiltIn = _isBuiltIn
        };

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string GenerateIdFromName(string name)
    {
        var id = string.Concat(name.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        if (string.IsNullOrWhiteSpace(id) || !id.Any(char.IsLetterOrDigit))
        {
            id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        }
        return id;
    }
}
