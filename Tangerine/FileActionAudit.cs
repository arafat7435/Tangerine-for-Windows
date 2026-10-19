using ImageMagick;
using ImageMagick.Drawing;
using System.Security.Cryptography;
using System.Windows.Interop;
using System.Reflection;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
namespace Tangerine;

// Isolated fixtures only. This suite never invokes WheelMode.Convert.
public sealed partial class FileActionAudit
{
    readonly App app;
    readonly string directory, input, output;
    readonly Engine engine;
    readonly AppSettings settings;
    readonly List<object> reports = new();
    readonly Dictionary<string, byte[]> originals = new();
    FileActionAudit(App app, string directory)
    {
        this.app = app; this.directory = Path.GetFullPath(directory); input = Path.Combine(this.directory, "inputs"); output = Path.Combine(this.directory, "outputs");
        Directory.CreateDirectory(input); Directory.CreateDirectory(output);
        settings = new() { OutputDirectory = output, EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory }; engine = new(settings);
    }
    public static async Task Run(App app, string directory, string? only = null, bool nativeDrag = false)
    {
        var audit = new FileActionAudit(app, directory);
        if (only == null || only == "images") await audit.Images();
        if (only == null || only == "media") await audit.Media();
        if (only == null || only == "files") await audit.Files();
        if (only == null || only == "ui") await audit.Editors();
        if (nativeDrag) { using (var image = new MagickImage(MagickColors.Orange, 80, 60)) image.Write(Path.Combine(directory, "drag-fixture.png")); await NativeDragAudit.Run(app, directory, audit.reports, true); }
        if (only == null || only == "desktop")
        {
            await audit.Test("Windows Share native interface", () => { var window = new Window(); try { window.Show(); _ = WindowsShare.GetForWindow(new WindowInteropHelper(window).Handle); } finally { window.Close(); } return Task.CompletedTask; });
            await DesktopAudit.Run(app, directory, audit.reports, true);
        }
        await audit.Test("Every source file remains byte-for-byte unchanged", () => { foreach (var (file, hash) in audit.originals) Check(File.Exists(file) && SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(hash), "Original changed: " + file); return Task.CompletedTask; });
        await audit.Test("No staging directories remain after actions", () => { Check(!Directory.GetDirectories(audit.output, ".tangerine-*", SearchOption.AllDirectories).Any(), "Staging directory leaked"); return Task.CompletedTask; });
        audit.Save();
    }
    void Save() => File.WriteAllText(Path.Combine(directory, "file-actions-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    async Task Test(string name, Func<Task> action)
    {
        var watch = Stopwatch.StartNew(); try { await action(); reports.Add(new { name, passed = true, ms = watch.ElapsedMilliseconds }); } catch (Exception ex) { reports.Add(new { name, passed = false, error = ex.ToString(), ms = watch.ElapsedMilliseconds }); }
        Save();
    }
    static void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new Exception("Invalid action succeeded.");
    }
    string Track(string path) { originals[path] = SHA256.HashData(File.ReadAllBytes(path)); return path; }
    Task<List<string>> Tool(string[] files, string id, OperationOptions? options = null, CancellationToken ct = default) => engine.Run(files, new(id, id, id), WheelMode.Tools, options ?? new(), ct);
    async Task<string> Tool(string file, string id, OperationOptions? options = null, CancellationToken ct = default) => (await Tool(new[] { file }, id, options, ct))[0];
    static byte[] Pixels(MagickImage image) => image.ToByteArray(MagickFormat.Rgba);
    async Task Images()
    {
        foreach (var ext in new[] { "png", "jpg", "webp", "heic", "avif", "tiff", "bmp", "jpeg", "tif", "heif" })
        {
            var file = Path.Combine(input, "image." + ext);
            await Test("Image fixture " + ext, () => { using var gradient = new MagickImage("gradient:#E04418-#1739D8", 160, 120); using var image = new MagickImage(gradient.ToByteArray(MagickFormat.Png)); image.SetAttribute("comment", "private fixture"); image.Write(file); Track(file); return Task.CompletedTask; });
            foreach (var id in Catalog.Tools(file).Select(x => x.Id)) await Test($"Image {ext}: {id}", async () =>
            {
                var options = new OperationOptions { X = 12, Y = 24, Width = 70, Height = 60, Padding = 20, Radius = 12, Rotation = 90, Brightness = 75, Saturation = 60, Text = "TEST", FontSize = 16, Quality = 45 };
                using var result = new MagickImage(await Tool(file, id, options));
                Check(result.Width == (id == "crop" ? 70 : id == "background" ? 200 : id == "edit" ? 120 : 160) && result.Height == (id == "crop" ? 60 : id == "background" ? 160 : id == "edit" ? 160 : 120), "Unexpected dimensions");
                if (id == "redact") { using var pixels = result.GetPixels(); var c = pixels.GetPixel(25, 35).ToColor()!; Check(c.R == 0 && c.G == 0 && c.B == 0, "Redaction is not opaque black"); }
                if (id == "metadata") Check(string.IsNullOrEmpty(result.GetAttribute("comment")), "Private comment survived");
                if (id == "annotate") { using var source = new MagickImage(file); Check(!Pixels(source).SequenceEqual(Pixels(result)), "Annotation did not alter pixels"); }
            });
        }
        var png = Path.Combine(input, "image.png");
        foreach (var rect in new[] { new OperationOptions { X = 0, Y = 0, Width = 160, Height = 120 }, new() { X = 159, Y = 119, Width = 1, Height = 1 }, new() { X = 13, Y = 17, Width = 67, Height = 51 } })
            await Test($"Crop exact pixels {rect.X},{rect.Y} {rect.Width}x{rect.Height}", async () => { using var before = new MagickImage(png); using var after = new MagickImage(await Tool(png, "crop", rect)); using var a = before.GetPixels(); using var b = after.GetPixels(); for (int y = 0; y < rect.Height; y++) for (int x = 0; x < rect.Width; x++) Check(a.GetPixel(rect.X + x, rect.Y + y).ToColor()!.Equals(b.GetPixel(x, y).ToColor()), "Wrong crop pixel origin"); });
        foreach (var id in new[] { "crop", "redact" }) foreach (var rect in new[] { new OperationOptions { X = 159, Y = 0, Width = 2, Height = 10 }, new() { X = -1, Y = 0, Width = 5, Height = 5 }, new() { X = 0, Y = 0, Width = 0, Height = 5 }, new() { X = int.MaxValue, Y = 0, Width = int.MaxValue, Height = 5 } })
                await Test($"Reject {id} rectangle {rect.X},{rect.Y} {rect.Width}x{rect.Height}", () => Reject(async () => { await Tool(png, id, rect); }));
        await Test("EXIF-oriented image crop uses displayed coordinates", async () =>
        {
            var file = Path.Combine(input, "oriented.jpg"); using (var image = new MagickImage(png)) { var profile = new ExifProfile(); profile.SetValue(ExifTag.Orientation, (ushort)6); image.SetProfile(profile); image.Orientation = ImageMagick.Orientation.RightTop; image.Write(file); }
            Track(file);
            using var result = new MagickImage(await Tool(file, "crop", new() { X = 100, Y = 130, Width = 20, Height = 30 })); Check(result.Width == 20 && result.Height == 30, "Orientation was ignored");
        });
        await Test("Batch crop failure rolls back all outputs", async () => { var small = Path.Combine(input, "small.png"); using (var image = new MagickImage(MagickColors.Red, 20, 20)) image.Write(small); Track(small); var before = Directory.GetFileSystemEntries(output).Order().ToArray(); await Reject(async () => { await Tool(new[] { png, small }, "crop", new() { X = 0, Y = 0, Width = 60, Height = 60 }); }); Check(before.SequenceEqual(Directory.GetFileSystemEntries(output).Order()), "Partial batch output survived"); });
        await Test("Repeated crop never overwrites previous output", async () => { var o = new OperationOptions { Width = 40, Height = 40 }; var first = await Tool(png, "crop", o); var hash = SHA256.HashData(File.ReadAllBytes(first)); var second = await Tool(png, "crop", o); Check(first != second && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(first))), "Output overwritten"); });
        await Test("Cancelled edit leaves no output", async () => { using var ct = new CancellationTokenSource(); ct.Cancel(); try { await Tool(png, "metadata", ct: ct.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { } });
        await Test("Transparent background retains gradient through source", async () => { var file = Path.Combine(input, "transparent.png"); using (var image = new MagickImage(MagickColors.Transparent, 30, 30)) image.Write(file); Track(file); using var result = new MagickImage(await Tool(file, "background", new() { Padding = 10, Radius = 4 })); using var pixels = result.GetPixels(); var p = pixels.GetPixel(25, 25).ToColor()!; Check(p.R > 100 && p.G < 230, "Transparent center turned opaque"); });
        await Test("Brightness and saturation change actual pixels", async () => { using var bright = new MagickImage(await Tool(png, "edit", new() { Brightness = 50, Saturation = 0 })); using var pixels = bright.GetPixels(); var p = pixels.GetPixel(50, 50).ToColor()!; Check(Math.Abs(p.R - p.G) <= 1 && Math.Abs(p.G - p.B) <= 1 && p.R < 100, "Photo adjustments did not apply"); });
        var tiff = Path.Combine(input, "multipage.tiff"); using (var images = new MagickImageCollection()) { images.Add(new MagickImage(MagickColors.Red, 40, 30)); images.Add(new MagickImage(MagickColors.Blue, 40, 30)); images.Write(tiff); }
        Track(tiff);
        foreach (var id in new[] { "metadata", "compress" }) await Test("Multipage TIFF " + id + " retains pages", async () => { using var images = new MagickImageCollection(await Tool(tiff, id)); Check(images.Count == 2, "Pages dropped"); });
        await Test("Multipage editing fails explicitly without page loss", () => Reject(async () => { await Tool(tiff, "crop", new() { Width = 20, Height = 20 }); }));
        await Test("Collage places every selected image", async () => { using var collage = new MagickImage((await Tool(new[] { png, png, png }, "collage"))[0]); Check(collage.Width == 1272 && collage.Height == 1272, "Wrong collage geometry"); using var pixels = collage.GetPixels(); foreach (var point in new[] { (324, 324), (948, 324), (324, 948) }) Check(pixels.GetPixel(point.Item1, point.Item2).ToColor()!.B > 20, "Missing collage image"); });
        await Test("Create PDF tool preserves all selected image pages", async () => { using var pdf = PdfReader.Open((await Tool(new[] { png, tiff }, "createpdf"))[0], PdfDocumentOpenMode.Import); Check(pdf.PageCount == 3, "Missing image pages"); });
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
    }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    async Task Editors()
    {
        var file = Path.Combine(input, "editor.png"); using (var image = new MagickImage("gradient:#FF7700-#1122CC", 160, 120)) image.Write(file); Track(file);
        var video = Path.Combine(input, "editor-video.mp4"); await Ffmpeg("-f", "lavfi", "-i", "testsrc2=size=160x120:duration=2", "-c:v", "libx264", video); Track(video);
        var audio = Path.Combine(input, "editor-audio.wav"); await Ffmpeg("-f", "lavfi", "-i", "sine=frequency=440:duration=2", audio); Track(audio);
        var pdf = Path.Combine(input, "editor.pdf"); Engine.ImagesToPdf(new[] { file }, pdf); Track(pdf);
        Dictionary<string, TextBox> Fields(OptionsWindow w) => (Dictionary<string, TextBox>)typeof(OptionsWindow).GetField("fields", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        foreach (var id in new[] { "crop", "redact", "background", "edit", "annotate", "compress", "trim", "speed", "channels", "snapshot", "split", "organize", "bleep", "collage" }) await Test("Options dialog renders " + id, async () =>
        {
            var source = id == "organize" ? pdf : id is "channels" or "bleep" ? audio : id is "trim" or "speed" or "snapshot" or "split" ? video : file;
            var w = new OptionsWindow(id == "collage" ? new[] { file, file } : new[] { source }, new(id, id, id), settings); try { w.Show(); await Task.Delay(50); await w.Ready.WaitAsync(TimeSpan.FromSeconds(15)); w.UpdateLayout(); Check(w.ActualHeight > 0 && w.PrimaryAction.IsVisible && w.PrimaryAction.IsEnabled && !string.IsNullOrWhiteSpace(w.PrimaryAction.Content?.ToString()), "Dialog is unusable"); if (id is "crop" or "background") App.Capture(w, Path.Combine(directory, id + "-editor.png")); } finally { w.Close(); }
        });
        foreach (var label in new[] { "1:1", "4:3", "16:9" }) await Test("Crop preset " + label + " stays inside image", () =>
        {
            var w = new OptionsWindow(file, new("crop", "CROP", "Crop"), settings); try { w.Show(); w.UpdateLayout(); Descendants<Button>(w).Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var fields = Fields(w); int x = int.Parse(fields["X"].Text), y = int.Parse(fields["Y"].Text), width = int.Parse(fields["Width"].Text), height = int.Parse(fields["Height"].Text); Check(x >= 0 && y >= 0 && x + width <= 160 && y + height <= 120 && width > 0 && height > 0, "Preset is outside image"); } finally { w.Close(); }
            return Task.CompletedTask;
        });
        await Test("Crop aspect presets keep tiny images nonempty", () =>
        {
            var tiny = Path.Combine(input, "tiny.png"); using (var image = new MagickImage(MagickColors.Red, 1, 1)) image.Write(tiny); Track(tiny); var w = new OptionsWindow(tiny, new("crop", "CROP", "Crop"), settings);
            try { w.Show(); w.UpdateLayout(); foreach (var label in new[] { "1:1", "4:3", "16:9" }) { Descendants<Button>(w).Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var f = Fields(w); Check(f["Width"].Text == "1" && f["Height"].Text == "1", "Aspect preset produced an empty rectangle"); } } finally { w.Close(); }
            return Task.CompletedTask;
        });
        await Test("Crop dialog keeps invalid bounds open with validation", () =>
        {
            var w = new OptionsWindow(file, new("crop", "CROP", "Crop"), settings); bool remained = false;
            w.Loaded += (_, _) => w.Dispatcher.BeginInvoke(() => { Fields(w)["X"].Text = "150"; Fields(w)["Width"].Text = "30"; Descendants<Button>(w).Single(b => Equals(b.Content, "Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); remained = w.IsVisible; if (w.IsVisible) w.Close(); });
            w.ShowDialog(); Check(remained, "Out-of-bounds crop closed the editor instead of showing validation"); return Task.CompletedTask;
        });
        await Test("Crop Apply passes exact chosen rectangle", () =>
        {
            var w = new OptionsWindow(file, new("crop", "CROP", "Crop"), settings); w.Loaded += (_, _) => w.Dispatcher.BeginInvoke(() => { var f = Fields(w); f["X"].Text = "12"; f["Y"].Text = "18"; f["Width"].Text = "60"; f["Height"].Text = "40"; Descendants<Button>(w).Single(b => Equals(b.Content, "Apply")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (w.IsVisible) w.Close(); }); Check(w.ShowDialog() == true && w.Options.X == 12 && w.Options.Y == 18 && w.Options.Width == 60 && w.Options.Height == 40, "Apply did not preserve rectangle"); return Task.CompletedTask;
        });
        await Test("Background preview proportions match exported result", async () => { var w = new OptionsWindow(file, new("background", "BG", "Background"), settings); try { w.Show(); await Task.Delay(50); w.UpdateLayout(); using var image = new MagickImage(await Tool(file, "background")); var size = w.BackgroundPreviewSize; Check(Math.Abs(size.Width / size.Height - (double)image.Width / image.Height) < .01, "Preview proportions differ"); } finally { w.Close(); } });
        foreach (var reverse in new[] { false, true }) await Test("Real crop mouse drag " + (reverse ? "reverse" : "forward") + " clamps to image bounds", async () =>
        {
            Native.GetCursorPos(out var original); var w = new OptionsWindow(file, new("crop", "CROP", "Crop"), settings) { Topmost = true };
            try
            {
                w.Show(); w.Activate(); await Task.Delay(50); await w.Ready.WaitAsync(TimeSpan.FromSeconds(10)); await Task.Delay(250); w.UpdateLayout(); var canvas = Descendants<SelectionSurface>(w).Single(); canvas.Reset(); double scale = canvas.Scale;
                var trace = new List<string>(); canvas.MouseLeftButtonDown += (_, e) => trace.Add("Down " + e.GetPosition(canvas)); canvas.MouseMove += (_, e) => trace.Add("Move " + e.GetPosition(canvas) + " " + e.LeftButton); canvas.MouseLeftButtonUp += (_, e) => trace.Add("Up " + e.GetPosition(canvas));
                var begin = canvas.PointToScreen(new Point((reverse ? 140 : 20) * scale, (reverse ? 100 : 20) * scale)); var end = canvas.PointToScreen(new Point(reverse ? -10 : canvas.Width + 10, reverse ? -10 : canvas.Height + 10));
                SetCursorPos((int)begin.X, (int)begin.Y); await Task.Delay(150); SetCursorPos((int)begin.X, (int)begin.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150); SetCursorPos((int)((begin.X + end.X) / 2), (int)((begin.Y + end.Y) / 2)); await Task.Delay(100); SetCursorPos((int)end.X, (int)end.Y); await Task.Delay(150); SetCursorPos((int)end.X, (int)end.Y); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                var f = Fields(w); int x = int.Parse(f["X"].Text), y = int.Parse(f["Y"].Text), width = int.Parse(f["Width"].Text), height = int.Parse(f["Height"].Text);
                File.WriteAllLines(Path.Combine(directory, "crop-drag-" + (reverse ? "reverse" : "forward") + ".txt"), trace);
                Check(x >= 0 && y >= 0 && x + width <= 160 && y + height <= 120 && width > 1 && height > 1, $"Mouse drag produced {x},{y} {width}x{height}; expected a rectangle inside 160x120"); Check(reverse ? x == 0 && y == 0 : x > 15 && y > 15 && x + width == 160 && y + height == 120, $"Mouse drag did not reach boundary: {x},{y} {width}x{height}, scale {scale}");
            }
            finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); w.Close(); SetCursorPos(original.X, original.Y); }
        });
        await Test("Video crop dialog initializes actual source dimensions", async () =>
        {
            var w = new OptionsWindow(video, new("crop", "CROP", "Crop"), settings); try { w.Show(); await Task.Delay(50); var apply = Descendants<Button>(w).Single(b => Equals(b.Content, "Apply")); var watch = Stopwatch.StartNew(); while (!apply.IsEnabled && watch.ElapsedMilliseconds < 5000) await Task.Delay(50); var f = Fields(w); Check(apply.IsEnabled && f["Width"].Text == "160" && f["Height"].Text == "120", "Hardcoded video crop exceeds source size"); } finally { w.Close(); }
        });
        await Test("Short media trim dialog initializes actual duration", async () =>
        {
            var w = new OptionsWindow(video, new("trim", "TRIM", "Trim"), settings); try { w.Show(); await Task.Delay(50); var apply = Descendants<Button>(w).Single(b => Equals(b.Content, "Apply")); var watch = Stopwatch.StartNew(); while (!apply.IsEnabled && watch.ElapsedMilliseconds < 5000) await Task.Delay(50); Check(apply.IsEnabled && double.Parse(Fields(w)["Duration"].Text, CultureInfo.InvariantCulture) == 2, "Default duration exceeds source"); } finally { w.Close(); }
        });
        await Test("Batch menus show only tools supported by every file", () =>
        {
            foreach (var selection in new[] { new[] { "image.png", "image.svg" }, new[] { "movie.mp4", "animation.gif" } }) { var items = Catalog.ToolsForSelection(selection); Check(items.Count > 0 && items.All(item => selection.All(p => Catalog.Tools(p, selection.Length).Any(x => x.Id == item.Id))), "Unsupported tool offered"); }
            return Task.CompletedTask;
        });
        await Test("File actions navigation returns to the selected file category", () =>
        {
            var w = new MainWindow(app); try { w.Show(); w.SetFiles(new[] { file }); w.UpdateLayout(); Descendants<Button>(w).Single(b => Equals(b.Content, "↗   File actions")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var list = (ListBox)w.FindName("CategoryList"); Check(list.SelectedItem == null, "Category remains selected while viewing actions"); list.SelectedIndex = 0; Check(((TextBlock)w.FindName("Heading")).Text == "Image tools", "Cannot return to image tools"); } finally { w.Hide(); }
            return Task.CompletedTask;
        });
    }
}
