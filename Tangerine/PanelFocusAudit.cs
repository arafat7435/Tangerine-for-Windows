using System.Windows.Interop;
namespace Tangerine;

internal static class PanelFocusAudit
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    internal static async Task Run(App app, string directory, List<object> reports)
    {
        Directory.CreateDirectory(directory); Native.GetCursorPos(out var cursor);
        var saved = new AppSettings(); saved.CopyFrom(app.Settings);
        app.Settings.EngineDirectory = Environment.GetEnvironmentVariable("TANGERINE_ENGINE_DIR") ?? app.Settings.EngineDirectory;
        app.Settings.ConvertKey = "Shift"; app.Settings.ToolsKey = "Ctrl"; app.Settings.ActionsKey = "Alt"; app.Settings.GlobalDragEnabled = true;
        var first = Path.Combine(directory, "photo.png"); var second = Path.Combine(directory, "second.png"); var gif = Path.Combine(directory, "animation.gif");
        using (var image = new ImageMagick.MagickImage("gradient:#FFA32B-#427C56", 320, 240)) { image.Write(first); image.Flop(); image.Write(second); image.Write(gif); }
        var audio = Path.Combine(directory, "sound.wav");
        await Engine.Process(app.Engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", audio }, CancellationToken.None);
        var script = Path.Combine(directory, "panel-drag-source.ps1"); File.WriteAllText(script, SourceScript);
        string targetFile = "", operation = "";
        using var hook = new GlobalDragHook(app.Settings, (files, mode) =>
        {
            app.ShowWheel(files, mode, true);
            var items = mode == WheelMode.Convert ? Catalog.ConversionsForSelection(files) : Catalog.ToolsForSelection(files);
            var index = items.ToList().FindIndex(item => item.Id == operation);
            if (index < 0) throw new InvalidOperationException("Requested wheel operation is missing: " + operation);
            var overlay = app.Windows.OfType<OverlayWindow>().Single(w => w.IsVisible); var point = overlay.TestScreenPoint(index);
            File.WriteAllText(targetFile, $"{(int)point.X},{(int)point.Y}");
        });
        async Task WaitUntil(Func<bool> condition, string error, int milliseconds = 6000)
        {
            var watch = Stopwatch.StartNew(); while (!condition()) { if (watch.ElapsedMilliseconds > milliseconds) throw new TimeoutException(error); await Task.Delay(30); }
        }
        static void RequireForeground(Window window)
        {
            var expected = new WindowInteropHelper(window).Handle; var actual = GetForegroundWindow();
            if (expected != actual || !window.IsActive) throw new InvalidDataException($"Panel opened behind the source: expected HWND {expected}, foreground HWND {actual}, IsActive={window.IsActive}.");
            if (window.Topmost) throw new InvalidDataException("A regular editor was left permanently on top.");
        }
        static void Escape() { keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); }
        async Task Case(string name, string[] files, string op, WheelMode mode, bool switchAway = false, bool nested = false, bool restoreSource = false, bool switchImmediately = false)
        {
            var caseDirectory = Path.Combine(directory, name); Directory.CreateDirectory(caseDirectory); targetFile = Path.Combine(caseDirectory, "target.txt"); operation = op;
            var trace = new List<string>(); WindowActivation.Diagnostic = message => trace.Add(Environment.TickCount64 + " " + message);
            OverlayWindow.Diagnostic = message => trace.Add(message);
            var done = Path.Combine(caseDirectory, "dropped.txt"); var stop = Path.Combine(caseDirectory, "stop.txt"); Process? process = null;
            try
            {
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "-NoProfile", "-STA", "-NonInteractive", "-File", script, caseDirectory, files[0], files.Length > 1 ? files[1] : "", mode == WheelMode.Convert ? "160" : "162" }) start.ArgumentList.Add(arg);
                process = Process.Start(start) ?? throw new InvalidOperationException("External source did not start.");
                await WaitUntil(() => File.Exists(done), "The external file drag did not finish.", 15000);
                if (File.ReadAllText(done) != "Copy") throw new InvalidDataException("The external drag ended without dropping on a wheel block.");
                MiniWindow? panel = null; await WaitUntil(() => { panel = app.Windows.OfType<MiniWindow>().FirstOrDefault(w => w.IsVisible); return panel != null; }, "Drop did not open a panel.");
                if (restoreSource || switchImmediately)
                {
                    await WaitUntil(() => GetForegroundWindow() == new WindowInteropHelper(panel!).Handle, "The panel never acquired foreground focus."); await Task.Delay(30);
                    if (restoreSource) Native.SetForegroundWindow(new IntPtr(long.Parse(File.ReadAllText(Path.Combine(caseDirectory, "source-hwnd.txt")))));
                    else { SetCursorPos(80, 150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
                    await Task.Delay(350);
                    if (switchImmediately) { GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess); if (foregroundProcess != (uint)process.Id) throw new InvalidDataException("Delayed activation stole focus after the user switched windows."); panel!.Close(); reports.Add(new { name = "Panel foreground: " + name, passed = true }); return; }
                }
                await Task.Delay(350); RequireForeground(panel!);
                if (panel is OptionsWindow editor) await editor.Ready.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(350); RequireForeground(panel!);
                if (nested)
                {
                    var button = Descendants<Button>(panel!).Single(b => Equals(b.Content, "Settings for all…"));
                    _ = app.Dispatcher.BeginInvoke(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    OptionsWindow? child = null; await WaitUntil(() => { child = app.Windows.OfType<OptionsWindow>().FirstOrDefault(w => w.IsVisible); return child != null; }, "Batch settings panel did not open.");
                    await Task.Delay(350); RequireForeground(child!);
                    if (child!.Owner != panel) throw new InvalidDataException("Batch settings lost their owner.");
                    Escape(); await WaitUntil(() => !child.IsVisible, "Escape did not close the active settings panel."); await Task.Delay(200); RequireForeground(panel!);
                }
                App.Capture(panel!, Path.Combine(caseDirectory, "panel.png"));
                if (switchAway)
                {
                    // A user click outside the editor must be allowed to return
                    // to the source; no recurring activation or topmost latch.
                    SetCursorPos(80, 150); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(650);
                    GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
                    if (foregroundProcess != (uint)process.Id) throw new InvalidDataException("The panel stole focus back after switching to the source.");
                    panel!.Close();
                }
                else { Escape(); await WaitUntil(() => !panel!.IsVisible, "Escape did not reach the panel without an extra click."); }
                reports.Add(new { name = "Panel foreground: " + name, passed = true });
            }
            catch (Exception ex) { reports.Add(new { name = "Panel foreground: " + name, passed = false, error = ex.ToString() }); }
            finally
            {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(0xA0, 0, 2, UIntPtr.Zero); keybd_event(0xA2, 0, 2, UIntPtr.Zero); Escape();
                foreach (var window in app.Windows.OfType<MiniWindow>().Reverse().ToArray()) window.Close(); foreach (var window in app.Windows.OfType<OverlayWindow>().ToArray()) window.Close();
                File.WriteAllText(stop, "stop");
                if (process != null) { try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); } File.WriteAllText(Path.Combine(caseDirectory, "source-log.txt"), (await process.StandardOutput.ReadToEndAsync()) + (await process.StandardError.ReadToEndAsync())); process.Dispose(); }
                File.WriteAllText(Path.Combine(directory, "panel-focus-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
                WindowActivation.Diagnostic = null; OverlayWindow.Diagnostic = null; File.WriteAllLines(Path.Combine(caseDirectory, "focus-trace.txt"), trace);
            }
        }
        try
        {
            await Case("gif-metadata", new[] { gif }, "metadata", WheelMode.Tools, switchAway: true);
            await Case("image-compress", new[] { first }, "compress", WheelMode.Tools);
            await Case("image-crop", new[] { first }, "crop", WheelMode.Tools);
            await Case("audio-metadata", new[] { audio }, "metadata", WheelMode.Tools);
            await Case("audio-trim", new[] { audio }, "trim", WheelMode.Tools);
            await Case("multiple-convert", new[] { first, second }, "jpg", WheelMode.Convert);
            await Case("multiple-compress-and-settings", new[] { first, second }, "compress", WheelMode.Tools, nested: true);
            await Case("combined-create-pdf", new[] { first, second }, "createpdf", WheelMode.Tools);
            await Case("source-restores-focus-after-drop", new[] { first }, "compress", WheelMode.Tools, restoreSource: true);
            await Case("user-switches-during-opening", new[] { first }, "compress", WheelMode.Tools, switchImmediately: true);
        }
        finally { app.Settings.CopyFrom(saved); SetCursorPos(cursor.X, cursor.Y); }
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var descendant in Descendants<T>(child)) yield return descendant; }
    }
    const string SourceScript = """
param([string]$Directory,[string]$First,[string]$Second,[int]$Key)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
public static class PanelDragSource {
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")]static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 public static void Run(string directory,string[] files,byte key) {
  using(var form=new Form {Text="Tangerine external panel focus check",WindowState=FormWindowState.Maximized,TopMost=true})
  using(var timer=new System.Windows.Forms.Timer {Interval=200}) {
   bool begun=false;
   form.Shown+=(s,e)=>{File.WriteAllText(Path.Combine(directory,"source-hwnd.txt"),form.Handle.ToInt64().ToString());timer.Start();};
   timer.Tick+=(s,e)=>{
    if(File.Exists(Path.Combine(directory,"stop.txt"))){form.Close();return;}
    if(begun)return;begun=true;var point=form.PointToScreen(new System.Drawing.Point(150,120));SetCursorPos(point.X,point.Y);mouse_event(2,0,0,0,UIntPtr.Zero);
   };
   bool dragged=false;
   form.MouseDown+=(s,e)=>{
    if(dragged||e.Button!=MouseButtons.Left)return;dragged=true;form.TopMost=false;Console.WriteLine("OLE begin");
    ThreadPool.QueueUserWorkItem(state=>{
     try{
      Thread.Sleep(150);keybd_event(key,0,0,UIntPtr.Zero);Thread.Sleep(80);keybd_event(key,0,2,UIntPtr.Zero);
      var target=Path.Combine(directory,"target.txt");var until=DateTime.UtcNow.AddSeconds(7);
      while(!File.Exists(target)&&DateTime.UtcNow<until)Thread.Sleep(20);
      var xy=File.ReadAllText(target).Split(',');SetCursorPos(int.Parse(xy[0]),int.Parse(xy[1]));Thread.Sleep(200);SetCursorPos(int.Parse(xy[0]),int.Parse(xy[1]));
     }catch(Exception error){Console.WriteLine(error);keybd_event(0x1B,0,0,UIntPtr.Zero);keybd_event(0x1B,0,2,UIntPtr.Zero);}
     finally{keybd_event(key,0,2,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
    });
    var effect=form.DoDragDrop(new DataObject(DataFormats.FileDrop,files),DragDropEffects.Copy);
    Console.WriteLine("OLE end: "+effect);File.WriteAllText(Path.Combine(directory,"dropped.txt"),effect.ToString());
   };
   Application.Run(form);
  }
 }
}
'@
$files = if ($Second) { @($First,$Second) } else { @($First) }
[PanelDragSource]::Run($Directory,$files,[byte]$Key)
""";
}
