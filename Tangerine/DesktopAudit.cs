using Clipboard = System.Windows.Clipboard;
namespace Tangerine;
public static class DesktopAudit
{
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    public static async Task Run(App app, string directory, List<object> reports, bool actionsOnly = false)
    {
        async Task Test(string name, Func<Task> action) { try { await action(); reports.Add(new { name, passed = true }); } catch (Exception ex) { reports.Add(new { name, passed = false, error = ex.ToString() }); } }
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
        Task Reject(Action action) { try { action(); } catch (ArgumentException) { return Task.CompletedTask; } catch (IOException) { return Task.CompletedTask; } throw new Exception("Invalid operation succeeded."); }
        string Fixture(string name, string text = "file-action fixture") { var path = Path.Combine(directory, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path; }
        var settingsFile = Path.Combine(directory, "test-settings.json");
        await Test("Settings save, reload and remapped shortcuts", () =>
        {
            var settings = new AppSettings { OutputDirectory = directory, ConvertKey = "Alt", ToolsKey = "Shift", ActionsKey = "Ctrl", Quality = 71 }; settings.Save(settingsFile); var loaded = AppSettings.Load(settingsFile);
            Check(loaded.OutputDirectory == directory && loaded.ConvertKey == "Alt" && loaded.ToolsKey == "Shift" && loaded.ActionsKey == "Ctrl" && loaded.Quality == 71, "Settings did not round trip"); return Task.CompletedTask;
        });
        await Test("Invalid settings preserve last saved settings", async () =>
        {
            var original = File.ReadAllText(settingsFile); await Reject(() => new AppSettings { ConvertKey = "Ctrl", ToolsKey = "Ctrl" }.Save(settingsFile)); await Reject(() => new AppSettings { OutputDirectory = "relative/path" }.Save(settingsFile));
            await Reject(() => new AppSettings { Quality = 0 }.Save(settingsFile)); await Reject(() => new AppSettings { EngineDirectory = directory }.Save(settingsFile)); Check(File.ReadAllText(settingsFile) == original, "Invalid save replaced valid settings");
        });
        await Test("Corrupt settings fall back to valid defaults", () => { var file = Fixture("broken-settings.json", "{ broken"); var loaded = AppSettings.Load(file); loaded.Validate(); Check(loaded.Quality == 82, "Defaults incorrect"); return Task.CompletedTask; });
        var saved = new AppSettings(); saved.CopyFrom(app.Settings);
        MainWindow? window = null;
        try
        {
            app.Settings.ConvertKey = "Alt"; app.Settings.ToolsKey = "Shift"; app.Settings.ActionsKey = "Ctrl"; app.Settings.OutputDirectory = directory;
            await Test("Main UI shows saved key mappings, output folder and existing history", () =>
            {
                app.Jobs.Add(new Job { Name = "History fixture", Status = "Done" }); window = new MainWindow(app); window.Show(); window.SetFiles(new[] { Fixture("ui-fixture.txt") }); window.UpdateLayout();
                Check(((TextBlock)window.FindName("ConvertKey")).Text == "alt", "Wrong conversion key"); Check(((TextBlock)window.FindName("ToolsKey")).Text == "shift", "Wrong tools key"); Check(((TextBlock)window.FindName("SelectedDetail")).Text.Contains(directory), "Wrong output location"); Check(((TextBlock)window.FindName("EmptyHistory")).Visibility == Visibility.Collapsed, "Empty history shown for existing jobs"); return Task.CompletedTask;
            });
            if (!actionsOnly) await Test("Every file category builds a usable wheel", () =>
            {
                foreach (var category in Catalog.Categories) { window!.SetFiles(new[] { "sample." + category.Sample }); var wheel = (RadialWheel)window.FindName("ConvertWheel"); Check(wheel.Items.Count > 0 && wheel.Selected != null, "No conversion wheel for " + category.Name); }
                return Task.CompletedTask;
            });
            await Test("Mixed selection retains common tools with its configured shortcut", () =>
            {
                window!.SetFiles(new[] { "sample.jpg", "sample.mp3" }); Check(((TextBlock)window.FindName("Heading")).Text == "Selected files", "Mixed selection not handled");
                var tools = (RadialWheel)window.FindName("ToolsWheel"); Check(tools.Items.Any(x => x.Id == "metadata") && !tools.Items.Any(x => x.Id == "crop"), "Mixed selection tools are incorrect"); Check(((TextBlock)window.FindName("ToolsKey")).Text == "shift", "Tools shortcut incorrect"); return Task.CompletedTask;
            });
            await Test("Wheel rejects center, outer area and empty menus", () =>
            {
                var wheel = new RadialWheel(); wheel.SetItems(Catalog.Actions); Check(wheel.Hit(new Point(144, 144)) == -1 && wheel.Hit(new Point(0, 0)) == -1, "Unsafe wheel target"); wheel.SetItems(Array.Empty<WheelItem>()); Check(wheel.Selected == null && wheel.Hit(new Point(144, 50)) == -1, "Empty wheel remained actionable"); return Task.CompletedTask;
            });
            await Test("Floating shelf keeps files and opens without taking ownership", async () =>
            {
                var fixture = Fixture("shelf-fixture.txt"); await FileActions.Execute(app, new[] { fixture }, "float", window); var shelf = app.Windows.OfType<ShelfWindow>().Single(); Check(shelf.IsVisible && shelf.Topmost && shelf.AllowDrop, "Shelf is not usable"); shelf.Close(); Check(File.Exists(fixture), "Closing shelf removed original");
            });
            // Materialize the clipboard before testing and restore it only if no
            // other application/user has changed it since our fixture was copied.
            var old = Clipboard.GetDataObject(); var snapshot = new DataObject(); bool snapshotOk = true;
            try { if (old != null) foreach (var format in old.GetFormats(false)) { var data = old.GetData(format, false); if (data == null) { snapshotOk = false; break; } snapshot.SetData(format, data, false); } } catch { snapshotOk = false; }
            if (snapshotOk)
            {
                uint sequence = 0; try
                {
                    await Test("Copy paths writes exact absolute paths", async () => { var files = new[] { Fixture("clipboard one.txt"), Fixture("clipboard two.txt") }; await FileActions.Execute(app, files, "copypath", window); sequence = GetClipboardSequenceNumber(); Check(Clipboard.GetText() == string.Join(Environment.NewLine, files), "Wrong clipboard paths"); });
                    await Test("Copy files creates a native Explorer file-drop list", async () => { var files = new[] { Fixture("copy-one.txt"), Fixture("copy-two.txt") }; await FileActions.Execute(app, files, "copy", window); sequence = GetClipboardSequenceNumber(); Check(Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files), "Wrong file-drop clipboard"); });
                }
                finally { if (sequence != 0 && GetClipboardSequenceNumber() == sequence) { if (old == null) Clipboard.Clear(); else Clipboard.SetDataObject(snapshot, true); } }
            }
            else reports.Add(new { name = "Clipboard integration", skipped = true, reason = "Current clipboard could not be fully preserved; left untouched." });
        }
        finally { window?.Hide(); app.Settings.CopyFrom(saved); app.Jobs.Clear(); }
        var destination = Path.Combine(directory, "move-destination"); Directory.CreateDirectory(destination);
        await Test("Move preserves data and avoids filename collisions", () =>
        {
            File.WriteAllText(Path.Combine(destination, "same.txt"), "keep destination"); var one = Fixture("move-source-a/same.txt", "one"); var two = Fixture("move-source-b/same.txt", "two"); var results = FileActions.MoveFiles(new[] { one, two, one }, destination);
            Check(results.Count == 2 && results.Distinct().Count() == 2 && !File.Exists(one) && !File.Exists(two), "Sources not moved once"); Check(File.ReadAllText(results[0]) == "one" && File.ReadAllText(results[1]) == "two" && File.ReadAllText(Path.Combine(destination, "same.txt")) == "keep destination", "Move lost or overwrote content"); return Task.CompletedTask;
        });
        await Test("Move preflights missing sources before moving any file", async () => { var valid = Fixture("preflight.txt"); await Reject(() => FileActions.MoveFiles(new[] { valid, Path.Combine(directory, "missing-file.txt") }, destination)); Check(File.Exists(valid), "Partial move occurred"); });
        await Test("Move rejects nested destinations and overlapping selections", async () =>
        {
            var child = Fixture("move-folder/child.txt"); var folder = Path.GetDirectoryName(child)!; await Reject(() => FileActions.MoveFiles(new[] { folder }, folder)); await Reject(() => FileActions.MoveFiles(new[] { folder, child }, destination)); Check(File.Exists(child), "Rejected move changed the source");
        });
        await Test("Move folder preserves its complete directory tree", () => { var child = Fixture("folder-tree/nested/leaf.txt"); var folder = Path.Combine(directory, "folder-tree"); var target = FileActions.MoveFiles(new[] { folder }, destination)[0]; Check(!Directory.Exists(folder) && File.ReadAllText(Path.Combine(target, "nested", "leaf.txt")) == "file-action fixture", "Directory move lost data"); return Task.CompletedTask; });
        await Test("Terminal opens PowerShell in the selected file folder", async () =>
        {
            var fixture = Fixture("terminal folder/selected.txt"); var script = Fixture("terminal-check.ps1", "param([string]$ResultPath)\n[System.IO.File]::WriteAllText($ResultPath, (Get-Location).Path)"); var result = Path.Combine(directory, "terminal-location.txt");
            var start = FileActions.TerminalInfo(fixture); start.WindowStyle = ProcessWindowStyle.Hidden;
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, result }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new Exception("Terminal failed to start"); try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } catch { if (!process.HasExited) process.Kill(true); throw; }
            Check(process.ExitCode == 0 && File.ReadAllText(result) == Path.GetDirectoryName(fixture), "Terminal opened in the wrong directory");
        });
        await Test("Quitting cancels current and queued jobs with no output", async () =>
        {
            var fixture = Path.Combine(directory, "queue-source.png"); using (var im = new ImageMagick.MagickImage("gradient:#FF7700-#1122EE", 2000, 2000)) im.Write(fixture);
            if (actionsOnly) { fixture = Path.Combine(directory, "queue-source.mp4"); var fixtureEngine = new Engine(new() { EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory }); await Engine.Process(fixtureEngine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=160x120:duration=2", "-c:v", "libx264", fixture }, CancellationToken.None); }
            var before = app.Settings.OutputDirectory; app.Settings.OutputDirectory = directory;
            try { var item = actionsOnly ? new WheelItem("mute", "MUTE", "Mute") : new WheelItem("avif", "AVIF", "Convert to AVIF"); var mode = actionsOnly ? WheelMode.Tools : WheelMode.Convert; var first = app.Execute(new[] { fixture }, item, mode); var second = app.Execute(new[] { fixture }, item, mode); app.CancelPendingJobs(); await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20)); Check(app.Jobs.Count == 2 && app.Jobs.All(j => j.Status == "Cancelled"), "Pending jobs continued after cancellation"); Check(!Directory.GetFiles(directory, actionsOnly ? "queue-source Mute*" : "queue-source*.avif").Any() && !Directory.GetDirectories(directory, ".tangerine-*").Any(), "Cancelled work left output"); }
            finally { app.Settings.OutputDirectory = before; }
        });
    }
}
