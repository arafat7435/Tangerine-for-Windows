using ImageMagick;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
namespace Tangerine;

public static class ConversionMatrixTest
{
    const string Marker = "Conversion marker 42";
    const string UnicodeText = "café नमस्ते";
    record Source(string Path, string Extension, string Scenario = "standard", int Pages = 1, bool HasText = true, bool HasAudio = true);
    record Result(string phase, string category, string input, string target, string scenario, bool passed, long conversionMs, long validationMs, string[] outputs, string? error);
    public static async Task Run(App app, string directory, string? only = null)
    {
        Directory.CreateDirectory(directory); var inputs = Path.Combine(directory, "inputs with spaces café"); var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(inputs); Directory.CreateDirectory(outputs);
        File.WriteAllText(Path.Combine(directory, "execution.json"), JsonSerializer.Serialize(new
        {
            StartedAt = DateTimeOffset.Now,
            Executable = Environment.ProcessPath,
            ProcessId = Environment.ProcessId,
            ApplicationDllSHA256 = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
            Filter = only,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        }, new JsonSerializerOptions { WriteIndented = true }));
        var engine = new Engine(new AppSettings { OutputDirectory = outputs, EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory });
        var sources = new List<Source>(); var results = new List<Result>();
        string Name(string extension, string prefix = "sample ' matrix") => Path.Combine(inputs, prefix + "." + extension);
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        void Save() { File.WriteAllText(Path.Combine(directory, "matrix-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
        Task<List<string>> Convert(string path, string format) => engine.Run(new[] { path }, new(format, format, "Convert to " + format), WheelMode.Convert, new(), CancellationToken.None);
        async Task Prepare(string extension, Func<Task<string>> action, int pages = 1) { try { var path = await action(); sources.Add(new(path, extension, Pages: pages)); results.Add(new("fixture", Catalog.Kind("x." + extension).ToString(), extension, "", "standard", true, 0, 0, new[] { path }, null)); } catch (Exception ex) { sources.Add(new(Name(extension), extension, Pages: pages)); results.Add(new("fixture", Catalog.Kind("x." + extension).ToString(), extension, "", "standard", false, 0, 0, Array.Empty<string>(), ex.ToString())); } Save(); }
        void AddFile(string extension, string content) { var path = Name(extension); File.WriteAllText(path, content, new UTF8Encoding(true)); sources.Add(new(path, extension)); }
        void CopyAlias(string extension, string original) { var path = Name(extension); File.Copy(sources.First(x => x.Extension == original).Path, path); sources.Add(new(path, extension)); }
        async Task<JsonDocument> Probe(string path) => JsonDocument.Parse(await Engine.Process(engine.FindTool("ffprobe")!, new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path }, CancellationToken.None));
        string TextPart(string path, string part) { using var zip = ZipFile.OpenRead(path); using var stream = (zip.GetEntry(part) ?? throw new InvalidDataException("Missing " + part)).Open(); return XDocument.Load(stream).Root!.Value; }
        void HasMarker(string text) { Require(Regex.Replace(text, @"\s+", "").Contains(Regex.Replace(Marker, @"\s+", "")), "Conversion lost the text marker"); }
        void HasUnicode(string text) { text = System.Net.WebUtility.HtmlDecode(text); Require(text.Contains("café") && text.Contains("नमस्ते"), "French or Hindi text was corrupted"); }
        void CheckSheetCells(string path, string format)
        {
            var rows = new List<string[]>();
            if (format is "csv" or "tsv")
            {
                using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8, true) { HasFieldsEnclosedInQuotes = true };
                parser.SetDelimiters(format == "csv" ? "," : "\t"); while (!parser.EndOfData && rows.Count < 4) rows.Add(parser.ReadFields()!);
            }
            else
            {
                using var archive = ZipFile.OpenRead(path);
                XDocument Part(string name) { using var stream = archive.GetEntry(name)!.Open(); return XDocument.Load(stream); }
                if (format == "xlsx")
                {
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    var strings = archive.GetEntry("xl/sharedStrings.xml") == null ? Array.Empty<string>() : Part("xl/sharedStrings.xml").Descendants(ns + "si").Select(x => x.Value).ToArray();
                    var cells = Part("xl/worksheets/sheet1.xml").Descendants(ns + "c").ToDictionary(x => x.Attribute("r")!.Value, x => x.Attribute("t")?.Value switch { "s" => strings[int.Parse(x.Element(ns + "v")!.Value)], "inlineStr" => x.Element(ns + "is")!.Value, _ => x.Element(ns + "v")?.Value ?? "" });
                    for (int row = 1; row <= 4; row++) rows.Add(new[] { cells.GetValueOrDefault("A" + row, ""), cells.GetValueOrDefault("B" + row, "") });
                }
                else
                {
                    XNamespace table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0", text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
                    rows = Part("content.xml").Descendants(table + "table").First().Elements(table + "table-row").Take(4).Select(row => row.Elements(table + "table-cell").Take(2).Select(cell => string.Concat(cell.Elements(text + "p").Select(p => p.Value))).ToArray()).ToList();
                }
            }
            Require(rows.Count == 4 && rows[0].Take(2).SequenceEqual(new[] { Marker, UnicodeText }) && rows[3].Take(2).SequenceEqual(new[] { "Total", "49" }), "Spreadsheet columns or cell values changed");
        }
        void CheckPicture(string path, bool exact) { using var image = new MagickImage(path); if (exact) Require(image.Width == 160 && image.Height == 120, "Image dimensions changed"); Require(image.Width > 0 && image.Height > 0, "Empty image"); image.Resize(32, 32); Require(image.TotalColors > 1, "Rendered image is blank"); }
        Dictionary<string, string> Contents(string path) { var root = Path.Combine(directory, "archive-check-" + Guid.NewGuid().ToString("N")); try { Engine.ExtractArchive(path, root, CancellationToken.None); return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p).Replace('\\', '/'), p => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); } }

