using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Security.Cryptography;
namespace Tangerine;

internal static class StressAudit
{
    internal static async Task Run(App app, string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "inputs"); var output = Path.Combine(directory, "outputs"); Directory.CreateDirectory(input); Directory.CreateDirectory(output);
        var engine = new Engine(new() { OutputDirectory = output, EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory });
        var results = new List<object>(); var originals = new Dictionary<string, byte[]>();
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
        string Track(string path) { originals[path] = SHA256.HashData(File.ReadAllBytes(path)); return path; }
        async Task Test(string name, Func<Task> run) { var watch = Stopwatch.StartNew(); try { await run(); results.Add(new { name, passed = true, ms = watch.ElapsedMilliseconds }); } catch (Exception ex) { results.Add(new { name, passed = false, error = ex.ToString(), ms = watch.ElapsedMilliseconds }); } File.WriteAllText(Path.Combine(directory, "stress-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
        async Task Reject(Func<Task> run) { try { await run(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or MagickException) { return; } throw new InvalidDataException("Invalid input was accepted."); }
        WheelItem Item(string id) => new(id, id, id);
        async Task<List<string>> Tool(string path, string id, OperationOptions options) => await engine.Run(new[] { path }, Item(id), WheelMode.Tools, options, CancellationToken.None);
        var images = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            var folder = Path.Combine(input, "source " + i); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "scan ' हिंदी & [same]." + (i % 2 == 0 ? "png" : "jpg"));
            using var image = new MagickImage("gradient:#E06028-#183990", (uint)(40 + i % 5 * 8), (uint)(32 + i % 3 * 8)); image.Write(path); images.Add(Track(path));
        }
        await Test("100-file conversion: unique outputs, decoded contents and existing destination preserved", async () =>
        {
            var occupied = Path.Combine(output, "scan ' हिंदी & [same].webp"); File.WriteAllText(occupied, "keep existing output");
            var plan = new BatchPlan(images.ToArray(), Item("webp"), WheelMode.Convert); await BatchRunner.Run(plan, engine, 82, CancellationToken.None);
            Check(plan.Entries.All(e => e.State == "Done"), string.Join("; ", plan.Entries.Where(e => e.State != "Done").Select(e => e.Error)));
            var paths = plan.Entries.SelectMany(e => e.Outputs).ToArray(); Check(paths.Length == 100 && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 100, "Output collision");
            foreach (var entry in plan.Entries) { using var source = new MagickImage(entry.Input); using var result = new MagickImage(entry.Outputs.Single()); Check(source.Width == result.Width && source.Height == result.Height, "Conversion changed dimensions"); }
            Check(File.ReadAllText(occupied) == "keep existing output", "Existing output was overwritten");
        });
        await Test("100-file crop: proportional settings plus every tenth file's override", async () =>
        {
            var plan = new BatchPlan(images.ToArray(), Item("crop"), WheelMode.Tools); await plan.SetCommon(images[0], new() { X = 0, Y = 0, Width = 20, Height = 16 }, engine, CancellationToken.None);
            for (int i = 0; i < 100; i += 10) plan.Entries[i].CustomOptions = new() { X = 1, Y = 1, Width = 7, Height = 9 };
            await BatchRunner.Run(plan, engine, 82, CancellationToken.None); Check(plan.Entries.All(e => e.State == "Done"), "Large crop batch failed");
            for (int i = 0; i < 100; i++) { using var source = new MagickImage(images[i]); using var result = new MagickImage(plan.Entries[i].Outputs.Single()); Check(result.Width == (i % 10 == 0 ? 7 : source.Width / 2) && result.Height == (i % 10 == 0 ? 9 : source.Height / 2), "Wrong per-file crop at " + i); }
        });
        await Test("100-file cancellation after 25 results retains only completed outputs", async () =>
        {
            using var cancellation = new CancellationTokenSource(); var plan = new BatchPlan(images.ToArray(), Item("bmp"), WheelMode.Convert); int done = 0;
            await BatchRunner.Run(plan, engine, 82, cancellation.Token, e => { if (e.State == "Done" && ++done == 25) cancellation.Cancel(); });
            Check(plan.Entries.Count(e => e.State == "Done") == 25 && plan.Entries.Count(e => e.State == "Cancelled") == 75, "Cancellation accounting failed");
            Check(plan.Entries.SelectMany(e => e.Outputs).Count() == 25 && plan.Entries.SelectMany(e => e.Outputs).All(File.Exists), "Cancellation lost completed outputs");
        });
        var badImage = Path.Combine(input, "corrupt.png"); File.WriteAllText(badImage, "not a PNG"); Track(badImage);
        await Test("Large mixed-validity batch continues after 20 corrupt inputs", async () =>
        {
            var files = images.Take(40).ToList(); for (int i = 0; i < 20; i++) { var path = Path.Combine(input, $"corrupt {i}.png"); File.Copy(badImage, path); Track(path); files.Insert(i * 2, path); }
            var plan = new BatchPlan(files.ToArray(), Item("webp"), WheelMode.Convert); await BatchRunner.Run(plan, engine, 82, CancellationToken.None);
            Check(plan.Entries.Count(e => e.State == "Done") == 40 && plan.Entries.Count(e => e.State == "Failed") == 20, "Failure isolation failed"); Check(plan.Entries.Last().State == "Done", "Failure stopped later files");
        });
        await Test("Locked source fails individually, then succeeds after release", async () =>
        {
            var plan = new BatchPlan(images.Take(2).ToArray(), Item("png"), WheelMode.Convert);
            using (var locked = new FileStream(images[0], FileMode.Open, FileAccess.Read, FileShare.None)) await BatchRunner.Run(plan, engine, 82, CancellationToken.None);
            Check(plan.Entries[0].State == "Failed" && plan.Entries[1].State == "Done", "Locked input damaged the rest of the batch");
            var retry = new BatchPlan(new[] { images[0] }, Item("png"), WheelMode.Convert); await BatchRunner.Run(retry, engine, 82, CancellationToken.None); Check(retry.Entries[0].State == "Done", "Released input could not recover");
        });
        foreach (var ext in new[] { "png", "jpg", "webp", "heic", "avif", "tiff", "bmp", "pdf", "zip", "tar", "gz", "rar", "mp3", "wav", "mp4", "docx", "epub", "srt", "vtt" }) await Test("Malformed " + ext + " rejects with no committed output", async () =>
        {
            var path = Path.Combine(input, "broken." + ext); File.WriteAllText(path, "broken data"); Track(path); var before = Directory.GetFileSystemEntries(output).ToHashSet();
            var target = ext is "zip" or "tar" or "gz" or "rar" ? "zip" : ext is "mp3" or "wav" ? "flac" : ext == "mp4" ? "webm" : ext is "docx" or "epub" or "srt" or "vtt" ? "txt" : "png";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await Reject(async () => { await engine.Run(new[] { path }, Item(target), WheelMode.Convert, new(), timeout.Token); }); Check(before.SetEquals(Directory.GetFileSystemEntries(output)), "Rejected input left output or staging files");
        });
        var odd = Path.Combine(input, "odd ' 161x121.mp4"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc=size=161x121:rate=20:duration=1.5", "-f", "lavfi", "-i", "sine=frequency=440:duration=1.5", "-c:v", "libx264", "-pix_fmt", "yuv444p", "-c:a", "aac", odd }, CancellationToken.None); Track(odd);
        foreach (var format in new[] { "mp4", "mov", "mkv" })
        {
            var oddSource = odd; if (format != "mp4") { oddSource = Path.ChangeExtension(odd, format); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", odd, "-c", "copy", oddSource }, CancellationToken.None); Track(oddSource); }
            foreach (var id in new[] { "compress", "trim", "speed", "redact", "split" }) await Test($"Odd-dimension {format} video supports {id}", async () =>
            {
                var options = new OperationOptions { Start = .2, Duration = .5, Speed = 1.5, X = 10, Y = 10, Width = 40, Height = 30 }; var produced = await Tool(oddSource, id, options);
                var paths = produced.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p) : new[] { p }).ToArray(); Check(paths.Length > 0, "No video output");
                foreach (var path in paths) { using var probe = JsonDocument.Parse(await Engine.Process(engine.FindTool("ffprobe")!, new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "json", path }, CancellationToken.None)); var stream = probe.RootElement.GetProperty("streams")[0]; Check(stream.GetProperty("width").GetInt32() == 161 && stream.GetProperty("height").GetInt32() == 121, "Tool changed the frame dimensions"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-i", path, "-f", "null", "-" }, CancellationToken.None); }
            });
        }
        foreach (var size in new[] { 1, 2, 3, 40 }) await Test($"Video blur supports {size}-pixel selection", async () =>
        {
            var paths = await Tool(odd, "redact", new() { X = 11, Y = 13, Width = size, Height = size, BlurRedaction = true }); var bounds = await engine.MediaBounds(paths.Single(), CancellationToken.None); Check(bounds.Width == 161 && bounds.Height == 121, "Blur changed frame dimensions"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-i", paths.Single(), "-f", "null", "-" }, CancellationToken.None);
        });
        await Test("Nearby snapshot times cannot collide after filename rounding", async () => { var files = await Tool(odd, "snapshot", new() { SnapshotTimes = new() { .1001, .1002, .1003 } }); Check(Directory.GetFiles(files.Single(), "*.png").Length == 3, "Snapshot selection lost a file"); });
        await Test("Identical snapshot times save one frame", async () => { var files = await Tool(odd, "snapshot", new() { SnapshotTimes = new() { .1, .1, .2 } }); Check(Directory.GetFiles(files.Single(), "*.png").Length == 2, "Exact duplicate snapshot was not deduplicated"); });
        await Test("Snapshot without a video frame rejects rather than committing a partial folder", async () => { var before = Directory.GetFileSystemEntries(output).ToHashSet(); await Reject(async () => { await Tool(odd, "snapshot", new() { SnapshotTimes = new() { .1, 1.499 } }); }); Check(before.SetEquals(Directory.GetFileSystemEntries(output)), "Partial snapshot folder was committed"); });
        await Test("40-page PDF split, reverse organization, duplicate and merge preserve page structure", async () =>
        {
            var pdf = Path.Combine(input, "40 pages.pdf"); using (var doc = new PdfDocument()) { for (int i = 0; i < 40; i++) { var page = doc.AddPage(); page.Width = PdfSharp.Drawing.XUnit.FromPoint(200 + i); page.Height = PdfSharp.Drawing.XUnit.FromPoint(300); } doc.Save(pdf); }
            Track(pdf);
            var split = await Tool(pdf, "split", new() { Pages = "40-1,1" }); Check(Directory.GetFiles(split.Single(), "*.pdf").Length == 41, "Split lost or overwrote duplicate page");
            var organized = await Tool(pdf, "organize", new() { Pages = "40-1,1", Rotation = 90 }); using (var doc = PdfReader.Open(organized.Single(), PdfDocumentOpenMode.Import)) Check(doc.PageCount == 41 && doc.Pages[0].Width.Point == 239 && doc.Pages[0].Rotate == 90, "Page structure changed");
            var merged = await engine.Run(new[] { pdf, organized.Single() }, Item("merge"), WheelMode.Tools, new(), CancellationToken.None); using var result = PdfReader.Open(merged.Single(), PdfDocumentOpenMode.Import); Check(result.PageCount == 81, "Merged page count changed");
        });
        await Test("All stress inputs retain their original hashes", () => { foreach (var (path, hash) in originals) Check(File.Exists(path) && SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(hash), "Source changed: " + path); return Task.CompletedTask; });
        await Test("No staging folders remain after stress and rejected files", () => { Check(!Directory.GetDirectories(output, ".tangerine-*", SearchOption.AllDirectories).Any(), "Staging directory leaked"); return Task.CompletedTask; });
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { checks = results.Count, ApplicationDllSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))) }));
    }
}
