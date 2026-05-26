using System.Windows;
using Prompter.Models;
using Prompter.Services;

namespace Prompter.Views;

public partial class SnippetDialog : Window
{
    public Snippet? Result { get; private set; }
    private readonly Snippet? _originalSnippet;
    private readonly List<Snippet> _existingSnippets;
    private readonly IInputInjectorService _injector;

    private static readonly List<(string Label, string Token)> KeyTokens = new()
    {
        ("Enter Key ↵", "{Enter}"),
        ("Tab Key ⇥", "{Tab}"),
        ("Escape", "{Escape}"),
        ("Backspace", "{Backspace}"),
        ("Delete", "{Delete}"),
        ("Arrow Up", "{Up}"),
        ("Arrow Down", "{Down}"),
        ("Arrow Left", "{Left}"),
        ("Arrow Right", "{Right}"),
        ("Home", "{Home}"),
        ("End", "{End}"),
        ("Page Up", "{PageUp}"),
        ("Page Down", "{PageDown}"),
        ("Ctrl+A (Select All)", "{Ctrl+A}"),
        ("Ctrl+C (Copy)", "{Ctrl+C}"),
        ("Ctrl+V (Paste)", "{Ctrl+V}")
    };

    public SnippetDialog(IInputInjectorService injector, List<Snippet> existingSnippets, Snippet? snippet = null)
    {
        InitializeComponent();
        _injector = injector;
        _existingSnippets = existingSnippets;

        foreach (var (label, _) in KeyTokens)
        {
            KeyComboBox.Items.Add(label);
        }
        KeyComboBox.SelectedIndex = 0;

        if (snippet != null)
        {
            Title = $"Edit Snippet — {snippet.Trigger}";
            TriggerTextBox.Text = snippet.Trigger;
            ExpansionTextBox.Text = snippet.Expansion;
            _originalSnippet = snippet;
        }
        else
        {
            Title = "Add Snippet";
        }
    }

    private void InsertKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (KeyComboBox.SelectedIndex < 0 || KeyComboBox.SelectedIndex >= KeyTokens.Count)
            return;

        var token = KeyTokens[KeyComboBox.SelectedIndex].Token;
        int selectionStart = ExpansionTextBox.SelectionStart;
        string text = ExpansionTextBox.Text;
        string newText = text.Substring(0, selectionStart) + token + text.Substring(selectionStart + ExpansionTextBox.SelectionLength);
        ExpansionTextBox.Text = newText;
        ExpansionTextBox.SelectionStart = selectionStart + token.Length;
        ExpansionTextBox.SelectionLength = 0;
        ExpansionTextBox.Focus();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var trigger = TriggerTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(trigger))
        {
            MessageBox.Show("Please enter a trigger phrase.", "Invalid Trigger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var expansion = ExpansionTextBox.Text;
        if (string.IsNullOrWhiteSpace(expansion))
        {
            MessageBox.Show("Please enter expansion text.", "Invalid Expansion", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_injector.ContainsKeyTokens(expansion))
        {
            try
            {
                _injector.ValidateExpansion(expansion);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Invalid Expansion", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Enforce uniqueness (case-insensitive), excluding the original snippet when editing
        var duplicate = _existingSnippets.FirstOrDefault(
            s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)
                 && !ReferenceEquals(s, _originalSnippet));

        if (duplicate != null)
        {
            MessageBox.Show(
                $"A snippet with the trigger '{trigger}' already exists.",
                "Duplicate Trigger",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = new Snippet
        {
            Trigger = trigger,
            Expansion = expansion
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