        // Include every supported input spelling, rather than sampling category defaults.
        var png = Name("png"); using (var image = new MagickImage("gradient:#FF7700-#1122EE", 160, 120)) image.Write(png); sources.Add(new(png, "png"));
        foreach (var format in new[] { "jpg", "webp", "heic", "tiff", "avif", "bmp" }) await Prepare(format, async () => { var path = Name(format); File.Copy((await Convert(png, format))[0], path); return path; });
        AddFile("svg", "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='120'><rect width='160' height='120' fill='#ff7700'/><circle cx='80' cy='60' r='30' fill='#1122ee'/></svg>");
        foreach (var (alias, original) in new[] { ("jpeg", "jpg"), ("tif", "tiff"), ("heif", "heic") }) CopyAlias(alias, original);
        var audio = Name("wav"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", audio }, CancellationToken.None); sources.Add(new(audio, "wav"));
        foreach (var format in new[] { "mp3", "m4a", "flac", "ogg", "opus", "aiff", "wma" }) await Prepare(format, async () => { var path = Name(format); File.Copy((await Convert(audio, format))[0], path); return path; }); CopyAlias("aif", "aiff");
        var video = Name("mp4"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=20:duration=2", "-i", audio, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video }, CancellationToken.None); sources.Add(new(video, "mp4"));
        foreach (var format in new[] { "mov", "mkv", "webm", "avi", "wmv", "gif" }) await Prepare(format, async () => { var path = Name(format); File.Copy((await Convert(video, format))[0], path); return path; });
        AddFile("txt", Marker + "\n" + UnicodeText + "\nOranges 42\n"); AddFile("md", "# " + Marker + "\n\n" + UnicodeText + "\n");
        AddFile("html", "<!doctype html><html><head><meta charset='utf-8'><style>/* STYLE_CANARY */ p { color:black }</style></head><body><h1>" + Marker + "</h1><p>" + UnicodeText + "</p><img src=\"" + System.Net.WebUtility.HtmlEncode(Path.GetFileName(png)) + "\" width='160' height='120'></body></html>"); CopyAlias("htm", "html");
        foreach (var format in new[] { "docx", "doc", "rtf" }) await Prepare(format, async () => { var path = Name(format); await ConversionFixtures.Word(path, Marker + "\r" + UnicodeText + "\r", png); return path; });
        await Prepare("pdf", async () => { var path = Name("pdf"); File.Copy((await Convert(sources.First(x => x.Extension == "docx").Path, "pdf"))[0], path); return path; });
        await Prepare("epub", () => { var path = Name("epub"); Engine.WriteEpub(Marker + "\n" + UnicodeText, path, "Matrix"); return Task.FromResult(path); });
        foreach (var format in new[] { "xlsx", "xls" }) await Prepare(format, async () => { var path = Name(format); await ConversionFixtures.Sheet(path, Marker, UnicodeText); return path; });
        AddFile("csv", Marker + "," + UnicodeText + "\nOranges,42\nApples,7\nTotal,49\n"); AddFile("tsv", Marker + "\t" + UnicodeText + "\nOranges\t42\nApples\t7\nTotal\t49\n");
        await Prepare("ods", async () => { var path = Name("ods"); File.Copy((await Convert(sources.First(x => x.Extension == "xlsx").Path, "ods"))[0], path); return path; });
        foreach (var format in new[] { "pptx", "ppt" }) await Prepare(format, async () => { var path = Name(format); await ConversionFixtures.Slides(path, Marker, UnicodeText); return path; }, 2);
        AddFile("srt", "1\n00:00:00,200 --> 00:00:00,800\n" + Marker + "\n\n2\n00:00:01,000 --> 00:00:01,700\n" + UnicodeText + "\n");
        AddFile("vtt", "WEBVTT\n\n00:00.200 --> 00:00.800\n" + Marker + "\n\n00:01.000 --> 00:01.700\n" + UnicodeText + "\n");
        var zip = Name("zip"); using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { foreach (var name in new[] { "nested/hello.txt", "café/data.txt" }) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8); writer.Write(Marker + " " + UnicodeText); } }
        sources.Add(new(zip, "zip"));
        foreach (var format in new[] { "tar", "gz" }) await Prepare(format, async () => { var path = Name(format); File.Copy((await Convert(zip, format))[0], path); return path; }); CopyAlias("tgz", "gz");
        var rar = Environment.GetEnvironmentVariable("TANGERINE_RAR_FIXTURE"); await Prepare("rar", () => { var path = Name("rar"); File.Copy(rar ?? throw new FileNotFoundException("Set TANGERINE_RAR_FIXTURE to test all archive conversions."), path); return Task.FromResult(path); });

