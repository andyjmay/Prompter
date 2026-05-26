using System.Windows;
using Prompter.Models;

namespace Prompter.Views;

public partial class DictionaryEntryDialog : Window
{
    public DictionaryEntry? Result { get; private set; }

    public DictionaryEntryDialog(DictionaryEntry? entry = null)
    {
        InitializeComponent();

        if (entry != null)
        {
            Title = $"Edit Entry — {entry.Word}";
            WordTextBox.Text = entry.Word;
            AliasesListBox.ItemsSource = new List<string>(entry.Aliases ?? new List<string>());
        }
        else
        {
            Title = "Add Dictionary Entry";
            AliasesListBox.ItemsSource = new List<string>();
        }
    }

    private void AddAliasButton_Click(object sender, RoutedEventArgs e)
    {
        var alias = NewAliasTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(alias))
            return;

        var word = WordTextBox.Text.Trim();
        if (alias.Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("An alias cannot be identical to the canonical word.", "Invalid Alias", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var items = (List<string>)AliasesListBox.ItemsSource;
        if (items.Any(a => a.Equals(alias, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("This alias already exists.", "Duplicate Alias", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        items.Add(alias);
        AliasesListBox.ItemsSource = null;
        AliasesListBox.ItemsSource = items;
        NewAliasTextBox.Clear();
    }

    private void RemoveAliasButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = AliasesListBox.SelectedItem as string;
        if (selected == null) return;

        var items = (List<string>)AliasesListBox.ItemsSource;
        items.Remove(selected);
        AliasesListBox.ItemsSource = null;
        AliasesListBox.ItemsSource = items;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var word = WordTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(word))
        {
            MessageBox.Show("Please enter a canonical word.", "Invalid Word", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var aliases = (List<string>)AliasesListBox.ItemsSource;
        if (aliases.Count == 0)
        {
            MessageBox.Show("Please add at least one alias.", "No Aliases", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new DictionaryEntry
        {
            Word = word,
            Aliases = aliases.ToList()
        };

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
