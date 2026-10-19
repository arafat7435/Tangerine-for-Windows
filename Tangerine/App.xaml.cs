using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
namespace Tangerine;
public partial class App : Application
{
    public const string DisplayName = "Tangerine for Windows";
    internal static string NoticesDirectory => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "notices")) ? Path.Combine(AppContext.BaseDirectory, "notices") : AppContext.BaseDirectory;
    public AppSettings Settings { get; } = AppSettings.Load();
    internal WheelSound WheelSound { get; } = new();
    public ObservableCollection<Job> Jobs { get; } = new();
    public bool Exiting { get; private set; }
    MainWindow? main; Forms.NotifyIcon? tray; GlobalDragHook? hook; OverlayWindow? overlay; Mutex? mutex;
    readonly SemaphoreSlim queue = new(1, 1); readonly CancellationTokenSource lifetime = new(); CancellationTokenSource? current; bool diagnostic;
    public Engine Engine { get; private set; } = null!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); Engine = new Engine(Settings); if (Settings.WheelSoundsEnabled) WheelSound.Prepare();
        diagnostic = e.Args.Any(x => x is "--audit" or "--panel-focus-test" or "--drag-activation-test" or "--stress-test" or "--batch-test" or "--panels-test" or "--conversion-matrix" or "--file-actions-test" or "--self-test" or "--interaction-test" or "--capture");
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Tangerine", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
        if (e.Args.Contains("--panel-focus-test")) { var directory = e.Args.SkipWhile(x => x != "--panel-focus-test").Skip(1).First(); await RunDiagnostic(() => PanelFocusAudit.Run(this, directory, new()), directory, "panel-focus-results.json"); return; }
        if (e.Args.Contains("--drag-activation-test")) { var directory = e.Args.SkipWhile(x => x != "--drag-activation-test").Skip(1).First(); Directory.CreateDirectory(directory); await RunDiagnostic(() => DragActivationAudit.Run(this, directory, new()), directory, "drag-activation-results.json"); return; }
        if (e.Args.Contains("--stress-test")) { var directory = e.Args.SkipWhile(x => x != "--stress-test").Skip(1).First(); await RunDiagnostic(() => StressAudit.Run(this, directory), directory, "stress-results.json"); return; }
        if (e.Args.Contains("--batch-test")) { var directory = e.Args.SkipWhile(x => x != "--batch-test").Skip(1).First(); await RunDiagnostic(() => BatchAudit.Run(this, directory), directory, "batch-results.json"); return; }
        if (e.Args.Contains("--panels-test")) { var directory = e.Args.SkipWhile(x => x != "--panels-test").Skip(1).First(); await RunDiagnostic(() => PanelAudit.Run(this, directory), directory, "panel-results.json"); return; }
        if (e.Args.Contains("--file-actions-test")) { var directory = e.Args.SkipWhile(x => x != "--file-actions-test").Skip(1).First(); var only = e.Args.SkipWhile(x => x != "--only").Skip(1).FirstOrDefault(); await RunDiagnostic(() => FileActionAudit.Run(this, directory, only, e.Args.Contains("--native-drag")), directory, "file-actions-results.json"); return; }
        if (e.Args.Contains("--conversion-matrix")) { var directory = e.Args.SkipWhile(x => x != "--conversion-matrix").Skip(1).First(); var only = e.Args.SkipWhile(x => x != "--only").Skip(1).FirstOrDefault(); await RunDiagnostic(() => ConversionMatrixTest.Run(this, directory, only), directory, "matrix-results.json"); return; }
        if (e.Args.Contains("--audit")) { var directory = e.Args.SkipWhile(x => x != "--audit").Skip(1).First(); try { var passed = await AuditSuite.Run(this, directory, e.Args.Contains("--documents-only")); Shutdown(passed ? 0 : 1); } catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "fatal-error.txt"), ex.ToString()); Shutdown(2); } return; }
        if (e.Args.Contains("--self-test")) { var directory = e.Args.SkipWhile(x => x != "--self-test").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "test-results"); await RunDiagnostic(() => SelfTest.Run(this, directory, e.Args.Contains("--preview-only"), e.Args.Contains("--core-only"), e.Args.Contains("--require-public"), e.Args.Contains("--require-standalone")), directory, "results.json"); return; }
        if (e.Args.Contains("--interaction-test")) { var directory = e.Args.SkipWhile(x => x != "--interaction-test").Skip(1).First(); await RunDiagnostic(() => InteractionTest.Run(this, directory), directory, "interaction-results.json"); return; }
        if (e.Args.Contains("--capture"))
        {
            var file = e.Args.SkipWhile(x => x != "--capture").Skip(1).First(); main = new MainWindow(this); main.Show(); await Task.Delay(450); Capture(main, file); Exiting = true; Shutdown(); return;
        }
        mutex = new Mutex(true, "Local\\TangerineWindows.Native", out bool created);
        if (!created) { var window = Native.FindWindow(null, DisplayName); if (window != IntPtr.Zero) { Native.ShowWindow(window, 9); Native.SetForegroundWindow(window); } Shutdown(); return; }
        try { var path = Path.Combine(AppSettings.DataDirectory, "history.json"); if (File.Exists(path)) foreach (var j in JsonSerializer.Deserialize<List<Job>>(File.ReadAllText(path)) ?? new()) Jobs.Add(j); } catch { }
        main = new MainWindow(this); MainWindow = main;
        tray = new Forms.NotifyIcon { Text = "Tangerine · drag, tap, drop", Icon = MakeIcon(), Visible = true }; var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Tangerine", null, (_, _) => ShowMain()); menu.Items.Add("Settings", null, (_, _) => { ShowMain(); new SettingsWindow(this) { Owner = main }.ShowDialog(); main.Refresh(); });
        menu.Items.Add("Licenses and notices", null, (_, _) => Reveal(NoticesDirectory));
        menu.Items.Add("Quit", null, (_, _) => Exit()); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => ShowMain();
        hook = new GlobalDragHook(Settings, (files, mode) => ShowWheel(files, mode, true));
        if (!e.Args.Contains("--tray")) main.Show();
        var files = e.Args.Where(File.Exists).ToArray(); if (files.Length > 0) ShowWheel(files, WheelMode.Convert);
    }
    public void ShowMain() { main?.Show(); if (main != null) { main.WindowState = WindowState.Normal; main.Activate(); } }
    protected override void OnExit(ExitEventArgs e) { WheelSound.Dispose(); base.OnExit(e); }
    async Task RunDiagnostic(Func<Task> run, string directory, string report)
    {
        try { await run(); using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, report))); Shutdown(json.RootElement.EnumerateArray().Any(test => test.TryGetProperty("passed", out var passed) && !passed.GetBoolean()) ? 1 : 0); }
        catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "fatal-error.txt"), ex.ToString()); Shutdown(2); }
    }
    public void ShowWheel(string[] files, WheelMode mode, bool dragging = false)
    {
        if (overlay is { IsVisible: true }) { overlay.Switch(mode); return; }
        overlay = new OverlayWindow(this, files, mode, dragging); overlay.Closed += (_, _) => overlay = null; overlay.ShowAtCursor();
    }
    public async Task Execute(string[] files, WheelItem item, WheelMode mode, Window? owner = null)
    {
        try
        {
            if (Exiting) return;
            files = Catalog.Selection(files); if (files.Length == 0) throw new ArgumentException("Choose at least one file.");
            if (mode == WheelMode.Actions) { await FileActions.Execute(this, files, item.Id, owner); return; }
            if (files.Any(Directory.Exists)) throw new InvalidOperationException("Choose a file for conversions. Folders can be copied, moved, shared, or kept in the floating shelf.");
            if (!(mode == WheelMode.Convert ? Catalog.ConversionsForSelection(files) : Catalog.ToolsForSelection(files)).Any(x => x.Id == item.Id)) throw new NotSupportedException("This operation is not supported by every selected file.");
            if (files.Length > 1 && !(mode == WheelMode.Tools && Catalog.IsGroup(item.Id)))
            {
                var batch = new BatchWindow(this, files, item, mode); if (owner?.IsVisible == true) batch.Owner = owner; batch.ShowDialog(); return;
            }
            var options = new OperationOptions { Quality = Settings.Quality };
            if (mode == WheelMode.Tools && OptionsWindow.Needed(item.Id))
            {
                var dialog = new OptionsWindow(files, item, Settings); if (owner?.IsVisible == true) dialog.Owner = owner; else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; if (dialog.ShowDialog() != true) return; options = dialog.Options; files = dialog.Inputs;
            }
            var job = new Job { Name = files.Length == 1 ? Path.GetFileName(files[0]) : $"{files.Length} files", Operation = item.Description }; Jobs.Insert(0, job); while (Jobs.Count > 50) Jobs.RemoveAt(Jobs.Count - 1);
            bool acquired = false; try
            {
                await queue.WaitAsync(lifetime.Token); acquired = true; current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                job.Status = "Processing…"; var outputs = await Engine.Run(files, item, mode, options, current.Token); job.Output = outputs.FirstOrDefault(); job.Status = outputs.Count == 1 ? "Done" : "Done · " + outputs.Count + " files";
                tray?.ShowBalloonTip(2500, "Tangerine", $"{item.Description} finished. Your original files are kept.", Forms.ToolTipIcon.Info);
            }
            catch (OperationCanceledException) { job.Status = "Cancelled"; }
            catch (Exception ex) { job.Status = "Failed · " + ex.Message; MessageBox.Show(ex.Message, item.Description, MessageBoxButton.OK, MessageBoxImage.Warning); }
            finally { if (acquired) { current?.Dispose(); current = null; queue.Release(); } SaveHistory(); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Tangerine", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    internal async Task ExecuteBatch(BatchPlan plan, AppSettings settings, CancellationToken cancellation, Action<BatchEntry> changed)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellation); bool acquired = false;
        foreach (var entry in plan.Entries) { Jobs.Insert(0, entry.Job); while (Jobs.Count > 50) Jobs.RemoveAt(Jobs.Count - 1); }
        try
        {
            await queue.WaitAsync(linked.Token); acquired = true; current = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            await BatchRunner.Run(plan, new Engine(settings), settings.Quality, current.Token, changed);
        }
        finally { if (acquired) { current?.Dispose(); current = null; queue.Release(); } SaveHistory(); }
    }
    void SaveHistory() { if (diagnostic) return; try { Directory.CreateDirectory(AppSettings.DataDirectory); File.WriteAllText(Path.Combine(AppSettings.DataDirectory, "history.json"), JsonSerializer.Serialize(Jobs)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    public void CancelCurrent() => current?.Cancel();
    internal void CancelPendingJobs() { lifetime.Cancel(); current?.Cancel(); }
    public new async void Exit()
    {
        if (Exiting) return; Exiting = true; CancelPendingJobs(); hook?.Dispose(); overlay?.Close();
        await queue.WaitAsync(); queue.Release(); tray?.Dispose(); mutex?.Dispose(); Shutdown();
    }
    public static void Reveal(string path)
    {
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; if (Directory.Exists(path)) info.ArgumentList.Add(path); else { info.ArgumentList.Add("/select,"); info.ArgumentList.Add(path); }
        System.Diagnostics.Process.Start(info);
    }
    public static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content; int w = (int)content.ActualWidth, h = (int)content.ActualHeight; var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, w, h)); dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, w, h)); }
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var stream = File.Create(path); encoder.Save(stream);
    }
    static System.Drawing.Icon MakeIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32); using var g = System.Drawing.Graphics.FromImage(bitmap); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(System.Drawing.Color.Transparent); using var orange = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 89, 0)); g.FillEllipse(orange, 2, 3, 28, 28); using var white = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(white, 16 + (float)Math.Cos(a) * 5, 17 + (float)Math.Sin(a) * 5, 16 + (float)Math.Cos(a) * 10, 17 + (float)Math.Sin(a) * 10); }
        var h = bitmap.GetHicon(); using var native = System.Drawing.Icon.FromHandle(h); var result = (System.Drawing.Icon)native.Clone(); Native.DestroyIcon(h); return result;
    }
}
