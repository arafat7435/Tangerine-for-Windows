using System.Windows.Interop;
namespace Tangerine;

internal static class WindowActivation
{
    internal static Action<string>? Diagnostic;
    [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);

    internal static async Task OnFirstRender(Window window)
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() }; var haveInput = GetLastInputInfo(ref input);
        BringForward(window);
        // The foreign OLE loop can restore its source after the first render.
        // Finish that handoff once, without an idle activation loop. A newer
        // user input or a newly opened app dialog cancels this delayed attempt.
        await Task.Delay(120);
        if (!window.IsVisible) return;
        var latest = new LastInput { Size = input.Size }; var foreground = GetForegroundWindow(); GetWindowThreadProcessId(foreground, out var process);
        var handle = new WindowInteropHelper(window).Handle;
        if (foreground == handle || process == (uint)Environment.ProcessId) return;
        if (!haveInput || !GetLastInputInfo(ref latest) || latest.Time != input.Time) { Diagnostic?.Invoke("Deferred activation cancelled after new input"); return; }
        Diagnostic?.Invoke("Completing OLE focus handoff"); BringForward(window);
    }
    static void BringForward(Window window)
    {
        if (!window.IsVisible) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !IsWindowEnabled(handle)) return;
        Diagnostic?.Invoke($"Activate {window.Title}: target {handle}, foreground {GetForegroundWindow()}");
        Native.SetForegroundWindow(handle);
        if (GetForegroundWindow() == handle) { window.Activate(); return; }

        // Explorer can remain foreground after its OLE drag loop returns even
        // though WPF reports the new dialog as active. Briefly join the input
        // queues for this opening gesture so native focus follows the panel.
        // Detach immediately; never leave editors topmost.
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId(); bool attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread) attached = AttachThreadInput(currentThread, foregroundThread, true);
            Native.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0003); // HWND_TOP, no move/resize.
            Native.SetForegroundWindow(handle);
            window.Activate();
        }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
        Diagnostic?.Invoke($"Activation finished: foreground {GetForegroundWindow()}, attached={attached}");
    }
}
