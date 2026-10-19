using ImageMagick;
using System.Reflection;
using System.Security.Cryptography;
using PdfSharp.Pdf.IO;
namespace Tangerine;

internal static class PanelAudit
{
    static readonly List<string> nativeTrace = new();
    internal static async Task Run(App app, string directory)
    {
        Directory.CreateDirectory(directory); nativeTrace.Clear(); var output = Path.Combine(directory, "outputs"); Directory.CreateDirectory(output);
        var settings = new AppSettings { OutputDirectory = output, EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory }; var engine = new Engine(settings); var reports = new List<object>();
        async Task Test(string name, Func<Task> action) { nativeTrace.Add(name); try { await action(); reports.Add(new { name, passed = true }); } catch (Exception ex) { reports.Add(new { name, passed = false, error = ex.ToString() }); } File.WriteAllLines(Path.Combine(directory, "native-pointer-trace.txt"), nativeTrace); File.WriteAllText(Path.Combine(directory, "panel-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })); }
        void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        var photo = Path.Combine(directory, "Oranges.png"); var source = Environment.GetEnvironmentVariable("TANGERINE_PANEL_PHOTO");
        using (var image = source != null ? new MagickImage(source) : new MagickImage("gradient:#FFA32B-#427C56", 420, 380)) { image.SetAttribute("comment", "Panel metadata fixture"); var profile = new ExifProfile(); profile.SetValue(ExifTag.Artist, "Tangerine panel audit"); profile.SetValue(ExifTag.ImageDescription, "Oranges in the sunshine"); image.SetProfile(profile); image.Write(photo); }
        var second = Path.Combine(directory, "Oranges detail.png"); using (var image = new MagickImage(photo)) { image.Flop(); image.Write(second); }
        var audio = Path.Combine(directory, "Voice with pauses.wav"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-af", "volume=enable='between(t,0,0.6)+between(t,1.8,2.4)+between(t,3.6,4)':volume=0", audio }, CancellationToken.None);
        var video = Path.Combine(directory, "Oranges.mp4"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-loop", "1", "-i", photo, "-i", audio, "-vf", "scale=380:346", "-t", "4", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", video }, CancellationToken.None);
        var pdf = Path.Combine(directory, "Oranges.pdf"); Engine.ImagesToPdf(new[] { photo, second }, pdf);
        var cases = new List<(string Name, string[] Files, string Op)>{
            ("image-compress",new[]{photo},"compress"),("metadata",new[]{photo},"metadata"),("edit-photo",new[]{photo},"edit"),("annotate-photo",new[]{photo},"annotate"),("background",new[]{photo},"background"),("crop-image",new[]{photo},"crop"),("redact-photo",new[]{photo},"redact"),("collage",new[]{photo,second,photo},"collage"),
            ("trim-video",new[]{video},"trim"),("crop-video",new[]{video},"crop"),("video-speed",new[]{video},"speed"),("join-videos",new[]{video,video},"join"),("snapshots",new[]{video},"snapshot"),("split-video",new[]{video},"split"),("redact-video",new[]{video},"redact"),
            ("normalize",new[]{audio},"normalize"),("visualizer",new[]{audio},"visualize"),("trim-audio",new[]{audio},"trim"),("channels",new[]{audio},"channels"),("bleep",new[]{audio},"bleep"),("merge-pdf",new[]{pdf,pdf},"merge"),("organize-pdf",new[]{pdf},"organize"),("split-pdf",new[]{pdf},"split"),("pdf-compress",new[]{pdf},"compress"),("audio-metadata",new[]{audio},"metadata"),("video-compress",new[]{video},"compress")};
        var hashes = new[] { photo, second, audio, video, pdf }.ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        var screenshots = Path.Combine(directory, "screenshots"); Directory.CreateDirectory(screenshots);
        foreach (var test in cases)
        {
            OptionsWindow? window = null;
            await Test("Panel: " + test.Name, async () =>
            {
                window = new OptionsWindow(test.Files, new(test.Op, test.Op, test.Op), settings); window.Show(); await Task.Delay(80); await window.Ready.WaitAsync(TimeSpan.FromSeconds(45)); await Task.Delay(120); window.UpdateLayout();
                Require(window.PrimaryAction.IsEnabled, "Primary action did not become available"); Require(window.ActualHeight <= SystemParameters.WorkArea.Height, "Panel exceeds the working area");
                Require(window.WindowStyle == WindowStyle.None, "System title bar is still present");
                App.Capture(window, Path.Combine(screenshots, test.Name + ".png"));
            });
            if (window != null)
            {
                try
                {
                    if (window.PrimaryAction.IsEnabled) await Test("Export from panel: " + test.Name, async () =>
                    {
                        var options = window.ReadOptions(); if (test.Op == "trim") { options.Start = .5; options.Duration = 1.5; }
                        if (test.Op == "split" && Catalog.Kind(test.Files[0]) == FileKind.Video) options.SplitPoints = new() { 1, 2.5 }; if (test.Op == "snapshot") options.SnapshotTimes = new() { .5, 2 }; if (test.Op == "channels") { options.Channels = 1; options.LeftVolume = 50; options.RightVolume = 80; }
                        if (test.Op == "normalize") options.TargetLoudness = -20; if (test.Op == "bleep") options.BleepRanges = new() { new(.5, 1), new(2, 2.5) };
                        var files = await engine.Run(window.Inputs, new(test.Op, test.Op, test.Op), WheelMode.Tools, options, CancellationToken.None); Require(files.Count > 0 && files.All(p => File.Exists(p) || Directory.Exists(p)), "No output created");
                        if (test.Op == "snapshot") Require(Directory.GetFiles(files[0], "*.png").Length == 2, "Selected frames were not saved");
                        if (test.Op == "split" && Catalog.Kind(test.Files[0]) == FileKind.Video) Require(Directory.GetFiles(files[0], "*.mp4").Length == 3, "Split markers were not used");
                        if (test.Op == "organize") { using var doc = PdfReader.Open(files[0], PdfDocumentOpenMode.Import); Require(doc.PageCount == 2, "Organizer dropped a page"); }
                        if (Catalog.Kind(files[0]) == FileKind.Image) { using var decoded = new MagickImage(files[0]); Require(decoded.Width > 0 && decoded.Height > 0, "Empty image"); }
                        if (Catalog.Kind(files[0]) is FileKind.Video or FileKind.Audio) { await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", files[0], "-f", "null", "-" }, CancellationToken.None); }
                    });
                }
                finally { window.Close(); }
            }
        }
        await Test("Crop handles and sliders update exported options", async () =>
        {
            var window = new OptionsWindow(photo, new("crop", "crop", "crop"), settings); try { window.Show(); await Task.Delay(60); await window.Ready; var surface = (SelectionSurface)typeof(OptionsWindow).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; surface.SetSelection(new Rect(25, 30, 160, 120)); var o = window.ReadOptions(); Require(o.X == 25 && o.Y == 30 && o.Width == 160 && o.Height == 120, "Selection is not wired to output"); var thumb = Descendants<System.Windows.Controls.Primitives.Thumb>(surface).First(); Require(Descendants<System.Windows.Controls.Primitives.Thumb>(surface).Count() == 8, "Eight selection handles are missing"); var file = (await engine.Run(new[] { photo }, new("crop", "crop", "crop"), WheelMode.Tools, o, CancellationToken.None))[0]; using var result = new MagickImage(file); Require(result.Width == 160 && result.Height == 120, "Wrong exported crop dimensions"); } finally { window.Close(); }
        });
        await Test("Photo sliders change the preview and saved image", async () =>
        {
            var window = new OptionsWindow(photo, new("edit", "edit", "edit"), settings); try { window.Show(); await Task.Delay(60); await window.Ready; var slider = Descendants<Slider>(window).Single(s => AutomationProperties.GetName(s) == "Brightness"); slider.Value = 55; await Task.Delay(250); Require(window.ReadOptions().Brightness == 55, "Slider did not update output options"); var file = (await engine.Run(new[] { photo }, new("edit", "edit", "edit"), WheelMode.Tools, window.ReadOptions(), CancellationToken.None))[0]; using var a = new MagickImage(photo); using var b = new MagickImage(file); Require(!a.ToByteArray(MagickFormat.Rgb).SequenceEqual(b.ToByteArray(MagickFormat.Rgb)), "Sliders did not alter saved pixels"); } finally { window.Close(); }
        });
        await Test("PDF page cards rotate, duplicate and reset", async () =>
        {
            var window = new OptionsWindow(pdf, new("organize", "organize", "organize"), settings); try { window.Show(); await Task.Delay(60); await window.Ready; Click(window, "Rotate page"); Click(window, "Duplicate page"); Require(window.Options.PageEdits.Count == 3 && window.Options.PageEdits[0].Rotation == 90, "Page buttons are disconnected"); var file = (await engine.Run(new[] { pdf }, new("organize", "organize", "organize"), WheelMode.Tools, window.ReadOptions(), CancellationToken.None))[0]; using var doc = PdfReader.Open(file, PdfDocumentOpenMode.Import); Require(doc.PageCount == 3 && doc.Pages[0].Rotate == 90, "Page card edits did not export"); Click(window, "Reset"); Require(window.Options.PageEdits.Count == 2 && window.Options.PageEdits.All(p => p.Rotation == 0), "Reset did not restore the page list"); } finally { window.Close(); }
        });
        await Test("File-order controls update the merge inputs", async () => { var window = new OptionsWindow(new[] { photo, second }, new("createpdf", "pdf", "pdf"), settings); try { window.Show(); await Task.Delay(60); var down = Descendants<Button>(window).First(b => AutomationProperties.GetName(b) == "Move file down"); down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.Inputs[0] == second, "File order did not change"); } finally { window.Close(); } });
        await Test("Repeated Add Frame clicks keep one selection and the correct save count", async () => { var window = new OptionsWindow(video, new("snapshot", "snapshot", "snapshot"), settings); try { window.Show(); await Task.Delay(60); await window.Ready; Click(window, "+  Add Frame"); Click(window, "+  Add Frame"); Require(window.ReadOptions().SnapshotTimes.Count == 1 && Equals(window.PrimaryAction.Content, "Save 1 Frame"), "Duplicate frame count is misleading"); var outputs = await engine.Run(new[] { video }, new("snapshot", "snapshot", "snapshot"), WheelMode.Tools, window.ReadOptions(), CancellationToken.None); Require(Directory.GetFiles(outputs.Single(), "*.png").Length == 1, "Displayed frame count differs from output"); } finally { window.Close(); } });
        await Test("Real mouse resizes an orange crop handle", async () =>
        {
            Native.GetCursorPos(out var cursor); var window = new OptionsWindow(photo, new("crop", "crop", "crop"), settings) { Topmost = true };
            try { window.Show(); await Task.Delay(100); await window.Ready; await Task.Delay(200); window.UpdateLayout(); var surface = (SelectionSurface)typeof(OptionsWindow).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; surface.SetSelection(new Rect(25, 30, 160, 120)); window.UpdateLayout(); var handle = Descendants<System.Windows.Controls.Primitives.Thumb>(surface).Single(t => Equals(t.Tag, "SE")); var start = handle.PointToScreen(new Point(4, 4)); await Drag(start, new Point(start.X + 35, start.Y + 30)); var options = window.ReadOptions(); Require(options.Width > 160 && options.Height > 120, "Real handle drag did not resize selection"); }
            finally { MouseEvent(4, 0, 0, 0, UIntPtr.Zero); window.Close(); SetCursorPos(cursor.X, cursor.Y); }
        });
        await Test("Real mouse adjusts the trim range", async () =>
        {
            Native.GetCursorPos(out var cursor); var window = new OptionsWindow(audio, new("trim", "trim", "trim"), settings) { Topmost = true };
            try { window.Show(); await Task.Delay(100); await window.Ready; window.UpdateLayout(); var timeline = (RangeTimeline)typeof(OptionsWindow).GetField("timeline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; timeline.SetRange(.5, 3, true); var start = timeline.PointToScreen(new Point(timeline.ActualWidth * .5 / 4, timeline.ActualHeight / 2)); await Drag(start, new Point(start.X + 35, start.Y)); Require(window.ReadOptions().Start > .6, "Trim handle did not update the exported start time"); }
            finally { MouseEvent(4, 0, 0, 0, UIntPtr.Zero); window.Close(); SetCursorPos(cursor.X, cursor.Y); }
        });
        foreach (var vertical in new[] { true, false }) await Test("Crop selection spanning the full " + (vertical ? "width moves vertically" : "height moves horizontally"), async () =>
        {
            Native.GetCursorPos(out var cursor); var window = new OptionsWindow(photo, new("crop", "crop", "crop"), settings) { Topmost = true };
            try { window.Show(); await Task.Delay(100); await window.Ready; await Task.Delay(200); window.UpdateLayout(); var surface = (SelectionSurface)typeof(OptionsWindow).GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; var original = vertical ? new Rect(0, 20, surface.PixelWidth, surface.PixelHeight / 3) : new Rect(20, 0, surface.PixelWidth / 3, surface.PixelHeight); surface.SetSelection(original); var start = surface.PointToScreen(new Point((original.X + original.Width / 2) * surface.Scale, (original.Y + original.Height / 2) * surface.Scale)); await Drag(start, new Point(start.X + (vertical ? 0 : 25), start.Y + (vertical ? 25 : 0))); var options = window.ReadOptions(); Require(options.Width == original.Width && options.Height == original.Height && (vertical ? options.Y > 20 && options.X == 0 : options.X > 20 && options.Y == 0), "Moving a full-span selection drew a new rectangle instead"); }
            finally { MouseEvent(4, 0, 0, 0, UIntPtr.Zero); window.Close(); SetCursorPos(cursor.X, cursor.Y); }
        });
        foreach (var playback in new[] { (File: video, Op: "trim"), (File: audio, Op: "normalize") }) await Test("Native playback: " + playback.Op, async () =>
        {
            var window = new OptionsWindow(playback.File, new(playback.Op, playback.Op, playback.Op), settings);
            try { window.Show(); await Task.Delay(100); await window.Ready; var media = (MediaPreview)typeof(OptionsWindow).GetField("media", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!; var player = (MediaElement)typeof(MediaPreview).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(media)!; player.Volume = 0; if (playback.Op == "normalize") await media.UseProcessedAudio("normalize", window.ReadOptions()); else media.PlayRange(.2, 3.5); var watch = Stopwatch.StartNew(); while (media.Position < .5 && watch.ElapsedMilliseconds < 6000) await Task.Delay(100); Require(media.IsPlaying && media.Position >= .5, "Native preview did not play"); }
            finally { window.Close(); }
        });
        await Test("Styled dropdown changes the compression size", async () => { var window = new OptionsWindow(photo, new("compress", "compress", "compress"), settings); try { window.Show(); await Task.Delay(100); var combo = Descendants<ComboBox>(window).Single(); combo.IsDropDownOpen = true; await Task.Delay(60); combo.SelectedIndex = 2; combo.IsDropDownOpen = false; Require(window.ReadOptions().MaxDimension == 1920, "Dropdown selection did not update output size"); } finally { window.Close(); } });
        await Test("Escape closes the mini panel", async () => { var window = new OptionsWindow(photo, new("compress", "compress", "compress"), settings); try { window.Show(); await Task.Delay(60); window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent }); Require(!window.IsVisible, "Escape did not close the panel"); } finally { window.Close(); } });
        await Test("Compression size applies to every TIFF page", async () => { var path = Path.Combine(directory, "two-pages.tiff"); using (var pages = new MagickImageCollection()) { pages.Add(new MagickImage(photo)); pages.Add(new MagickImage(second)); pages.Write(path); } var result = (await engine.Run(new[] { path }, new("compress", "compress", "compress"), WheelMode.Tools, new() { MaxDimension = 128 }, CancellationToken.None))[0]; using var decoded = new MagickImageCollection(result); Require(decoded.Count == 2 && decoded.All(p => p.Width <= 128 && p.Height <= 128), "Multi-page compression ignored the selected size"); });
        foreach (var shape in new[] { "Arrow", "Rectangle", "Ellipse" }) await Test("Annotation shape: " + shape, async () => { var file = (await engine.Run(new[] { photo }, new("annotate", "annotate", "annotate"), WheelMode.Tools, new() { AnnotationShape = shape, X = 20, Y = 30, Width = 160, Height = 120, Color = "#FFFFFF" }, CancellationToken.None))[0]; using var before = new MagickImage(photo); using var after = new MagickImage(file); Require(!before.ToByteArray(MagickFormat.Rgb).SequenceEqual(after.ToByteArray(MagickFormat.Rgb)), "The annotation shape did not change pixels"); });
        await Test("Blurred photo redaction preserves pixels outside the selected area", async () => { var file = (await engine.Run(new[] { photo }, new("redact", "redact", "redact"), WheelMode.Tools, new() { BlurRedaction = true, X = 20, Y = 30, Width = 101, Height = 83 }, CancellationToken.None))[0]; using var before = new MagickImage(photo); using var after = new MagickImage(file); using var a = before.GetPixels(); using var b = after.GetPixels(); Require(a.GetPixel(0, 0).ToColor()!.Equals(b.GetPixel(0, 0).ToColor()), "Blur changed pixels outside the selection"); Require(!before.ToByteArray(MagickFormat.Rgb).SequenceEqual(after.ToByteArray(MagickFormat.Rgb)), "Blur did not alter the selected region"); });
        await Test("Blurred video redaction exports playable media", async () => { var file = (await engine.Run(new[] { video }, new("redact", "redact", "redact"), WheelMode.Tools, new() { BlurRedaction = true, X = 20, Y = 30, Width = 101, Height = 83 }, CancellationToken.None))[0]; await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", file, "-f", "null", "-" }, CancellationToken.None); });
        await Test("Floating shelf uses the same panel layout", async () => { var window = new ShelfWindow(new[] { photo, pdf }); try { window.Show(); await Task.Delay(100); App.Capture(window, Path.Combine(screenshots, "floating-shelf.png")); Require(window.WindowStyle == WindowStyle.None, "Shelf has old window chrome"); } finally { window.Close(); } });
        await Test("Originals remain unchanged", () => { Require(hashes.All(p => p.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p.Key)))), "A source was changed"); return Task.CompletedTask; });
        File.WriteAllText(Path.Combine(directory, "complete.json"), JsonSerializer.Serialize(new { completed = true, checks = reports.Count, ApplicationDllSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))) }));
    }
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] static extern void MouseEvent(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    static async Task Drag(Point start, Point end)
    {
        void Trace(string step) { Native.GetCursorPos(out var p); nativeTrace.Add($"{step}: actual {p.X},{p.Y}; start {start}; end {end}; capture {GetCapture()}; left {(Native.GetAsyncKeyState(1) & 0x8000) != 0}"); }
        SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(150); Trace("Before down"); MouseEvent(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(150); Trace("After down"); SetCursorPos((int)((start.X + end.X) / 2), (int)((start.Y + end.Y) / 2)); await Task.Delay(150); Trace("Midpoint"); SetCursorPos((int)end.X, (int)end.Y); await Task.Delay(150); Trace("Before up"); MouseEvent(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100); Trace("After up");
    }
    static void Click(Window w, string name) => Descendants<Button>(w).First(b => AutomationProperties.GetName(b) == name || Equals(b.Content, name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T value) yield return value; foreach (var x in Descendants<T>(child)) yield return x; } }
}
