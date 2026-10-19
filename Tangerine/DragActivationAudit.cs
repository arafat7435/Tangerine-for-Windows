using System.Windows.Interop;
namespace Tangerine;

internal static class DragActivationAudit
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    internal static async Task Run(App app, string directory, List<object> reports)
    {
        Native.GetCursorPos(out var cursor);
        var settings = new AppSettings(); var text = new TextBox { Text = "Normal text selection must keep its modifier keys.", Margin = new Thickness(15), AcceptsReturn = true };
        var window = new Window { Title = "Tangerine file-drag activation check", Content = text, Left = 60, Top = 60, Width = 390, Height = 160, Topmost = true };
        var calls = new List<(string[] Files, WheelMode Mode)>(); var keys = new HashSet<Key>();
        var trace = new List<string>(); window.PreviewKeyDown += (_, e) => { var key = e.Key == Key.System ? e.SystemKey : e.Key; keys.Add(key); trace.Add("Input: " + key); };
        FileDragProbe.Diagnostic = trace.Add;
        using var hook = new GlobalDragHook(settings, (files, mode) => { calls.Add((files, mode)); trace.Add("Confirmed: " + mode + " " + string.Join(" | ", files)); });
        var first = Path.Combine(directory, "drag-guard-first.txt"); var second = Path.Combine(directory, "drag-guard-second.txt"); File.WriteAllText(first, "first"); File.WriteAllText(second, "second");
        async Task Case(string name, byte key, bool held = false, bool move = false, bool ole = false, bool fileData = false, bool multiple = false, bool preHeld = false, bool enabled = true)
        {
            var before = calls.Count; keys.Clear(); settings.GlobalDragEnabled = enabled; trace.Add(name);
            try
            {
                window.Show(); window.Activate(); Native.SetForegroundWindow(new WindowInteropHelper(window).Handle); text.Focus(); text.Select(0, 0); await Task.Delay(100);
                var start = text.PointToScreen(new Point(30, 18)); var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var paths = multiple ? new[] { first, second } : new[] { first };
                await Task.Run(async () =>
                {
                    try
                    {
                        mouse_event(4, 0, 0, 0, UIntPtr.Zero); SetCursorPos((int)start.X, (int)start.Y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                        if (preHeld) { keybd_event(key, 0, 0, UIntPtr.Zero); await Task.Delay(80); }
                        if (held || ole) { mouse_event(2, 0, 0, 0, UIntPtr.Zero); await Task.Delay(70); }
                        if (ole)
                        {
                            _ = app.Dispatcher.BeginInvoke(() => { begun.TrySetResult(); var data = fileData ? new DataObject(DataFormats.FileDrop, paths) : new DataObject(DataFormats.UnicodeText, "text, not a file"); DragDrop.DoDragDrop(text, data, DragDropEffects.Copy); });
                            await begun.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(100);
                        }
                        if (move) { SetCursorPos((int)start.X + 75, (int)start.Y + 6); await Task.Delay(70); }
                        if (!preHeld) keybd_event(key, 0, 0, UIntPtr.Zero);
                        await Task.Delay(70);
                        if (!preHeld) keybd_event(key, 0, 2, UIntPtr.Zero);
                        await Task.Delay(500);
                    }
                    finally { if (ole) { keybd_event(0x1B, 0, 0, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); } mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
                });
                await Task.Delay(100);
                bool expected = ole && fileData && !preHeld && enabled;
                if (calls.Count - before != (expected ? 1 : 0)) throw new InvalidDataException($"Expected {(expected ? 1 : 0)} file-drag activations; got {calls.Count - before}.");
                if (expected && (!calls[^1].Files.SequenceEqual(paths) || calls[^1].Mode != (key == 0xA0 ? WheelMode.Convert : key == 0xA2 ? WheelMode.Tools : WheelMode.Actions))) throw new InvalidDataException("File selection or wheel mode was lost.");
                if (!ole && !keys.Contains(KeyInterop.KeyFromVirtualKey(key))) throw new InvalidDataException("The modifier key was swallowed outside a file drag.");
                if (app.Windows.OfType<FileDragProbe>().Any(p => p.IsVisible)) throw new InvalidDataException("An expired probe was left open.");
                reports.Add(new { name = "Drag activation: " + name, passed = true });
            }
            catch (Exception ex) { reports.Add(new { name = "Drag activation: " + name, passed = false, error = ex.ToString() }); }
            finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); foreach (var probe in app.Windows.OfType<FileDragProbe>().ToArray()) probe.Close(); File.WriteAllLines(Path.Combine(directory, "drag-activation-trace.txt"), trace); File.WriteAllText(Path.Combine(directory, "drag-activation-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })); }
        }
        try
        {
            foreach (var (key, name) in new[] { ((byte)0xA0, "Shift"), ((byte)0xA2, "Ctrl"), ((byte)0xA4, "Alt") })
            {
                await Case(name + " alone stays with the active application", key);
                await Case(name + " with a held mouse button does not activate", key, held: true);
                await Case(name + " during ordinary text selection does not activate", key, held: true, move: true);
                await Case(name + " during a text OLE drag does not activate", key, ole: true);
                await Case(name + " during a stationary file OLE drag activates", key, ole: true, fileData: true);
            }
            await Case("A multi-file OLE drag preserves all paths", 0xA0, ole: true, fileData: true, multiple: true);
            await Case("A modifier already held before the drag does not auto-activate", 0xA0, ole: true, fileData: true, preHeld: true);
            await Case("Disabled activation does not react to a file drag", 0xA0, ole: true, fileData: true, enabled: false);
            await Case("No stale file-drag state survives a completed drag", 0xA0, held: true);
            await ExternalSource();
        }
        finally { FileDragProbe.Diagnostic = null; window.Close(); SetCursorPos(cursor.X, cursor.Y); }
        async Task ExternalSource()
        {
            const string name = "Drag activation: a separate Windows Forms process supplies a real multi-file OLE payload";
            var script = Path.Combine(directory, "external-drag-source.ps1");
            File.WriteAllText(script, """
param([string]$FirstFile,[string]$SecondFile)
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
public static class DragSource {
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")]static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 public static void Run(string[] files) {
  using(var form=new Form {Text="Tangerine external file drag check",Width=280,Height=140,Left=90,Top=90,StartPosition=FormStartPosition.Manual,TopMost=true})
  using(var timer=new System.Windows.Forms.Timer {Interval=250}) {
   form.Shown+=(s,e)=>timer.Start();
   timer.Tick+=(s,e)=>{timer.Stop();var point=form.PointToScreen(new System.Drawing.Point(60,45));SetCursorPos(point.X,point.Y);mouse_event(2,0,0,0,UIntPtr.Zero);};
   form.MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;Console.WriteLine("OLE begin");
    ThreadPool.QueueUserWorkItem(state=>{try{Thread.Sleep(150);keybd_event(0xA0,0,0,UIntPtr.Zero);Thread.Sleep(80);keybd_event(0xA0,0,2,UIntPtr.Zero);Thread.Sleep(500);}finally{keybd_event(0x1B,0,0,UIntPtr.Zero);keybd_event(0x1B,0,2,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}});
    try{Console.WriteLine("OLE end: "+form.DoDragDrop(new DataObject(DataFormats.FileDrop,files),DragDropEffects.Copy));}finally{mouse_event(4,0,0,0,UIntPtr.Zero);form.Close();}
   };Application.Run(form);
  }
 }
}
'@
[DragSource]::Run(@($FirstFile,$SecondFile))
""");
            var before = calls.Count; settings.GlobalDragEnabled = true; window.Hide();
            try
            {
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var argument in new[] { "-NoProfile", "-STA", "-NonInteractive", "-File", script, first, second }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("External drag source did not start.");
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } catch { if (!process.HasExited) process.Kill(true); throw; }
                var error = await process.StandardError.ReadToEndAsync(); var log = await process.StandardOutput.ReadToEndAsync(); trace.Add(log);
                if (process.ExitCode != 0 || calls.Count != before + 1 || !calls[^1].Files.SequenceEqual(new[] { first, second }) || calls[^1].Mode != WheelMode.Convert) throw new InvalidDataException($"External file drag: exit {process.ExitCode}, confirmations {calls.Count - before}. " + error + " " + string.Join("; ", trace.TakeLast(10)));
                reports.Add(new { name, passed = true });
            }
            catch (Exception ex) { reports.Add(new { name, passed = false, error = ex.ToString() }); }
            finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(0xA0, 0, 2, UIntPtr.Zero); keybd_event(0x1B, 0, 2, UIntPtr.Zero); File.WriteAllLines(Path.Combine(directory, "drag-activation-trace.txt"), trace); File.WriteAllText(Path.Combine(directory, "drag-activation-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true })); }
        }
    }
}
