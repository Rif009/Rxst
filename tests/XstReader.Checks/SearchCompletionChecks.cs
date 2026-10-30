using XstReader.App;

static class SearchCompletionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var options = new SearchOptions();
        var all = SearchCompletionEngine.Suggest("", 0, options)!;
        check(all.Items.Select(s => s.Operator).Order().SequenceEqual(new[] { "attachment", "body", "content", "filename", "from", "subject", "to" }), "Search completion offers every supported query operator");
        check(SearchCompletionEngine.Suggest("co", 2, options)!.Items.Single().Operator == "content" &&
            SearchCompletionEngine.Suggest("ATT", 3, options)!.Items.Single().Operator == "attachment", "Operator suggestions narrow by prefix, ignoring case");
        check(!all.Items.Single(s => s.Operator == "content").Enabled &&
            SearchCompletionEngine.Suggest("co", 2, options with { FileContents = true })!.Items.Single().Enabled,
            "Content suggestions explain disabled fields and reflect current search settings");
        check(SearchCompletionEngine.Complete("bo", 2, "body", options) == new CompletedSearch("body:", 5), "Accepting a suggestion inserts a colon and positions the caret for a value");
        var query = "from:Анна bo subject:\"план проекта\"";
        var caret = query.IndexOf("bo", StringComparison.Ordinal) + 2;
        var completed = SearchCompletionEngine.Complete(query, caret, "body", options)!;
        check(completed.Text == "from:Анна body: subject:\"план проекта\"" && completed.Caret == query.IndexOf("bo", StringComparison.Ordinal) + 5,
            "Completing an operator in the middle preserves earlier filters and following quoted phrases");
        check(SearchCompletionEngine.Complete("bo:\"текст письма\"", 2, "body", options) == new CompletedSearch("body:\"текст письма\"", 5),
            "Completing an existing operator preserves its colon and quoted value");
        check(SearchCompletionEngine.Complete("bodXYZ", 3, "body", options) == new CompletedSearch("body:", 5),
            "Completion replaces the entire unfinished operator, including text after the caret");
        check(SearchCompletionEngine.Suggest("body:co", 7, options) == null &&
            SearchCompletionEngine.Suggest("attachment:bo", 13, options) == null &&
            SearchCompletionEngine.Suggest("subject:\"from body\"", 17, options) == null &&
            SearchCompletionEngine.Suggest("\"bo", 3, options) == null,
            "Operator suggestions do not interrupt values or quoted search phrases");
        check(SearchCompletionEngine.Suggest("https://example.com", 8, options) == null &&
            SearchCompletionEngine.Suggest("договор", 7, options) == null &&
            SearchCompletionEngine.Suggest("unmatched", 9, options) == null,
            "URLs, ordinary Cyrillic words and unknown prefixes leave the search text untouched");
        check(SearchCompletionEngine.Suggest("from:Анна ", 10, options)!.Items.Count == 7,
            "Starting a new term offers fields without replacing completed terms");
        check(SearchCompletionEngine.Complete("body:contract", 13, "body", options) == null,
            "An outdated suggestion cannot overwrite a value typed after the popup appeared");
        var language = Strings.Current.English ? 1 : 0;
        try
        {
            Strings.Current.SetLanguage(0); var ru = all.Items.Single(s => s.Operator == "content").Description;
            Strings.Current.SetLanguage(1); var en = all.Items.Single(s => s.Operator == "content").Description;
            check(ru.Contains("Текст внутри файлов") && en.Contains("Text inside attached files") && ru != en,
                "Suggestion descriptions and examples are localized in Russian and English");
        }
        finally { Strings.Current.SetLanguage(language); }
    }
}
