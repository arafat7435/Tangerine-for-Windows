using System.IO.Compression;
using System.Xml.Linq;
using System.Net;
namespace Tangerine;
public sealed partial class Engine
{
    public static bool HasOffice(string progId) => Type.GetTypeFromProgID(progId) != null;
    static Task<T> Sta<T>(Func<T> action, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { ct.ThrowIfCancellationRequested(); var result = action(); ct.ThrowIfCancellationRequested(); tcs.TrySetResult(result); } catch (OperationCanceledException) { tcs.TrySetCanceled(ct); } catch (Exception ex) { tcs.TrySetException(ex); } }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return tcs.Task;
    }
    static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj); }
    internal static object CreateOffice(string prog)
    {
        var type = Type.GetTypeFromProgID(prog) ?? throw new InvalidOperationException($"This conversion requires Microsoft {prog.Split('.')[0]} installed on this PC.");
        for (int attempt = 0; ; attempt++)
        {
            try { return Activator.CreateInstance(type)!; }
            catch (COMException ex) when (attempt < 4 && (ex.HResult == unchecked((int)0x800706BE) || ex.HResult == unchecked((int)0x800706BA) || ex.HResult == unchecked((int)0x80080005))) { Thread.Sleep(250 * (attempt + 1)); }
        }
    }
    static int OpenCount(dynamic app, FileKind kind) => kind == FileKind.Presentation ? (int)app.Presentations.Count : kind == FileKind.Spreadsheet ? (int)app.Workbooks.Count : (int)app.Documents.Count;
    static void QuitIfEmpty(dynamic app, FileKind kind)
    {
        // PowerPoint can return a shared instance. Never quit a user's open presentations.
        if (OpenCount(app, kind) == 0) { if (kind is FileKind.Document or FileKind.Epub) app.Quit(false); else app.Quit(); }
    }
    static string ReadDocumentText(string input)
    {
        var ext = Catalog.Extension(input);
        if (ext == "docx") { using var zip = ZipFile.OpenRead(input); using var stream = zip.GetEntry("word/document.xml")!.Open(); var xml = XDocument.Load(stream); XNamespace ns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"; return string.Join("\n\n", xml.Descendants(ns + "p").Select(p => string.Concat(p.Descendants(ns + "t").Select(x => x.Value)))); }
        if (ext == "epub")
        {
            using var zip = ZipFile.OpenRead(input); var pages = new List<string>();
            var opf = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
            IEnumerable<ZipArchiveEntry> ordered = zip.Entries.Where(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".html"));
            if (opf != null) { using var opfStream = opf.Open(); var xml = XDocument.Load(opfStream); XNamespace ns = "http://www.idpf.org/2007/opf"; var manifest = xml.Descendants(ns + "item").Where(x => x.Attribute("id") != null).ToDictionary(x => x.Attribute("id")!.Value, x => x.Attribute("href")?.Value ?? ""); var baseUri = new Uri("https://epub.local/" + opf.FullName); ordered = xml.Descendants(ns + "itemref").Select(x => x.Attribute("idref")?.Value ?? "").Where(manifest.ContainsKey).Select(id => zip.GetEntry(Uri.UnescapeDataString(new Uri(baseUri, manifest[id]).AbsolutePath.TrimStart('/')))).Where(x => x != null).Cast<ZipArchiveEntry>().ToArray(); }
            foreach (var entry in ordered) { using var s = entry.Open(); using var reader = new StreamReader(s); pages.Add(HtmlText(reader.ReadToEnd())); }
            return string.Join("\n\n", pages);
        }
        var text = File.ReadAllText(input); return ext is "html" or "htm" ? HtmlText(text) : text;
    }
    static string HtmlText(string html)
    {
        html = Regex.Replace(html, @"<!--[\s\S]*?-->|<(head|script|style|template)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, @"</(p|div|h[1-6]|li|tr)>|<br\s*/?>", "\n", RegexOptions.IgnoreCase), "<[^>]+>", ""));
    }
    static string ResolveHtmlReferences(string html, string original)
    {
        var baseUri = new Uri(Path.GetFullPath(original));
        return Regex.Replace(html, "(?<prefix>\\b(?:src|href)\\s*=\\s*[\"'])(?<url>[^\"']+)(?<suffix>[\"'])", match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value); if (url.StartsWith('#') || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return match.Value;
            if (Uri.TryCreate(baseUri, url, out var resolved) && resolved.IsFile) return match.Groups["prefix"].Value + WebUtility.HtmlEncode(resolved.LocalPath) + match.Groups["suffix"].Value;
            return match.Value;
        }, RegexOptions.IgnoreCase);
    }
    static void EmbedHtmlAssets(string target, string? original = null)
    {
        original ??= target; var html = File.ReadAllText(target); var root = Path.GetFullPath(Path.GetDirectoryName(original)!) + Path.DirectorySeparatorChar;
        html = Regex.Replace(html, "(?<prefix>\\b(?:src|href)\\s*=\\s*[\"'])(?<url>[^\"']+)(?<suffix>[\"'])", match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (url.StartsWith('#')) return match.Value;
            if (!Uri.TryCreate(new Uri(Path.GetFullPath(original)), url, out var uri) || !uri.IsFile) return match.Value;
            var reference = match.Groups["prefix"].Value + WebUtility.HtmlEncode(uri.AbsoluteUri) + match.Groups["suffix"].Value;
            var path = Path.GetFullPath(uri.LocalPath); if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return reference;
            var mime = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".svg" => "image/svg+xml", ".bmp" => "image/bmp", ".css" => "text/css", _ => null };
            return mime == null ? reference : match.Groups["prefix"].Value + "data:" + mime + ";base64," + System.Convert.ToBase64String(File.ReadAllBytes(path)) + match.Groups["suffix"].Value;
        }, RegexOptions.IgnoreCase);
        File.WriteAllText(target, html, new UTF8Encoding(false));
    }
    public static void WriteEpub(string text, string target, string title)
    {
        text = CleanExportText(text);
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        void Entry(string name, string body, CompressionLevel level = CompressionLevel.Optimal) { using var writer = new StreamWriter(zip.CreateEntry(name, level).Open(), new UTF8Encoding(false)); writer.Write(body); }
        Entry("mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Entry("META-INF/container.xml", "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
        var name = WebUtility.HtmlEncode(title); var uid = Guid.NewGuid().ToString();
        Entry("OEBPS/content.opf", $"<?xml version=\"1.0\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"id\"><metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"id\">urn:uuid:{uid}</dc:identifier><dc:title>{name}</dc:title><dc:language>en</dc:language><meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta></metadata><manifest><item id=\"body\" href=\"body.xhtml\" media-type=\"application/xhtml+xml\"/><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/></manifest><spine><itemref idref=\"body\"/></spine></package>");
        Entry("OEBPS/body.xhtml", $"<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>{name}</title></head><body><h1>{name}</h1>" + string.Join("", text.Split('\n').Select(p => "<p>" + WebUtility.HtmlEncode(p) + "</p>")) + "</body></html>");
        Entry("OEBPS/nav.xhtml", $"<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>Contents</title></head><body><nav epub:type=\"toc\"><ol><li><a href=\"body.xhtml\">{name}</a></li></ol></nav></body></html>");
    }
    static string CleanExportText(string text)
    {
        // Word exposes cell/row markers as control characters. Preserve useful
        // separators while ensuring exported text is valid Unicode/XML.
        text = text.Replace("\r\a\r\a", "\n").Replace("\r\a", "\t").Replace('\a', '\t').Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', '\n').Replace("\f", "\n\n");
        return Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F\uFFFE\uFFFF]", "");
    }
    async Task<List<string>> DocumentConvert(string input, string target, string format, CancellationToken ct)
    {
        var kind = Catalog.Kind(input); var ext = Catalog.Extension(input);
        if (ext == "pages") throw new NotSupportedException("Apple Pages files need to be exported to DOCX on a Mac first.");
        if (format == "html" && ext is "html" or "htm")
        {
            File.Copy(input, target); EmbedHtmlAssets(target, input); return new() { target };
        }
        if (format == "html" && ext is "doc" or "rtf")
        {
            var docx = Path.Combine(Path.GetDirectoryName(target)!, "html-source.docx"); await OfficeConvert(input, docx, "docx", FileKind.Document, ct);
            File.WriteAllText(target, DocxHtml(docx), new UTF8Encoding(false)); return new() { target };
        }
        if (format == "html" && ext is "docx" or "txt" or "md" or "epub")
        {
            var html = ext == "docx" ? DocxHtml(input) : TextHtml(ReadDocumentText(input), ext == "md");
            File.WriteAllText(target, html, new UTF8Encoding(false)); return new() { target };
        }
        if (kind is FileKind.Document or FileKind.Epub && ext is "docx" or "md" or "txt" or "html" or "htm" or "epub")
        {
            if (format is "txt" or "md" or "epub")
            {
                var text = ReadDocumentText(input); if (format == "epub") WriteEpub(text, target, Path.GetFileNameWithoutExtension(input)); else File.WriteAllText(target, text, new UTF8Encoding(false)); return new() { target };
            }
            if (ext is "md" or "epub")
            {
                var html = Path.Combine(Path.GetDirectoryName(target)!, "source.html"); var text = ReadDocumentText(input);
                File.WriteAllText(html, "<html><head><meta charset=\"utf-8\"></head><body>" + string.Join("", text.Split('\n').Select(p => p.StartsWith("# ") ? "<h1>" + WebUtility.HtmlEncode(p[2..]) + "</h1>" : "<p>" + WebUtility.HtmlEncode(p) + "</p>")) + "</body></html>"); input = html;
            }
        }
        if (format is "png" or "jpg")
        {
            var pdf = Path.ChangeExtension(target, "pdf"); await OfficeConvert(input, pdf, "pdf", kind, ct); var folder = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + " " + format.ToUpperInvariant()); Directory.CreateDirectory(folder); PdfRaster.Render(pdf, folder, format, ct); return new() { folder };
        }
        return await OfficeConvert(input, target, format, kind, ct);
    }
    static async Task<List<string>> OfficeConvert(string input, string target, string format, FileKind kind, CancellationToken ct)
    {
        var result = await Sta(() =>
        {
            string prog = kind == FileKind.Spreadsheet ? "Excel.Application" : kind == FileKind.Presentation ? "PowerPoint.Application" : "Word.Application";
            var title = Path.GetFileNameWithoutExtension(input);
            var inputExtension = Catalog.Extension(input); int textOrigin = 2;
            // A staged copy also avoids closing an already-open user document with the same path.
            var copyExtension = kind == FileKind.Spreadsheet && inputExtension is "csv" or "tsv" ? ".txt" : Path.GetExtension(input);
            var copy = Path.Combine(Path.GetDirectoryName(target)!, "office-input-" + Guid.NewGuid().ToString("N") + copyExtension);
            if (Catalog.Extension(input) is "html" or "htm") File.WriteAllText(copy, ResolveHtmlReferences(File.ReadAllText(input), input), new UTF8Encoding(false));
            else if (Catalog.Extension(input) is "txt" or "csv" or "tsv") { if (StageOfficeText(input, copy, ct)) textOrigin = 65001; }
            else File.Copy(input, copy); input = copy;
            dynamic app = CreateOffice(prog); dynamic? document = null; int priorCount = OpenCount(app, kind); object security = app.AutomationSecurity; object? alerts = kind == FileKind.Presentation ? null : app.DisplayAlerts; object? updateLinks = kind == FileKind.Spreadsheet ? app.AskToUpdateLinks : null;
            try
            {
                app.AutomationSecurity = 3;
                if (kind == FileKind.Spreadsheet)
                {
                    if (priorCount == 0) app.Visible = false; app.DisplayAlerts = false; app.AskToUpdateLinks = false;
                    if (inputExtension is "csv" or "tsv")
                    {
                        app.Workbooks.OpenText(input, Origin: textOrigin, StartRow: 1, DataType: 1, TextQualifier: 1, ConsecutiveDelimiter: false, Tab: inputExtension == "tsv", Semicolon: false, Comma: inputExtension == "csv", Space: false, Other: false, Local: false);
                        document = app.ActiveWorkbook;
                    }
                    else document = app.Workbooks.Open(input, UpdateLinks: 0, ReadOnly: true, IgnoreReadOnlyRecommended: true);
                    if (format == "pdf") document.ExportAsFixedFormat(0, target);
                    else document.SaveAs(target, format switch { "xlsx" => 51, "ods" => 60, "csv" => 62, "tsv" => 42, _ => throw new NotSupportedException(format) });
                }
                else if (kind == FileKind.Presentation)
                {
                    document = app.Presentations.Open(input, -1, 0, 0);
                    if (format == "pdf") document.SaveAs(target, 32); else throw new NotSupportedException(format);
                }
                else
                {
                    if (priorCount == 0) app.Visible = false; app.DisplayAlerts = 0;
                    document = app.Documents.Open(input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                    if (Catalog.Extension(input) is "html" or "htm") EmbedWordPictures(document);
                    if (format == "pdf") document.ExportAsFixedFormat(target, 17);
                    else if (format == "epub") WriteEpub((string)document.Content.Text, target, title);
                    else if (format is "md" or "txt") File.WriteAllText(target, CleanExportText((string)document.Content.Text), new UTF8Encoding(false));
                    else { if (format == "html") document.WebOptions.Encoding = 65001; document.SaveAs2(target, format switch { "docx" => 16, "html" => 10, "txt" => 7, _ => throw new NotSupportedException(format) }); }
                }
                return new List<string> { target };
            }
            finally { try { if (document != null) { if (kind == FileKind.Presentation) document.Close(); else document.Close(false); } } finally { Release((object?)document); try { app.AutomationSecurity = security; if (alerts != null) app.DisplayAlerts = alerts; if (updateLinks != null) app.AskToUpdateLinks = updateLinks; QuitIfEmpty(app, kind); } finally { Release((object)app); } } }
        }, ct); if (format == "html") EmbedHtmlAssets(target); if (format == "tsv") File.WriteAllText(target, File.ReadAllText(target), new UTF8Encoding(true)); return result;
    }
    static bool StageOfficeText(string input, string copy, CancellationToken ct)
    {
        // Excel guesses the Windows code page for UTF-8 CSV/TSV without a BOM.
        // Normalize only the staged copy; retain legacy encodings if UTF-8 decoding fails.
        try
        {
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            using var writer = new StreamWriter(copy, false, new UTF8Encoding(true));
            var buffer = new char[16384]; int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) { ct.ThrowIfCancellationRequested(); writer.Write(buffer, 0, count); }
            return true;
        }
        catch (DecoderFallbackException) { File.Copy(input, copy, true); return false; }
    }
    static void EmbedWordPictures(dynamic document)
    {
        dynamic shapes = document.InlineShapes;
        try
        {
            for (int i = 1; i <= (int)shapes.Count; i++)
            {
                dynamic shape = shapes[i]; try { if ((int)shape.Type == 4) { dynamic link = shape.LinkFormat; try { link.SavePictureWithDocument = true; link.BreakLink(); } finally { Release((object)link); } } } finally { Release((object)shape); }
            }
        }
        finally { Release((object)shapes); }
    }
    static async Task<List<string>> PdfToOffice(string input, string target, string format, CancellationToken ct)
    {
        if (format == "xlsx")
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(input); var pages = pdf.GetPages().Select(p => p.Text).ToArray();
            if (pages.All(string.IsNullOrWhiteSpace)) throw new NotSupportedException("This PDF has no extractable text. Use PNG, JPG, DOCX or PPTX to keep its scanned pages. OCR is not available.");
            return await Sta(() => { dynamic app = CreateOffice("Excel.Application"); dynamic? book = null; object alerts = app.DisplayAlerts; try { if (OpenCount(app, FileKind.Spreadsheet) == 0) app.Visible = false; app.DisplayAlerts = false; book = app.Workbooks.Add(); for (int i = 0; i < pages.Length; i++) { dynamic sheet = i == 0 ? book.Worksheets[1] : book.Worksheets.Add(After: book.Worksheets[book.Worksheets.Count]); try { sheet.Name = "Page " + (i + 1); for (int offset = 0, row = 1; offset < pages[i].Length; offset += 32000, row++) sheet.Cells[row, 1] = pages[i].Substring(offset, Math.Min(32000, pages[i].Length - offset)); sheet.Columns[1].ColumnWidth = 100; sheet.Columns[1].WrapText = true; } finally { Release((object)sheet); } } book.SaveAs(target, 51); return new List<string> { target }; } finally { try { book?.Close(false); } finally { Release((object?)book); app.DisplayAlerts = alerts; QuitIfEmpty(app, FileKind.Spreadsheet); Release((object)app); } } }, ct);
        }
        var dir = Path.Combine(Path.GetDirectoryName(target)!, "slides"); Directory.CreateDirectory(dir); var images = PdfRaster.Render(input, dir, "png", ct, 144);
        return await Sta(() => { dynamic app = CreateOffice("PowerPoint.Application"); dynamic? deck = null; try { deck = app.Presentations.Add(0); for (int i = 0; i < images.Count; i++) { using var bitmap = new ImageMagick.MagickImage(images[i]); if (i == 0) { deck.PageSetup.SlideWidth = (float)bitmap.Width / 2; deck.PageSetup.SlideHeight = (float)bitmap.Height / 2; } dynamic slide = deck.Slides.Add(i + 1, 12); try { dynamic shape = slide.Shapes.AddPicture(images[i], 0, -1, 0f, 0f, (float)deck.PageSetup.SlideWidth, (float)deck.PageSetup.SlideHeight); Release((object)shape); } finally { Release((object)slide); } } deck.SaveAs(target, 24); return new List<string> { target }; } finally { try { deck?.Close(); } finally { Release((object?)deck); QuitIfEmpty(app, FileKind.Presentation); Release((object)app); } } }, ct);
    }
}
