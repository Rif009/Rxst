using System.Text.Json;

namespace XstReader.App;

public sealed class SearchHistoryStore(string path)
{
    public IReadOnlyList<string> Load(int limit = 5)
    {
        try { return File.Exists(path) ? Normalize(JsonSerializer.Deserialize<List<string?>>(File.ReadAllText(path)) ?? [], limit) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public static string[] Normalize(IEnumerable<string?> queries, int limit = 5) => queries.Where(q => !string.IsNullOrWhiteSpace(q))
        .Select(q => q!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(Math.Clamp(limit, 0, 100)).ToArray();
    public void Save(IEnumerable<string> queries, int limit = 5)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Normalize(queries, limit), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