        var imagePdf = Name("pdf", "image-only"); Engine.ImagesToPdf(new[] { png, png }, imagePdf); sources.Add(new(imagePdf, "pdf", "image-only", 2, false));
        var multipage = Name("tiff", "multipage"); using (var images = new MagickImageCollection()) { images.Add(new MagickImage(png)); images.Add(new MagickImage(png)); images.Write(multipage); }
        sources.Add(new(multipage, "tiff", "multipage", 2));
        var silent = Name("mp4", "silent"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", video, "-an", "-c:v", "copy", silent }, CancellationToken.None); sources.Add(new(silent, "mp4", "silent", HasAudio: false));
        var gzip = Name("gz", "plain payload.txt"); using (var file = File.Create(gzip)) using (var stream = new GZipStream(file, CompressionMode.Compress)) stream.Write(Encoding.UTF8.GetBytes(Marker)); sources.Add(new(gzip, "gz", "single-file"));
        foreach (var extension in new[] { "txt", "csv", "tsv" }) { var path = Name(extension, "utf8 no-bom"); File.WriteAllText(path, File.ReadAllText(sources.First(s => s.Extension == extension).Path), new UTF8Encoding(false)); sources.Add(new(path, extension, "utf8-no-bom")); }
        var sourceHashes = sources.Where(x => File.Exists(x.Path)).ToDictionary(x => x.Path, x => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Path))));
        var filters = only?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var planned = sources.SelectMany(s => Catalog.Conversions(s.Path).Select(t => new { s, t })).Where(x => filters == null || filters.Any(filter => Catalog.Kind(x.s.Path).ToString().Equals(filter, StringComparison.OrdinalIgnoreCase) || x.s.Extension.Equals(filter, StringComparison.OrdinalIgnoreCase) || x.s.Scenario.Equals(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
        Require(planned.Length > 0, "No conversions matched the requested filter.");
        File.WriteAllText(Path.Combine(directory, "matrix-plan.json"), JsonSerializer.Serialize(planned.Select(x => new { category = Catalog.Kind(x.s.Path).ToString(), input = x.s.Extension, target = x.t.Id, scenario = x.s.Scenario }), new JsonSerializerOptions { WriteIndented = true }));
        foreach (var pair in planned)
        {
            var source = pair.s; var target = pair.t.Id; var kind = Catalog.Kind(source.Path); string[] produced = Array.Empty<string>(); long conversionMs = 0; var clock = Stopwatch.StartNew();
            File.WriteAllText(Path.Combine(directory, "current.json"), JsonSerializer.Serialize(new { input = source.Extension, target, scenario = source.Scenario, started = DateTimeOffset.Now }));
            try
            {
                produced = (await Convert(source.Path, target)).ToArray(); conversionMs = clock.ElapsedMilliseconds; clock.Restart();
                Require(produced.Length > 0, "No outputs"); var result = produced[0];
                if ((kind == FileKind.Pdf && !source.HasText && target is "txt" or "md" or "epub" or "xlsx") || (kind == FileKind.Video && !source.HasAudio && target == "mp3")) throw new InvalidDataException("Unavailable source content was reported as a successful conversion.");
                if (kind == FileKind.Archive) { var before = Contents(source.Path); var after = Contents(result); Require(before.Count == after.Count && before.All(x => after.TryGetValue(x.Key, out var hash) && hash == x.Value), "Archive entries changed"); }
                else if (kind is FileKind.Audio or FileKind.Video) { using var probe = await Probe(result); var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray(); bool wantAudio = kind == FileKind.Audio || target == "mp3"; Require(streams.Any(s => s.GetProperty("codec_type").GetString() == (wantAudio ? "audio" : "video")), "Missing media stream"); var duration = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture); Require(duration > 1.7 && duration < 2.5, "Media duration changed"); if (kind == FileKind.Video && source.Extension != "gif" && source.HasAudio && target != "gif") Require(streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), "Video lost its audio"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", result, "-f", "null", "-" }, CancellationToken.None); }
                else if (target is "png" or "jpg" or "webp" or "heic" or "avif" or "bmp" or "tiff")
                {
                    if (Directory.Exists(result)) { var files = Directory.GetFiles(result); Require(files.Length == source.Pages, "Rendered page count changed"); foreach (var file in files) CheckPicture(file, kind == FileKind.Image); } else { CheckPicture(result, kind == FileKind.Image); }
                }
                else if (target == "pdf") { using var pdf = UglyToad.PdfPig.PdfDocument.Open(result); Require(pdf.NumberOfPages == source.Pages, "PDF page count changed"); if (kind != FileKind.Image) HasMarker(string.Join(" ", pdf.GetPages().Select(p => p.Text))); }
                else if (target == "docx") { using (var archive = ZipFile.OpenRead(result)) { if (!source.HasText) Require(archive.Entries.Count(e => e.FullName.StartsWith("word/media/")) > 0, "Scanned PDF images missing from DOCX"); else { var body = TextPart(result, "word/document.xml"); HasMarker(body); if (kind != FileKind.Pdf) HasUnicode(body); } } if (!source.HasText) { var roundTrip = (await Convert(result, "pdf"))[0]; using var pdf = UglyToad.PdfPig.PdfDocument.Open(roundTrip); Require(pdf.NumberOfPages == source.Pages, "Scanned PDF → DOCX introduced missing/extra pages"); } }
                else if (target == "pptx") { using var archive = ZipFile.OpenRead(result); Require(archive.Entries.Count(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$")) == source.Pages, "Slide count changed"); Require(archive.Entries.Any(e => e.FullName.StartsWith("ppt/media/")), "Slide images missing"); }
                else if (target is "xlsx" or "ods")
                {
                    using var archive = ZipFile.OpenRead(result); var body = string.Join(" ", archive.Entries.Where(e => e.FullName.EndsWith(".xml")).Select(e => { using var stream = e.Open(); return XDocument.Load(stream).Root!.Value; })); HasMarker(body); if (kind == FileKind.Spreadsheet) { Require(body.Contains("49"), "Spreadsheet lost calculated values"); HasUnicode(body); }
                }
                else if (target == "epub")
                {
                    using (var package = ZipFile.OpenRead(result)) { using var stream = package.GetEntry("OEBPS/content.opf")!.Open(); Require(XDocument.Load(stream).Descendants(XName.Get("title", "http://purl.org/dc/elements/1.1/")).Single().Value == Path.GetFileNameWithoutExtension(source.Path), "EPUB title lost the source filename"); }
                    using var archive = ZipFile.OpenRead(result); Require(archive.GetEntry("mimetype") != null, "Invalid EPUB"); var body = string.Join(" ", archive.Entries.Where(e => e.FullName.EndsWith(".xhtml")).Select(e => { using var stream = e.Open(); return XDocument.Load(stream).Root!.Value; })); HasMarker(body); if (kind != FileKind.Pdf) HasUnicode(body); if (source.Extension is "html" or "htm") Require(!body.Contains("STYLE_CANARY"), "HTML style code leaked into EPUB");
                }
                else
                {
                    var body = File.ReadAllText(result); HasMarker(body); if (kind == FileKind.Spreadsheet) Require(body.Contains("49"), "Spreadsheet lost its total"); if (source.Extension is "html" or "htm" && target is "txt" or "md") Require(!body.Contains("STYLE_CANARY"), "HTML style code leaked into text");
                    if (kind != FileKind.Pdf) HasUnicode(body);
                    if (target is "srt" or "vtt") Require(body.Contains(target == "srt" ? "00:00:00,200" : "00:00:00.200") && body.Contains(target == "srt" ? "00:00:01,700" : "00:00:01.700"), "Subtitle timing changed");
                }
                if (kind == FileKind.Spreadsheet && target is "csv" or "tsv" or "xlsx" or "ods") CheckSheetCells(result, target);
                Require(sourceHashes[source.Path] == System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.Path))), "Source was modified");
                results.Add(new("conversion", kind.ToString(), source.Extension, target, source.Scenario, true, conversionMs, clock.ElapsedMilliseconds, produced, null));
            }
            catch (Exception ex) { bool expected = (kind == FileKind.Pdf && !source.HasText && target is "txt" or "md" or "epub" or "xlsx") || (kind == FileKind.Video && !source.HasAudio && target == "mp3"); bool rejected = expected && produced.Length == 0 && (kind == FileKind.Pdf ? ex is NotSupportedException && ex.Message.Contains("text", StringComparison.OrdinalIgnoreCase) : ex is InvalidDataException && ex.Message.Contains("no audio", StringComparison.OrdinalIgnoreCase)); results.Add(new(expected ? "source-limit" : "conversion", kind.ToString(), source.Extension, target, source.Scenario, rejected, conversionMs, clock.ElapsedMilliseconds, produced, ex.ToString())); }
            Save();
        }
        Require(!Directory.GetDirectories(outputs, ".tangerine-*").Any(), "Staging folders leaked");
        var summary = new { planned = planned.Length, conversions = results.Count(x => x.phase == "conversion"), passed = results.Count(x => x.phase == "conversion" && x.passed), failed = results.Count(x => !x.passed), sourceLimits = results.Count(x => x.phase == "source-limit"), categories = results.Where(x => x.phase == "conversion").GroupBy(x => x.category).Select(g => new { category = g.Key, checkedPairs = g.Count(), passed = g.Count(x => x.passed), failed = g.Count(x => !x.passed) }) };
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
}
