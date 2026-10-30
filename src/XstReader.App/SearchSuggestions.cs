using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace XstReader.App;

public partial class MainWindow
{
    private bool completingSearch, suggestionsDismissed;
    private void InitializeSearchSuggestions()
    {
        SearchBox.TextChanged += (_, _) => QueueSearchSuggestions(true);
        SearchBox.GotFocus += (_, _) => QueueSearchSuggestions(true);
        SearchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
                QueueSearchSuggestions(false);
        };
        // Tunnel before TextBox consumes arrows and Tab for text/navigation.
        SearchBox.AddHandler(KeyDownEvent, SearchSuggestionsKeyDown, RoutingStrategies.Tunnel);
        Vm.Settings.PropertyChanged += (_, _) => QueueSearchSuggestions(false);
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowMailbox) && !Vm.ShowMailbox) SearchSuggestionsPopup.IsOpen = false;
        };
        Deactivated += (_, _) => SearchSuggestionsPopup.IsOpen = false;
    }
    private void QueueSearchSuggestions(bool resetDismissal)
    {
        if (completingSearch) return;
        if (resetDismissal) suggestionsDismissed = false;
        Dispatcher.UIThread.Post(UpdateSearchSuggestions, DispatcherPriority.Background);
    }
    private void UpdateSearchSuggestions()
    {
        if (completingSearch) return;
        if (!SearchBox.IsFocused || !Vm.ShowMailbox || suggestionsDismissed || SearchBox.SelectionStart != SearchBox.SelectionEnd)
        { SearchSuggestionsPopup.IsOpen = false; return; }
        var suggestions = SearchCompletionEngine.Suggest(SearchBox.Text ?? "", SearchBox.CaretIndex, Vm.Settings.SearchSnapshot());
        if (suggestions == null) { SearchSuggestionsPopup.IsOpen = false; return; }
        var previous = (SearchSuggestionsList.SelectedItem as SearchSuggestion)?.Operator;
        SearchSuggestionsList.ItemsSource = suggestions.Items;
        SearchSuggestionsList.SelectedIndex = Math.Max(0, suggestions.Items.ToList().FindIndex(s => s.Operator == previous));
        SearchSuggestionsPopup.IsOpen = true;
    }
    private void SearchSuggestionsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Space)
        { suggestionsDismissed = false; UpdateSearchSuggestions(); e.Handled = true; return; }
        if (SearchSuggestionsPopup.IsOpen)
        {
            if (e.Key is Key.Up or Key.Down)
            {
                var count = SearchSuggestionsList.ItemCount;
                SearchSuggestionsList.SelectedIndex = (SearchSuggestionsList.SelectedIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                SearchSuggestionsList.ScrollIntoView(SearchSuggestionsList.SelectedItem!);
                e.Handled = true; return;
            }
            if (e.Key is Key.Enter or Key.Tab && e.KeyModifiers == KeyModifiers.None)
            { InsertSearchSuggestion(); e.Handled = true; return; }
            if (e.Key == Key.Escape)
            { suggestionsDismissed = true; SearchSuggestionsPopup.IsOpen = false; e.Handled = true; return; }
        }
        else if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None)
        { suggestionsDismissed = false; UpdateSearchSuggestions(); e.Handled = SearchSuggestionsPopup.IsOpen; }
        if (e.Key == Key.Enter && !e.Handled) { Vm.CommitSearch(true); e.Handled = true; }
    }
    private void ChooseSearchSuggestion(object? sender, TappedEventArgs e)
    { InsertSearchSuggestion(); e.Handled = true; }
    private void InsertSearchSuggestion()
    {
        if (SearchSuggestionsList.SelectedItem is not SearchSuggestion selected) return;
        var completion = SearchCompletionEngine.Complete(SearchBox.Text ?? "", SearchBox.CaretIndex, selected.Operator, Vm.Settings.SearchSnapshot());
        if (completion == null) return;
        completingSearch = true;
        try
        {
            SearchSuggestionsPopup.IsOpen = false;
            Vm.Query = completion.Text; SearchBox.Text = completion.Text;
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.SelectionStart = SearchBox.SelectionEnd = completion.Caret;
            suggestionsDismissed = true;
        }
        finally { completingSearch = false; }
    }
}
