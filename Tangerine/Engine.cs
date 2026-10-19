using ImageMagick;
namespace Tangerine;

public sealed partial class Engine
{
    readonly AppSettings settings;
    public Engine(AppSettings settings) => this.settings = settings;
    public string? FindTool(string name)
    {
        var installDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dirs = new[] { settings.EngineDirectory, Path.Combine(installDirectory, "engines"), installDirectory, Path.Combine(AppContext.BaseDirectory, "engines"), AppContext.BaseDirectory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var d in dirs.Where(x => !string.IsNullOrWhiteSpace(x))) { var p = Path.Combine(d, name + ".exe"); if (File.Exists(p)) return p; }
        return null;
    }
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!; var stem = Catalog.Stem(path); var ext = path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(path);
        for (int n = 2; ; n++) { var p = Path.Combine(dir, $"{stem} {n}{ext}"); if (!File.Exists(p) && !Directory.Exists(p)) return p; }
    }
    public async Task<List<string>> Run(string[] inputs, WheelItem item, WheelMode mode, OperationOptions options, CancellationToken token)
    {
        if (inputs.Length == 0) throw new InvalidOperationException("Choose at least one file.");
        options.Validate(mode == WheelMode.Tools ? item.Id : null);
        token.ThrowIfCancellationRequested();
        foreach (var input in inputs) if (!File.Exists(input) && !Directory.Exists(input)) throw new FileNotFoundException("The source is no longer available.", input);
        if (mode == WheelMode.Convert && inputs.Any(p => Catalog.Extension(p) != item.Id && !Catalog.Conversions(p).Any(x => x.Id == item.Id))) throw new NotSupportedException("This conversion is not available for every selected file.");
        if (mode == WheelMode.Tools && inputs.Any(p => !Catalog.Tools(p, inputs.Length).Any(x => x.Id == item.Id))) throw new NotSupportedException($"{item.Description} is not available for this file selection.");
        var output = string.IsNullOrWhiteSpace(settings.OutputDirectory) ? Path.GetDirectoryName(inputs[0])! : settings.OutputDirectory;
        Directory.CreateDirectory(output);
        var work = Path.Combine(Path.GetFullPath(output), ".tangerine-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        var committed = new List<string>();
        try
        {
            var files = await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (mode == WheelMode.Tools && item.Id is "createpdf" or "merge" or "collage" or "join") return await Group(inputs, item.Id, work, options, token);
                var generated = new List<string>();
                for (int i = 0; i < inputs.Length; i++)
                {
                    token.ThrowIfCancellationRequested(); var input = inputs[i];
                    var sub = Path.Combine(work, i.ToString()); Directory.CreateDirectory(sub);
                    var name = Catalog.Stem(input); var ext = Catalog.Extension(input);
                    if (mode == WheelMode.Convert)
                    {
                        var target = Path.Combine(sub, name + "." + (item.Id == "gz" ? "tar.gz" : item.Id));
                        generated.AddRange(await ConvertFile(input, target, item.Id, options, token));
                    }
                    else
                    {
                        var target = Path.Combine(sub, $"{name} {item.Description}.{ext}");
                        generated.AddRange(await Tool(input, target, item.Id, options, token));
                    }
                }
                return generated;
            }, token);
            token.ThrowIfCancellationRequested();
            foreach (var file in files)
            {
                if (File.Exists(file) && new FileInfo(file).Length == 0) throw new IOException("The converter produced an empty file.");
                var final = Unique(Path.Combine(output, Path.GetFileName(file)));
                if (Directory.Exists(file)) Directory.Move(file, final); else File.Move(file, final, false);
                committed.Add(final);
            }
            if (committed.Count == 0) throw new InvalidDataException("The converter did not produce any output.");
            return committed;
        }
        catch { foreach (var p in committed) { if (File.Exists(p)) File.Delete(p); else if (Directory.Exists(p)) Directory.Delete(p, true); } throw; }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }
    async Task<List<string>> ConvertFile(string input, string target, string format, OperationOptions o, CancellationToken ct)
    {
        switch (Catalog.Kind(input))
        {
            case FileKind.Image:
                return ConvertImage(input, target, format, o);
            case FileKind.Audio: case FileKind.Video: await MediaConvert(input, target, format, o, ct); break;
            case FileKind.Pdf: return await PdfConvert(input, target, format, ct);
            case FileKind.Archive: return ArchiveConvert(input, target, format, ct);
            case FileKind.Subtitle: File.WriteAllText(target, Subtitles(File.ReadAllText(input), format), new UTF8Encoding(false)); break;
            default: return await DocumentConvert(input, target, format, ct);
        }
        return new() { target };
    }
    async Task<List<string>> Tool(string input, string target, string id, OperationOptions o, CancellationToken ct)
    {
        switch (Catalog.Kind(input))
        {
            case FileKind.Image: return ImageTool(input, target, id, o);
            case FileKind.Video: case FileKind.Audio: return await MediaTool(input, target, id, o, ct);
            case FileKind.Pdf: return PdfTool(input, target, id, o);
            case FileKind.Archive when id == "extract":
                var dir = Path.Combine(Path.GetDirectoryName(target)!, Catalog.Stem(input) + " Extracted"); ExtractArchive(input, dir, ct); return new() { dir };
            default: throw new NotSupportedException("This operation is not available for this file type.");
        }
    }
    async Task<List<string>> Group(string[] inputs, string id, string work, OperationOptions o, CancellationToken ct)
    {
        var target = Path.Combine(work, id switch { "createpdf" => "Combined.pdf", "merge" => "Merged.pdf", "join" => "Joined.mp4", _ => "Collage.png" });
        if (id == "createpdf") ImagesToPdf(inputs, target);
        else if (id == "merge") MergePdf(inputs, target);
        else if (id == "collage")
        {
            using var canvas = MakeCollage(inputs, o, ct); canvas.Write(target);
        }
        else await JoinVideos(inputs, target, ct);
        return new() { target };
    }
    internal static MagickImage MakeCollage(string[] inputs, OperationOptions o, CancellationToken ct, double previewScale = 1)
    {
        int columns = o.CollageColumns > 0 ? o.CollageColumns : (int)Math.Ceiling(Math.Sqrt(inputs.Length)), rows = (int)Math.Ceiling((double)inputs.Length / columns), gap = o.CollageGap;
        int width = o.CollageWidth > 0 ? o.CollageWidth : columns * 600 + (columns + 1) * gap, height = o.CollageWidth > 0 ? (int)Math.Round(width / o.CollageRatio) : rows * 600 + (rows + 1) * gap;
        width = Math.Max(1, (int)(width * previewScale)); height = Math.Max(1, (int)(height * previewScale)); gap = (int)(gap * previewScale); int cellW = (width - (columns + 1) * gap) / columns, cellH = (height - (rows + 1) * gap) / rows; if (cellW < 1 || cellH < 1) throw new ArgumentException("Reduce the spacing or increase the canvas size.");
        var canvas = new MagickImage(new MagickColor(o.Color), (uint)width, (uint)height);
        try { for (int i = 0; i < inputs.Length; i++) { ct.ThrowIfCancellationRequested(); using var image = new MagickImage(inputs[i]); image.AutoOrient(); if (o.CollageFill) { image.Resize(new MagickGeometry((uint)cellW, (uint)cellH) { FillArea = true }); image.Crop((uint)cellW, (uint)cellH, Gravity.Center); image.ResetPage(); } else image.Resize((uint)cellW, (uint)cellH); canvas.Composite(image, (i % columns) * (cellW + gap) + gap + (cellW - (int)image.Width) / 2, (i / columns) * (cellH + gap) + gap + (cellH - (int)image.Height) / 2, CompositeOperator.Over); } return canvas; } catch { canvas.Dispose(); throw; }
    }
    public static async Task<string> Process(string exe, IEnumerable<string> args, CancellationToken ct, string? working = null)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = working ?? Path.GetDirectoryName(exe)! };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = psi }; process.Start();
        using var cancel = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); } catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } catch (InvalidOperationException) { } try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { } throw; }
        var error = await stderr; var result = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, error.Split('\n').TakeLast(8)).Trim());
        return result;
    }
    public string Ffmpeg => FindTool("ffmpeg") ?? throw new InvalidOperationException("FFmpeg is required for audio and video. Set its folder in Settings, or run scripts/setup-engines.ps1.");
    static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Subtitles(string text, string format)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        text = Regex.Replace(text, @"\AWEBVTT[^\n]*\n*", "");
        text = Regex.Replace(text, @"(?m)^(NOTE|STYLE|REGION)[\s\S]*?(\n\n|$)", "");
        text = Regex.Replace(text, @"(?<![\d:])(\d{2}:\d{2})([.,])(\d{3})", m => "00:" + m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value);
        text = Regex.Replace(text, @"(\d{2}:\d{2}:\d{2})[.,](\d{3})", m => m.Groups[1].Value + (format == "vtt" ? "." : ",") + m.Groups[2].Value);
        var blocks = Regex.Split(text.Trim(), @"\n\s*\n"); var cues = new List<string>();
        foreach (var block in blocks)
        {
            var lines = block.Split('\n'); var t = Array.FindIndex(lines, l => l.Contains("-->")); if (t < 0) continue;
            var time = Regex.Match(lines[t], @"(\d{2}:\d{2}:\d{2}[.,]\d{3})\s+-->\s+(\d{2}:\d{2}:\d{2}[.,]\d{3})"); if (!time.Success) continue;
            var dialogue = string.Join("\n", lines.Skip(t + 1));
            if (format == "txt") cues.Add(System.Net.WebUtility.HtmlDecode(Regex.Replace(dialogue, "<[^>]+>", "")));
            else cues.Add((cues.Count + 1) + "\n" + time.Groups[1].Value + " --> " + time.Groups[2].Value + "\n" + dialogue);
        }
        if (cues.Count == 0) throw new InvalidDataException("No valid subtitle cues were found.");
        return (format == "vtt" ? "WEBVTT\n\n" : "") + string.Join(format == "txt" ? "\n" : "\n\n", cues) + "\n";
    }
}
