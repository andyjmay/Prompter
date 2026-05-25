using System.Windows;
using Prompter.Models;

namespace Prompter.Views;

public partial class SnippetDialog : Window
{
    public Snippet? Result { get; private set; }
    private readonly Snippet? _originalSnippet;
    private readonly List<Snippet> _existingSnippets;

    public SnippetDialog(List<Snippet> existingSnippets, Snippet? snippet = null)
    {
        InitializeComponent();
        _existingSnippets = existingSnippets;

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
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
