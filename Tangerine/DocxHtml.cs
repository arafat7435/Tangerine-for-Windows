using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
namespace Tangerine;
public sealed partial class Engine
{
    static string TextHtml(string text, bool markdown = false) => HtmlPage(string.Join("\n", text.Split('\n').Select(line =>
    {
        int level = markdown ? line.TakeWhile(c => c == '#').Count() : 0;
        var tag = level is > 0 and <= 6 && line.Length > level && line[level] == ' ' ? "h" + level : "p";
        return $"<{tag}>" + WebUtility.HtmlEncode(tag == "p" ? line : line[(level + 1)..]) + $"</{tag}>";
    })));
    static string HtmlPage(string body) => "<!doctype html><html><head><meta charset=\"utf-8\"><style>body{font-family:Calibri,Arial,sans-serif;line-height:1.45}img{max-width:100%;height:auto}table{border-collapse:collapse}td{border:1px solid #ccc;padding:.4em}p{white-space:pre-wrap}</style></head><body>" + body + "</body></html>";
    // Native DOCX HTML export avoids Word's modal security notice and keeps
    // embedded images in the final file after the conversion staging is removed.
    static string DocxHtml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Xml(string name) { using var stream = (zip.GetEntry(name) ?? throw new InvalidDataException("Missing DOCX part: " + name)).Open(); return XDocument.Load(stream); }
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main", r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main", v = "urn:schemas-microsoft-com:vml";
        var relations = zip.GetEntry("word/_rels/document.xml.rels") == null ? new Dictionary<string, XElement>() : Xml("word/_rels/document.xml.rels").Root!.Elements().ToDictionary(e => (string)e.Attribute("Id")!, e => e);
        string? Target(string? id, bool external = false) { if (id == null || !relations.TryGetValue(id, out var relation)) return null; if (((string?)relation.Attribute("TargetMode") == "External") != external) return null; return (string?)relation.Attribute("Target"); }
        string Picture(string? id)
        {
            var target = Target(id); if (target == null) return "";
            var part = Uri.UnescapeDataString(new Uri(new Uri("https://docx.local/word/document.xml"), target).AbsolutePath.TrimStart('/'));
            var entry = zip.GetEntry(part); if (entry == null) return "";
            using var stream = entry.Open(); using var memory = new MemoryStream(); stream.CopyTo(memory);
            var mime = Path.GetExtension(part).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".svg" => "image/svg+xml", ".bmp" => "image/bmp", _ => null };
            if (mime == null) { using var img = new ImageMagick.MagickImage(memory.ToArray()); return "<img alt=\"\" src=\"data:image/png;base64," + System.Convert.ToBase64String(img.ToByteArray(ImageMagick.MagickFormat.Png)) + "\">"; }
            return "<img alt=\"\" src=\"data:" + mime + ";base64," + System.Convert.ToBase64String(memory.ToArray()) + "\">";
        }
        string Render(XElement e)
        {
            if (e.Name == w + "t") return WebUtility.HtmlEncode(e.Value);
            if (e.Name == w + "tab") return "&#9;";
            if (e.Name == w + "br" || e.Name == w + "cr") return "<br>";
            if (e.Name == a + "blip") return Picture((string?)e.Attribute(r + "embed"));
            if (e.Name == v + "imagedata") return Picture((string?)e.Attribute(r + "id"));
            if (e.Name.LocalName.EndsWith("Pr") || e.Name == w + "instrText" || e.Name == w + "del") return "";
            // AlternateContent contains two representations of one drawing.
            if (e.Name.LocalName == "AlternateContent") return string.Concat((e.Elements().FirstOrDefault() ?? e).Elements().Select(Render));
            var body = string.Concat(e.Elements().Select(Render));
            if (e.Name == w + "r")
            {
                var props = e.Element(w + "rPr");
                foreach (var (key, tag) in new[] { ("b", "strong"), ("i", "em"), ("u", "u"), ("strike", "s") }) { var property = props?.Element(w + key); if (property != null && (string?)property.Attribute(w + "val") is not ("0" or "false" or "none")) body = $"<{tag}>{body}</{tag}>"; }
                return body;
            }
            if (e.Name == w + "p")
            {
                var style = (string?)e.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val") ?? "";
                var heading = Regex.Match(style, "^Heading([1-6])$", RegexOptions.IgnoreCase); var tag = heading.Success ? "h" + heading.Groups[1].Value : "p";
                return $"<{tag}>{body}</{tag}>\n";
            }
            if (e.Name == w + "tbl") return "<table>" + body + "</table>";
            if (e.Name == w + "tr") return "<tr>" + body + "</tr>";
            if (e.Name == w + "tc") return "<td>" + body + "</td>";
            if (e.Name == w + "hyperlink")
            {
                var link = Target((string?)e.Attribute(r + "id"), true);
                if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto") return "<a href=\"" + WebUtility.HtmlEncode(link) + "\">" + body + "</a>";
            }
            return body;
        }
        return HtmlPage(Render(Xml("word/document.xml").Root!));
    }
}
