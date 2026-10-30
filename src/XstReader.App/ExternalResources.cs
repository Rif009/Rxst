using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace XstReader.App;

public interface IExternalResourceLoader
{
    Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken);
}

public sealed class ExternalResourceLoader : IExternalResourceLoader
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(6) };
    public async Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect < 4; redirect++)
        {
            if (uri.Scheme is not ("http" or "https")) return null;
            using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 4 * 1024 * 1024) return null;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (output.Length + count > 4 * 1024 * 1024) return null;
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        return null;
    }
}

public static class ExternalHtml
{
    // All network requests happen here. The renderer receives embedded bytes only.
    public static async Task<string> PrepareAsync(string html, MailRenderOptions options,
        Func<string, string?>? resolveCid = null, CancellationToken cancellationToken = default, IExternalResourceLoader? loader = null)
    {
        if (!options.EnableHtml || string.IsNullOrWhiteSpace(html)) return "";
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        if (!options.ShowInlineImages)
            foreach (var img in doc.DocumentNode.Descendants("img").ToArray())
            {
                var src = img.GetAttributeValue("src", "").Trim();
                if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    img.SetAttributeValue("src", "");
            }
        if (options.AllowExternalResources)
        {
            loader ??= new ExternalResourceLoader();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var cache = new Dictionary<Uri, byte[]?>();
            var budget = 16 * 1024 * 1024;
            var baseHref = doc.DocumentNode.SelectSingleNode("//base[@href]")?.GetAttributeValue("href", "");
            Uri? baseUri = RemoteUri(baseHref, null);
            foreach (var node in doc.DocumentNode.Descendants().Where(n => n.Name == "img" ||
                n.Name == "link" && n.GetAttributeValue("rel", "").Equals("stylesheet", StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested || budget <= 0) break;
                var uri = RemoteUri(WebUtility.HtmlDecode(node.GetAttributeValue(node.Name == "img" ? "src" : "href", "")), baseUri);
                if (uri == null) continue;
                if (!cache.TryGetValue(uri, out var bytes))
                {
                    if (cache.Count >= 20) break;
                    try { bytes = await loader.LoadAsync(uri, timeout.Token); }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { bytes = null; }
                    if (bytes?.Length > budget) bytes = null;
                    budget -= bytes?.Length ?? 0; cache[uri] = bytes;
                }
                if (bytes == null) continue;
                if (node.Name == "img")
                {
                    var data = MailHtml.ImageDataUri(bytes);
                    if (data != null) node.SetAttributeValue("src", data);
                }
                else
                {
                    var style = doc.CreateElement("style"); style.InnerHtml = Encoding.UTF8.GetString(bytes);
                    node.ParentNode.ReplaceChild(style, node);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return MailHtml.Prepare(doc.DocumentNode.OuterHtml, options.ShowInlineImages ? resolveCid : null, options.FontSize);
    }
    private static Uri? RemoteUri(string? value, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith("//")) value = "https:" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) && baseUri != null) Uri.TryCreate(baseUri, value, out uri);
        return uri?.Scheme is "http" or "https" ? uri : null;
    }
}
