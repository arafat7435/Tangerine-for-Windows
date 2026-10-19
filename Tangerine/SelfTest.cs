using ImageMagick;
using System.IO.Compression;
using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
namespace Tangerine;
public static class SelfTest
{
    public static async Task Run(App app, string directory, bool previewOnly = false, bool coreOnly = false, bool requirePublic = false, bool requireStandalone = false)
    {
        Directory.CreateDirectory(directory); var results = new List<object>(); var report = Path.Combine(directory, "results.json");
        var inputs = Path.Combine(directory, "inputs"); var outputs = Path.Combine(directory, "outputs"); Directory.CreateDirectory(inputs); Directory.CreateDirectory(outputs);
        var settings = new AppSettings { OutputDirectory = outputs, EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory }; var engine = new Engine(settings);
        async Task Test(string name, Func<Task> run) { var watch = Stopwatch.StartNew(); try { await run(); results.Add(new { name, passed = true, ms = watch.ElapsedMilliseconds }); } catch (Exception ex) { results.Add(new { name, passed = false, error = ex.ToString(), ms = watch.ElapsedMilliseconds }); } File.WriteAllText(report, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
        Task<List<string>> Convert(string input, string format) => engine.Run(new[] { input }, new(format, format, "Convert to " + format), WheelMode.Convert, new(), CancellationToken.None);
        Task<List<string>> Tool(string[] files, string id, OperationOptions? o = null) => engine.Run(files, new(id, id, id), WheelMode.Tools, o ?? new(), CancellationToken.None);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        if (requirePublic) await Test("Public build excludes the private reference sound", () => { Check(!WheelSound.Available, "The public binary embeds a private sound asset"); return Task.CompletedTask; });
        if (requireStandalone)
        {
            await Test("Standalone executable uses its bundled .NET and WPF runtime", () =>
            {
                foreach (var assembly in new[] { typeof(object).Assembly, typeof(Window).Assembly })
                    Check(Path.GetDirectoryName(assembly.Location)!.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase), "A runtime assembly was loaded outside the extracted bundle: " + assembly.FullName);
                // Windows single-file hosts can statically link CoreCLR into the EXE.
                using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Tangerine.runtimeconfig.json")));
                var runtime = configuration.RootElement.GetProperty("runtimeOptions");
                Check(!runtime.TryGetProperty("frameworks", out _) && runtime.GetProperty("includedFrameworks").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "Microsoft.NETCore.App"), "The EXE depends on a shared .NET installation");
                return Task.CompletedTask;
            });
            await Test("Standalone executable carries readable licenses and runtime inventory", () =>
            {
                Check(File.ReadAllText(Path.Combine(App.NoticesDirectory, "LICENSE")).Contains("MIT License"), "Project license is missing");
                Check(File.Exists(Path.Combine(App.NoticesDirectory, "licenses", "pdfium-8100", "LICENSE")), "PDFium license is missing");
                using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(App.NoticesDirectory, "dependencies.json")));
                foreach (var runtime in new[] { "Microsoft.NETCore.App.Runtime.win-x64", "Microsoft.WindowsDesktop.App.Runtime.win-x64" })
                    Check(inventory.RootElement.EnumerateArray().Any(x => string.Equals(x.GetProperty("name").GetString(), runtime, StringComparison.OrdinalIgnoreCase)), "Runtime notice is absent: " + runtime);
                return Task.CompletedTask;
            });
        }
        var image = Path.Combine(inputs, "sample.png"); using (var img = new MagickImage("gradient:#FF5900-#33AAFF", 160, 120)) img.Write(image);
        var original = SHA256.HashData(File.ReadAllBytes(image));
        await Test("Background preview proportions match the exported PNG", async () =>
        {
            var output = (await Tool(new[] { image }, "background", new() { Padding = 80, Radius = 24 }))[0]; using var exported = new MagickImage(output);
            var window = new OptionsWindow(image, new("background", "ADD BG", "Add Background"), settings); try { window.Show(); await Task.Delay(180); window.UpdateLayout(); var size = window.BackgroundPreviewSize; Check(size.Height > 0 && Math.Abs(size.Width / size.Height - (double)exported.Width / exported.Height) < .01, "Preview and export have different proportions"); App.Capture(window, Path.Combine(directory, "background-editor.png")); } finally { window.Close(); }
        });
        if (previewOnly) { File.WriteAllText(Path.Combine(directory, "complete.txt"), "1 focused preview check completed. See results.json."); return; }
        await Test("Radial geometry, 1–9 sectors, center dead zone", () => { for (int n = 1; n <= 9; n++) { var wheel = new RadialWheel(); wheel.SetItems(Enumerable.Range(0, n).Select(i => new WheelItem(i.ToString(), i.ToString(), "Item")).ToArray()); Check(wheel.Hit(new Point(144, 144)) == -1, "Center was actionable"); for (int i = 0; i < n; i++) { double a = (n == 3 ? Math.PI : -Math.PI / 2) + i * Math.PI * 2 / n; Check(wheel.Hit(new Point(144 + 94 * Math.Cos(a), 144 + 94 * Math.Sin(a))) == i, $"Wrong sector {i}/{n}"); } } return Task.CompletedTask; });
        foreach (var format in new[] { "jpg", "webp", "heic", "avif", "bmp", "tiff" }) await Test("Image conversion: " + format, async () => { var files = await Convert(image, format); using var im = new MagickImage(files[0]); Check(im.Width == 160 && im.Height == 120, "Dimensions changed"); });
        await Test("Crop: exact output dimensions", async () => { var p = (await Tool(new[] { image }, "crop", new() { X = 10, Y = 20, Width = 70, Height = 60 }))[0]; using var im = new MagickImage(p); Check(im.Width == 70 && im.Height == 60, "Crop dimensions wrong"); });
        await Test("Redaction replaces pixels", async () => { var p = (await Tool(new[] { image }, "redact", new() { X = 10, Y = 10, Width = 40, Height = 40 }))[0]; using var im = new MagickImage(p); using var pixels = im.GetPixels(); var c = pixels.GetPixel(20, 20).ToColor()!; Check(c.R == 0 && c.G == 0 && c.B == 0, "Redacted area is not black"); });
        await Test("Background: padding and rounded-corner render", async () => { var p = (await Tool(new[] { image }, "background", new() { Padding = 30, Radius = 15 }))[0]; using var im = new MagickImage(p); Check(im.Width == 220 && im.Height == 180, "Wrong padding"); });
        await Test("Photo editing: rotation", async () => { var p = (await Tool(new[] { image }, "edit", new() { Rotation = 90 }))[0]; using var im = new MagickImage(p); Check(im.Width == 120 && im.Height == 160, "Rotation failed"); });
        await Test("Annotation changes pixels", async () => { var p = (await Tool(new[] { image }, "annotate", new() { X = 8, Y = 40, Text = "Hello", FontSize = 20, Color = "#000000" }))[0]; Check(!SHA256.HashData(File.ReadAllBytes(p)).SequenceEqual(original), "No annotation"); });
        await Test("Remove image metadata", async () => { var tagged = Path.Combine(inputs, "tagged.jpg"); using (var im = new MagickImage(image)) { im.SetAttribute("comment", "private fixture"); im.Write(tagged); } var p = (await Tool(new[] { tagged }, "metadata"))[0]; using var result = new MagickImage(p); Check(string.IsNullOrEmpty(result.GetAttribute("comment")), "Comment survived"); });
        await Test("Batch collage", async () => { var p = (await Tool(new[] { image, image, image }, "collage"))[0]; using var im = new MagickImage(p); Check(im.Width > 1000 && im.Height > 1000, "Missing collage grid"); });
        string pdf = "", merged = "";
        await Test("Image to PDF", async () => { pdf = (await Convert(image, "pdf"))[0]; using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import); Check(doc.PageCount == 1, "Expected one page"); });
        await Test("Merge and reorder PDF pages", async () => { merged = (await Tool(new[] { pdf, pdf }, "merge"))[0]; var p = (await Tool(new[] { merged }, "organize", new() { Pages = "2,1,2", Rotation = 90 }))[0]; using var doc = PdfReader.Open(p, PdfDocumentOpenMode.Import); Check(doc.PageCount == 3 && doc.Pages[0].Rotate == 90, "Page organization failed"); });
        await Test("Split PDF", async () => { var p = (await Tool(new[] { merged }, "split"))[0]; Check(Directory.GetFiles(p, "*.pdf").Length == 2, "Expected two PDFs"); });
        await Test("PDF rasterization at 300 DPI", async () => { var dir = (await Convert(pdf, "png"))[0]; using var im = new MagickImage(Directory.GetFiles(dir)[0]); Check(im.Width == 500 && im.Height == 375, "Wrong PDF image dimensions"); });
        await Test("PDF compression and metadata", async () => { await Tool(new[] { pdf }, "compress"); await Tool(new[] { pdf }, "metadata"); });
        await Test("Subtitle timestamps survive round trip", async () => { var p = Path.Combine(inputs, "captions.srt"); File.WriteAllText(p, "1\n00:00:01,200 --> 00:00:02,800\nHello world\n\n2\n00:00:03,000 --> 00:00:04,000\nSecond cue\n"); var vtt = (await Convert(p, "vtt"))[0]; var srt = (await Convert(vtt, "srt"))[0]; var text = File.ReadAllText(srt); Check(text.Contains("00:00:01,200 --> 00:00:02,800") && text.Contains("Second cue"), "Cue times lost"); });
        var folder = Path.Combine(inputs, "archive-source"); Directory.CreateDirectory(Path.Combine(folder, "nested")); File.WriteAllText(Path.Combine(folder, "nested", "hello.txt"), "archive fixture"); var zip = Path.Combine(inputs, "sample.zip"); if (!File.Exists(zip)) ZipFile.CreateFromDirectory(folder, zip);
        await Test("ZIP → TAR → GZIP → extracted contents", async () => { var tar = (await Convert(zip, "tar"))[0]; var gz = (await Convert(tar, "gz"))[0]; var extracted = (await Tool(new[] { gz }, "extract"))[0]; Check(File.ReadAllText(Path.Combine(extracted, "nested", "hello.txt")) == "archive fixture", "Archive changed contents"); });
        await Test("Reject archive path traversal", () => { try { Engine.SafeArchivePath(outputs, "../escape.txt"); throw new Exception("Unsafe path accepted"); } catch (InvalidDataException) { } return Task.CompletedTask; });
        await Test("Reject invalid page ranges", () => { try { Engine.PageOrder("0,3", 2); throw new Exception("Invalid page accepted"); } catch (ArgumentException) { } return Task.CompletedTask; });
        await Test("Cancellation creates no output", async () => { using var cts = new CancellationTokenSource(); cts.Cancel(); try { await engine.Run(new[] { image }, new("jpg", "JPG", "Convert"), WheelMode.Convert, new(), cts.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { } Check(!Directory.GetDirectories(outputs, ".tangerine-*").Any(), "Working directory leaked"); });
        await Test("Collision-safe output and original preserved", async () => { var one = (await Convert(image, "jpg"))[0]; var two = (await Convert(image, "jpg"))[0]; Check(one != two, "Output was overwritten"); Check(SHA256.HashData(File.ReadAllBytes(image)).SequenceEqual(original), "Source changed"); });
        if (coreOnly) { File.WriteAllText(Path.Combine(directory, "complete.txt"), $"{results.Count} core checks completed. Office, media engines and native input tests are outside this suite."); return; }
        var textFile = Path.Combine(inputs, "notes.txt"); File.WriteAllText(textFile, "Tangerine integration test\nLocal files stay local.\nSecond paragraph."); string docx = "", wordPdf = "";
        await Test("Word: TXT → DOCX → PDF", async () => { docx = (await Convert(textFile, "docx"))[0]; wordPdf = (await Convert(docx, "pdf"))[0]; using var doc = UglyToad.PdfPig.PdfDocument.Open(wordPdf); Check(doc.GetPages().Any(p => p.Text.Contains("integration test")), "PDF lost document text"); });
        await Test("PDF text, DOCX, EPUB, and EPUB input", async () => { var txt = (await Convert(wordPdf, "txt"))[0]; Check(File.ReadAllText(txt).Contains("Tangerine"), "PDF text missing"); await Convert(wordPdf, "docx"); var epub = (await Convert(docx, "epub"))[0]; var plain = (await Convert(epub, "txt"))[0]; Check(File.ReadAllText(plain).Contains("Local files stay local"), "EPUB round trip failed"); });
        await Test("Excel: CSV → XLSX / ODS / TSV / PDF", async () => { var csv = Path.Combine(inputs, "numbers.csv"); File.WriteAllText(csv, "Name,Value\nOranges,12\nApples,7"); var xlsx = (await Convert(csv, "xlsx"))[0]; using (var z = ZipFile.OpenRead(xlsx)) Check(z.GetEntry("xl/workbook.xml") != null, "Invalid workbook"); await Convert(xlsx, "ods"); await Convert(xlsx, "tsv"); var p = (await Convert(xlsx, "pdf"))[0]; using var doc = PdfReader.Open(p, PdfDocumentOpenMode.Import); Check(doc.PageCount >= 1, "Empty sheet PDF"); });
        await Test("PDF → PowerPoint → PDF", async () => { var pptx = (await Convert(merged, "pptx"))[0]; var p = (await Convert(pptx, "pdf"))[0]; using var doc = PdfReader.Open(p, PdfDocumentOpenMode.Import); Check(doc.PageCount == 2, "Slides did not round trip"); });
        await Test("Office conversions preserve an existing PowerPoint document", async () =>
        {
            dynamic powerpoint = Engine.CreateOffice("PowerPoint.Application"); dynamic? existing = null;
            try { existing = powerpoint.Presentations.Add(0); string name = existing.Name; object security = powerpoint.AutomationSecurity; var pptx = (await Convert(merged, "pptx"))[0]; await Convert(pptx, "pdf"); Check((string)existing.Name == name && (int)powerpoint.Presentations.Count >= 1, "An existing presentation was closed"); Check((int)powerpoint.AutomationSecurity == (int)security, "Office security preference was changed"); }
            finally { if (existing != null) { existing.Close(); Marshal.FinalReleaseComObject((object)existing); } if ((int)powerpoint.Presentations.Count == 0) powerpoint.Quit(); Marshal.FinalReleaseComObject((object)powerpoint); }
        });
        await Test("PDF → XLSX text extraction", async () => { var p = (await Convert(wordPdf, "xlsx"))[0]; using var zip = ZipFile.OpenRead(p); Check(zip.GetEntry("xl/workbook.xml") != null, "Invalid spreadsheet"); });
        var audio = Path.Combine(inputs, "tone.wav"); var video = Path.Combine(inputs, "clip.mp4");
        await Test("FFmpeg creates audio/video fixtures", async () => { await Engine.Process(engine.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", audio }, CancellationToken.None); await Engine.Process(engine.Ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "testsrc=size=160x120:rate=15:duration=2", "-i", audio, "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video }, CancellationToken.None); });
        foreach (var fmt in new[] { "mp3", "m4a", "flac", "ogg", "opus", "aiff", "wma" }) await Test("Audio conversion: " + fmt, async () => { await Convert(audio, fmt); });
        foreach (var fmt in new[] { "mov", "mkv", "webm", "avi", "wmv", "gif", "mp3" }) await Test("Video conversion: " + fmt, async () => { await Convert(video, fmt); });
        foreach (var id in new[] { "compress", "metadata", "normalize", "trim", "channels", "bleep", "visualize" }) await Test("Audio tool: " + id, async () => { await Tool(new[] { audio }, id, new() { Start = .2, Duration = .5 }); });
        foreach (var id in new[] { "compress", "metadata", "mute", "trim", "crop", "redact", "speed", "snapshot", "split" }) await Test("Video tool: " + id, async () => { await Tool(new[] { video }, id, new() { Start = .2, Duration = .5, X = 4, Y = 4, Width = 100, Height = 80 }); });
        await Test("Join video clips", async () => { await Tool(new[] { video, video }, "join"); });
        await Test("Capture native UI and editors", async () => { var main = new MainWindow(app); main.Show(); await Task.Delay(250); App.Capture(main, Path.Combine(directory, "main-window.png")); foreach (var id in new[] { "crop", "background" }) { var window = new OptionsWindow(image, new(id, id, id == "crop" ? "Crop Image" : "Add Background"), settings) { Owner = main }; window.Show(); await Task.Delay(180); App.Capture(window, Path.Combine(directory, id + "-editor.png")); window.Close(); } main.Hide(); });
        File.WriteAllText(Path.Combine(directory, "complete.txt"), $"{results.Count} checks completed. See results.json.");
    }
}
