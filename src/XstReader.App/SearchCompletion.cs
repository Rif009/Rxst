namespace XstReader.App;

public sealed record SearchSuggestion(string Operator, string DescriptionKey, bool Enabled)
{
    public string Label => Operator + ":";
    public string Description => Strings.Current[DescriptionKey];
    public string Availability => Enabled ? "" : Strings.Current["SuggestionDisabled"];
    public bool IsDisabled => !Enabled;
}

public sealed record SearchCompletion(int Start, int Length, IReadOnlyList<SearchSuggestion> Items);
public sealed record CompletedSearch(string Text, int Caret);

public static class SearchCompletionEngine
{
    public static SearchCompletion? Suggest(string text, int caret, SearchOptions options)
    {
        if (caret < 0 || caret > text.Length) return null;
        var start = 0; var quoted = false;
        for (var i = 0; i < caret; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            else if (!quoted && char.IsWhiteSpace(text[i])) start = i + 1;
        }
        if (quoted) return null;
        var end = caret; var rightQuoted = false;
        while (end < text.Length)
        {
            if (text[end] == '"') rightQuoted = !rightQuoted;
            if (!rightQuoted && char.IsWhiteSpace(text[end])) break;
            end++;
        }
        var token = text[start..end];
        var colon = token.IndexOf(':');
        // Only complete the operator: never rewrite a value, quoted phrase or URL.
        if (colon >= 0 && caret > start + colon) return null;
        var prefix = text[start..caret];
        if (prefix.Any(c => !char.IsAsciiLetter(c))) return null;
        if (colon < 0 && token.Any(c => !char.IsAsciiLetter(c))) return null;
        SearchSuggestion[] operators =
        [
            new("from", "SuggestFrom", true), new("to", "SuggestTo", true),
            new("subject", "SuggestSubject", true), new("body", "SuggestBody", options.Body),
            new("attachment", "SuggestAttachment", options.AttachmentNames),
            new("content", "SuggestContent", options.FileContents),
            new("filename", "SuggestFilename", options.AttachmentNames)
        ];
        var items = operators.Where(s => s.Operator.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return items.Length == 0 ? null : new(start, colon >= 0 ? colon + 1 : end - start, items);
    }
    public static CompletedSearch? Complete(string text, int caret, string field, SearchOptions options)
    {
        var context = Suggest(text, caret, options);
        var suggestion = context?.Items.FirstOrDefault(s => s.Operator == field);
        if (context == null || suggestion == null) return null;
        return new(text.Remove(context.Start, context.Length).Insert(context.Start, suggestion.Label), context.Start + suggestion.Label.Length);
    }
}
