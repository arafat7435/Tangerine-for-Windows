using System.Windows.Interop;
using System.Windows.Threading;
namespace Tangerine;

// The global key hook cannot inspect another application's OLE data object.
// This tiny, nonactivating target asks OLE for the current drag payload before
// showing any wheel. A held mouse button or a text drag never authorizes it.
internal sealed class FileDragProbe : Window
{
    internal static Action<string>? Diagnostic;
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    readonly Action<string[]> confirmed;
    readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromMilliseconds(20) };
    readonly long started = Environment.TickCount64;
    bool finished;
    internal FileDragProbe(Action<string[]> confirmed)
    {
        this.confirmed = confirmed;
        Width = 64; Height = 64; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; AllowDrop = true;
        SourceInitialized += (_, _) => ((HwndSource)PresentationSource.FromVisual(this)).AddHook(Message);
        DragEnter += Inspect; DragOver += Inspect;
        Drop += (_, e) => { e.Effects = DragDropEffects.None; e.Handled = true; Close(); };
        Closed += (_, _) => { finished = true; expiry.Stop(); };
        expiry.Tick += (_, _) => { if (Environment.TickCount64 - started > 300 || (Native.GetAsyncKeyState(1) & 0x8000) == 0 || (Native.GetAsyncKeyState(27) & 0x8000) != 0) { Diagnostic?.Invoke("Probe expired"); Close(); } else FollowCursor(); };
    }
    static IntPtr Message(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return (IntPtr)3; } // WM_MOUSEACTIVATE: never take focus.
        return IntPtr.Zero;
    }
    internal void ShowAtCursor()
    {
        Show(); if (finished) return; FollowCursor(); if (finished) return; Diagnostic?.Invoke("Probe shown");
        expiry.Start();
        // Foreign OLE loops may only reevaluate their drop target on mouse input.
        // A zero-distance move also handles a stationary file drag; it does not
        // create a drag or authorize a wheel without the actual OLE payload.
        mouse_event(0x2001, 0, 0, 0, UIntPtr.Zero);
    }
    void FollowCursor() { Native.GetCursorPos(out var point); Native.SetWindowPos(new WindowInteropHelper(this).Handle, (IntPtr)(-1), point.X - 32, point.Y - 32, 64, 64, 0x0010); }
    void Inspect(object sender, DragEventArgs e)
    {
        e.Handled = true; e.Effects = DragDropEffects.None; if (finished) return;
        string[]? files = null;
        try { Diagnostic?.Invoke("OLE formats: " + string.Join(",", e.Data.GetFormats(false))); if (e.Data.GetDataPresent(DataFormats.FileDrop, false) && e.Data.GetData(DataFormats.FileDrop, false) is string[] paths && paths.Length > 0 && paths.All(p => !string.IsNullOrWhiteSpace(p))) files = paths; }
        catch (COMException) { }
        catch (InvalidOperationException) { }
        finished = true; Close();
        if (files != null && (Native.GetAsyncKeyState(1) & 0x8000) != 0) { e.Effects = DragDropEffects.Copy; confirmed(files); }
    }
}
