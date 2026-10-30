using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace XstReader.App;

public static class MailHtml
{
    public static string Prepare(string? html, Func<string, string?>? resolveCid = null, int fontSize = 14)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        foreach (var node in (doc.DocumentNode.SelectNodes("//script|//iframe|//object|//embed|//form|//input|//button|//meta|//base|//link|//svg|//video|//audio|//source") ?? Enumerable.Empty<HtmlNode>()).ToArray()) node.Remove();
        foreach (var node in doc.DocumentNode.Descendants().Where(n => n.NodeType == HtmlNodeType.Element).ToArray())
        {
            foreach (var attr in node.Attributes.ToArray())
            {
                if (attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                    attr.Name is "srcset" or "background" or "poster" or "action" or "formaction" or "srcdoc") node.Attributes.Remove(attr);
                else if (attr.Name == "style") attr.Value = CleanCss(attr.Value);
            }
            if (node.Name == "style") node.InnerHtml = CleanCss(node.InnerHtml);
            // HtmlRenderer clips TH baselines in some email tables. A styled TD
            // renders the same header reliably without altering the archived HTML.
            if (node.Name == "th")
            {
                node.Name = "td";
                node.SetAttributeValue("style", node.GetAttributeValue("style", "") + ";font-weight:bold;");
            }
            if (node.Name == "a")
            {
                var link = WebUtility.HtmlDecode(node.GetAttributeValue("href", "")).Trim();
                if (!IsSafeLink(link)) node.Attributes.Remove("href");
            }
            if (node.Name != "img") { node.Attributes.Remove("src"); continue; }
            var src = WebUtility.HtmlDecode(node.GetAttributeValue("src", "")).Trim();
            string? safe = null;
            if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) safe = resolveCid?.Invoke(Uri.UnescapeDataString(src[4..]));
            else if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && src.Length < 24 * 1024 * 1024)
            {
                var comma = src.IndexOf(',');
                if (comma > 0 && src[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                {
                    try { safe = ImageDataUri(Convert.FromBase64String(src[(comma + 1)..])); }
                    catch (FormatException) { }
                }
            }
            if (safe != null) node.SetAttributeValue("src", safe);
            else
            {
                var replacement = doc.CreateElement("span");
                replacement.SetAttributeValue("style", "color:#777777;font-size:12px");
                replacement.InnerHtml = "[" + WebUtility.HtmlEncode(node.GetAttributeValue("alt", Strings.Current["ExternalImage"])) + "]";
                node.ParentNode?.ReplaceChild(replacement, node);
            }
        }
        // A white canvas preserves the original email's text and table colours in either app theme.
        var content = doc.DocumentNode.SelectSingleNode("//body")?.InnerHtml ?? doc.DocumentNode.InnerHtml;
        var styles = string.Join("\n", (doc.DocumentNode.SelectNodes("//head/style") ?? Enumerable.Empty<HtmlNode>()).Select(s => s.OuterHtml));
        return "<html><head><style>body{font-family:Arial;font-size:" + Math.Clamp(fontSize, 12, 22) + "px;color:#24212c;background-color:#ffffff;}table{border-collapse:collapse;}a{color:#7353b0;}img{max-width:100%;}</style>" + styles + "</head><body>" + content + "</body></html>";
    }
    public static bool IsSafeLink(string href) => href.StartsWith('#') ||
        (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto");
    private static string CleanCss(string css)
    {
        // CSS escape sequences can obscure URLs, so escaped rules are excluded.
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        if (css.Contains('\\')) return "";
        css = Regex.Replace(css, @"@import[^;]*(;|$)", "", RegexOptions.IgnoreCase);
        css = Regex.Replace(css, @"url\s*\([^)]*\)", "none", RegexOptions.IgnoreCase);
        return Regex.Replace(css, @"(?:expression|behavior|-moz-binding)\s*[:(][^;}]*(;|$)", "", RegexOptions.IgnoreCase);
    }
    public static string? ImageDataUri(byte[] bytes)
    {
        var mime = bytes.AsSpan() switch
        {
            var b when b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            var b when b.StartsWith(new byte[] { 255, 216, 255 }) => "image/jpeg",
            var b when b.StartsWith("GIF8"u8) => "image/gif",
            var b when b.StartsWith("BM"u8) => "image/bmp",
            _ => null
        };
        return mime == null ? null : $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
