using System.Security.Cryptography;
using System.Windows.Interop;
namespace Tangerine;

internal static class WheelSoundAudit
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    internal static async Task Run(App app, string directory, List<object> reports)
    {
        if (!WheelSound.Available) { reports.Add(new { name = "Wheel sound: optional private sound asset", skipped = true, reason = "The public build excludes the reference sound. Supply a licensed clip locally to run playback checks." }); return; }
        async Task Test(string name, Func<Task> run) { try { await run(); reports.Add(new { name = "Wheel sound: " + name, passed = true }); } catch (Exception ex) { reports.Add(new { name = "Wheel sound: " + name, passed = false, error = ex.ToString() }); } }
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        int Count() => app.WheelSound.PlaybackCount;
        var enabled = app.Settings.WheelSoundsEnabled; Native.GetCursorPos(out var pointer);
        var wheel = new RadialWheel(); var window = new Window { Title = "Tangerine sound check", Content = wheel, SizeToContent = SizeToContent.WidthAndHeight, Left = 80, Top = 80, Topmost = true };
        var trace = new List<string>();
        wheel.MouseMove += (_, e) => trace.Add($"{Environment.TickCount64} move {e.GetPosition(wheel)} selected={wheel.SelectedIndex} sounds={Count()}");
        wheel.MouseLeave += (_, _) => trace.Add($"{Environment.TickCount64} leave selected={wheel.SelectedIndex} sounds={Count()}");
        try
        {
            app.Settings.WheelSoundsEnabled = true; SetCursorPos(5, 5);
            await Test("embedded clip matches the original website byte for byte", () =>
            {
                using var clip = typeof(App).Assembly.GetManifestResourceStream("Tangerine.Assets.highlight.wav") ?? throw new IOException("Sound resource missing");
                Check(Convert.ToHexString(SHA256.HashData(clip)) == "606B2829835A38E88A62355C9D7E3F1C2E7699DB6C047CF2AE36FFF6D375E02C", "Sound resource differs"); return Task.CompletedTask;
            });
            await Test("opening the wheel is silent", async () => { var before = Count(); wheel.SetItems(Catalog.Tools("sample.jpg")); window.Show(); await Task.Delay(200); Check(Count() == before, "Initialization played a sound"); });
            await Test("first pointer entry into the preselected block plays a tick", async () =>
            {
                var before = Count(); var entry = new TaskCompletionSource<(int Index, int Sounds)>(TaskCreationOptions.RunContinuationsAsynchronously);
                MouseEventHandler entered = (_, _) => entry.TrySetResult((wheel.SelectedIndex, Count())); wheel.MouseMove += entered;
                try { var point = wheel.PointToScreen(new Point(144, 50)); SetCursorPos((int)point.X, (int)point.Y); var observed = await entry.Task.WaitAsync(TimeSpan.FromSeconds(2)); Check(observed.Index == 0 && observed.Sounds == before + 1, $"First pointer entry selected {observed.Index}; sound count {before} → {observed.Sounds}"); await Task.Delay(140); }
                finally { wheel.MouseMove -= entered; }
            });
            await Test("native pointer transition plays exactly one clip", async () =>
            {
                var before = Count(); var point = wheel.PointToScreen(new Point(144 + 94 * Math.Sin(2 * Math.PI / wheel.Items.Count), 144 - 94 * Math.Cos(2 * Math.PI / wheel.Items.Count)));
                SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(200); Check(wheel.SelectedIndex == 1 && Count() == before + 1, $"Pointer selection={wheel.SelectedIndex}, sounds={before} → {Count()}. " + string.Join("; ", trace.TakeLast(8)));
            });
            await Test("movement within the selected block is silent", async () => { var before = Count(); Native.GetCursorPos(out var p); SetCursorPos(p.X + 1, p.Y + 1); await Task.Delay(120); Check(Count() == before, "Same-block pointer movement repeated the sound"); });
            await Test("leaving through the center and re-entering the block plays one tick", async () =>
            {
                Native.GetCursorPos(out var block); var before = Count(); var center = wheel.PointToScreen(new Point(144, 144)); SetCursorPos((int)center.X, (int)center.Y); await Task.Delay(80);
                SetCursorPos(block.X, block.Y); await Task.Delay(140); Check(Count() == before + 1, $"Re-entry sounds={before} → {Count()}. " + string.Join("; ", trace.TakeLast(8)));
            });
            await Test("native arrow-key navigation plays the same clip", async () =>
            {
                var center = wheel.PointToScreen(new Point(144, 144)); SetCursorPos((int)center.X, (int)center.Y);
                // A center click grants real OS focus without invoking an operation.
                mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                window.Activate(); var handle = new WindowInteropHelper(window).Handle; Native.SetForegroundWindow(handle); Keyboard.Focus(wheel);
                Check(GetForegroundWindow() == handle, "OS foreground focus was not granted to the sound test window");
                var before = Count(); var index = wheel.SelectedIndex;
                keybd_event(0x27, 0x4D, 1, UIntPtr.Zero); await Task.Delay(40); keybd_event(0x27, 0x4D, 3, UIntPtr.Zero); await Task.Delay(150);
                Check(wheel.SelectedIndex == (index + 1) % wheel.Items.Count && Count() == before + 1, $"Keyboard selection {index} → {wheel.SelectedIndex}, native playback {before} → {Count()}, focus: {Keyboard.FocusedElement?.GetType().Name}");
            });
            await Test("center and invalid selections are silent", async () => { var before = Count(); var center = wheel.PointToScreen(new Point(144, 144)); SetCursorPos((int)center.X, (int)center.Y); wheel.Select(-1); wheel.Select(wheel.Items.Count); await Task.Delay(100); Check(Count() == before, "Empty selection played a sound"); });
            await Test("muting applies immediately and re-enabling restores playback", () => { var before = Count(); app.Settings.WheelSoundsEnabled = false; wheel.Select(3); Check(wheel.SelectedIndex == 3 && Count() == before, "Mute failed"); app.Settings.WheelSoundsEnabled = true; wheel.Select(4); Check(Count() == before + 1, "Re-enable failed"); return Task.CompletedTask; });
            await Test("every available conversion, tool and action block uses the clip", async () =>
            {
                foreach (var items in Catalog.Categories.SelectMany(c => new[] { Catalog.Conversions("sample." + c.Sample), Catalog.Tools("sample." + c.Sample) }).Append(Catalog.Actions))
                {
                    var before = Count(); wheel.SetItems(items); Check(Count() == before, "Menu refresh played a sound");
                    for (int i = 1; i < items.Count; i++) { wheel.Select(i); Check(Count() == before + i, "A wheel block failed native playback"); await Task.Delay(16); }
                }
            });
            async Task Drain() { var watch = Stopwatch.StartNew(); while (app.WheelSound.CompletedCount + app.WheelSound.InterruptedCount < Count() && watch.ElapsedMilliseconds < 3000) await Task.Delay(20); Check(app.WheelSound.CompletedCount + app.WheelSound.InterruptedCount == Count(), "Native buffers did not finish"); }
            foreach (var interval in new[] { 16, 8 }) await Test($"48 rapid transitions with requested {interval} ms delays finish all ticks without interruption", async () =>
            {
                await Drain(); var before = Count(); var completed = app.WheelSound.CompletedCount; var interrupted = app.WheelSound.InterruptedCount;
                for (int i = 0; i < 48; i++) { wheel.Select((wheel.SelectedIndex + 1) % wheel.Items.Count); await Task.Delay(interval); }
                await Drain(); Check(Count() == before + 48 && app.WheelSound.CompletedCount == completed + 48 && app.WheelSound.InterruptedCount == interrupted, "Rapid transitions cancelled or skipped an audio buffer");
            });
            await Test("first tick after idle finishes on the audio device", async () => { await Drain(); await Task.Delay(1000); var completed = app.WheelSound.CompletedCount; wheel.Select((wheel.SelectedIndex + 1) % wheel.Items.Count); await Drain(); Check(app.WheelSound.CompletedCount == completed + 1, "First tick after idle did not complete"); });
            await Test("hidden wheels and idle time remain silent", async () => { window.Hide(); var before = Count(); wheel.Select(0); await Task.Delay(250); Check(Count() == before, "Hidden or idle wheel played a sound"); });
            await Test("sound preference survives saving and copying, defaults on for older settings", () =>
            {
                Check(JsonSerializer.Deserialize<AppSettings>("{}")!.WheelSoundsEnabled, "Older settings defaulted to mute"); var settings = new AppSettings { WheelSoundsEnabled = false }; var path = Path.Combine(directory, "sound-settings.json"); settings.Save(path); var loaded = AppSettings.Load(path); Check(!loaded.WheelSoundsEnabled, "Mute preference was not saved"); var copy = new AppSettings(); copy.CopyFrom(loaded); Check(!copy.WheelSoundsEnabled, "Copy dropped mute preference"); settings.WheelSoundsEnabled = true; settings.Save(path); Check(AppSettings.Load(path).WheelSoundsEnabled, "Enabled preference was not saved"); return Task.CompletedTask;
            });
            await Test("native audio buffer stops and disposes safely", () => { using var sound = new WheelSound(); Check(sound.Play(), "Windows rejected native playback"); sound.Dispose(); sound.Dispose(); Check(!sound.Play(), "Disposed player restarted"); return Task.CompletedTask; });
        }
        finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); keybd_event(0x27, 0x4D, 3, UIntPtr.Zero); window.Close(); app.Settings.WheelSoundsEnabled = enabled; SetCursorPos(pointer.X, pointer.Y); File.WriteAllLines(Path.Combine(directory, "sound-input-trace.txt"), trace); }
    }
}
