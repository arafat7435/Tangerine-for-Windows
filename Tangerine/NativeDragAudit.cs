using System.Windows.Interop;
namespace Tangerine;
public static class NativeDragAudit
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    public static async Task Run(App app, string directory, List<object> reports, bool actionsOnly = false)
    {
        Native.GetCursorPos(out var original); var saved = new AppSettings(); saved.CopyFrom(app.Settings); app.Settings.OutputDirectory = directory;
        var fixture = Path.Combine(directory, "drag-fixture.png");
        var source = new Window { Title = "Tangerine interaction audit", Width = 270, Height = 150, Left = 80, Top = 80, Topmost = true }; var surface = new Border { Background = Brushes.WhiteSmoke, Padding = new Thickness(25), Child = new TextBlock { Text = "Testing file gestures…", FontSize = 18 } }; source.Content = surface;
        var trace = new System.Collections.Concurrent.ConcurrentQueue<string>();
        OverlayWindow.Diagnostic = message => trace.Enqueue(message);
        TaskCompletionSource<(WheelMode Mode, Point Point)>? target = null; int activeSector = 0;
        using var hook = new GlobalDragHook(app.Settings, (files, mode) =>
        {
            trace.Enqueue("Hook: " + mode + " · " + files.Length + " files"); app.ShowWheel(files, mode, true);
            var overlay = app.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible); if (mode == WheelMode.Convert && !File.Exists(Path.Combine(directory, "floating-wheel.png"))) App.Capture(overlay, Path.Combine(directory, "floating-wheel.png")); target?.TrySetResult((mode, overlay.TestScreenPoint(activeSector)));
        });
        async Task Scenario(string name, byte key, WheelMode expected, int sector, bool remap = false, bool escape = false, bool disabled = false)
        {
            app.Jobs.Clear(); app.Settings.ConvertKey = remap ? "Ctrl" : "Shift"; app.Settings.ToolsKey = remap ? "Alt" : "Ctrl"; app.Settings.ActionsKey = remap ? "Shift" : "Alt"; app.Settings.GlobalDragEnabled = !disabled;
            target = new(TaskCreationOptions.RunContinuationsAsynchronously); var activeTarget = target; activeSector = sector; var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); trace.Enqueue(name);
            try
            {
                source.Show(); source.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(source).Handle); await Task.Delay(150); var start = surface.PointToScreen(new Point(60, 30));
                void Begin() { trace.Enqueue("OLE begin"); began.TrySetResult(); var effect = DragDrop.DoDragDrop(surface, new DataObject(DataFormats.FileDrop, new[] { fixture }), DragDropEffects.Copy); trace.Enqueue("OLE end: " + effect); }
                await Task.Run(async () =>
                {
                    try
                    {
                        SetCursorPos((int)start.X, (int)start.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100); _ = app.Dispatcher.BeginInvoke(Begin); await began.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(150);
                        keybd_event(key, 0, 0, UIntPtr.Zero); await Task.Delay(60); keybd_event(key, 0, 2, UIntPtr.Zero); await Task.Delay(300);
                        if (disabled) { if (activeTarget.Task.IsCompleted) throw new Exception("Disabled hook triggered a wheel"); return; }
                        var result = await activeTarget.Task.WaitAsync(TimeSpan.FromSeconds(3)); if (result.Mode != expected) throw new Exception("Wrong wheel mode: " + result.Mode); var point = result.Point; trace.Enqueue("Target: " + point);
                        SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(250);
                        if (escape) { keybd_event(0x1B, 0, 0, UIntPtr.Zero); await Task.Delay(80); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
                        SetCursorPos((int)point.X, (int)point.Y); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    }
                    finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
                });
                // Metadata now opens its review panel before removing tags.
                if (expected == WheelMode.Tools && sector == 1 && !escape && !disabled)
                {
                    var ready = Stopwatch.StartNew(); OptionsWindow? editor;
                    do { editor = app.Windows.OfType<OptionsWindow>().FirstOrDefault(w => w.IsVisible); if (editor == null) await Task.Delay(50); } while (editor == null && ready.ElapsedMilliseconds < 3000);
                    if (editor == null) throw new Exception("Metadata drop did not open its panel");
                    await editor.Ready; editor.PrimaryAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                var watch = Stopwatch.StartNew();
                if (sector < 0 || escape || disabled) { await Task.Delay(500); if (app.Jobs.Count != 0 || app.Windows.OfType<OverlayWindow>().Any(w => w.IsVisible)) throw new Exception("Cancelled drag performed work or left an overlay"); }
                else if (expected == WheelMode.Actions) { while (!app.Windows.OfType<ShelfWindow>().Any() && watch.ElapsedMilliseconds < 4000) await Task.Delay(100); if (!app.Windows.OfType<ShelfWindow>().Any()) throw new Exception("Float action did not open a shelf"); }
                else { while (!app.Jobs.Any(j => j.Status.StartsWith("Done")) && watch.ElapsedMilliseconds < 10000) await Task.Delay(100); var job = app.Jobs.FirstOrDefault(); if (job?.Output == null || !File.Exists(job.Output)) throw new Exception("Drop did not produce an output"); using var image = new ImageMagick.MagickImage(job.Output); if (image.Width != 80 || image.Height != 60) throw new Exception("Drop output is invalid"); if (expected == WheelMode.Convert && Catalog.Extension(job.Output) != "jpg") throw new Exception("Wrong conversion selected"); }
                reports.Add(new { name, passed = true });
            }
            catch (Exception ex) { reports.Add(new { name, passed = false, error = ex.ToString() }); }
            finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); foreach (var w in app.Windows.OfType<OverlayWindow>().ToArray()) w.Close(); foreach (var w in app.Windows.OfType<ShelfWindow>().ToArray()) w.Close(); foreach (var w in app.Windows.OfType<OptionsWindow>().ToArray()) w.Close(); File.WriteAllText(Path.Combine(directory, "interaction-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })); }
        }
        try
        {
            if (!actionsOnly) await Scenario("Native stationary drag → Shift wheel → key release → JPG output", 0xA0, WheelMode.Convert, 0);
            await Scenario("Native Ctrl drag opens tools and strips image metadata", 0xA2, WheelMode.Tools, 1);
            await Scenario("Native Alt drag opens actions and floats the file", 0xA4, WheelMode.Actions, 1);
            if (actionsOnly) await Scenario("Remapped Shift drag opens actions and floats the file", 0xA0, WheelMode.Actions, 1, remap: true);
            else await Scenario("Remapped Ctrl drag opens conversions and writes JPG", 0xA2, WheelMode.Convert, 0, remap: true);
            await Scenario("Dropping in the wheel center cancels without output", 0xA2, WheelMode.Tools, -1);
            await Scenario("Escape cancels the native file drag", 0xA2, WheelMode.Tools, 1, escape: true);
            if (actionsOnly && Process.GetProcessesByName("Tangerine").Any(p => p.Id != Environment.ProcessId && p.MainWindowTitle == App.DisplayName)) reports.Add(new { name = "Disabled global drag hook leaves the gesture alone", skipped = true, reason = "Another running Tangerine instance has its own hook; left it untouched." });
            else await Scenario("Disabled global drag hook leaves the gesture alone", 0xA2, WheelMode.Tools, 1, disabled: true);
        }
        finally { OverlayWindow.Diagnostic = null; source.Close(); SetCursorPos(original.X, original.Y); app.Settings.CopyFrom(saved); app.Jobs.Clear(); File.WriteAllLines(Path.Combine(directory, "native-drag-trace.txt"), trace); }
    }
}
