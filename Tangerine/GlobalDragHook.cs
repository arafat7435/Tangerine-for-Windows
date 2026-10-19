using System.Windows.Interop;
namespace Tangerine;
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int cmd);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
}
public sealed class GlobalDragHook : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr msg, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    const int ProbeDrag = 0x8052, CancelProbe = 0x8053;
    readonly HookProc callback; readonly AppSettings settings; readonly Action<string[], WheelMode> trigger; readonly HashSet<int> pressed = new(); readonly HwndSource messages; IntPtr handle; bool disposed; FileDragProbe? probe; int request; long requestedAt;
    public GlobalDragHook(AppSettings settings, Action<string[], WheelMode> trigger)
    {
        this.settings = settings; this.trigger = trigger;
        // OLE's nested drag loop can postpone normal Dispatcher work until the
        // pointer moves. A native message works during a stationary drag and
        // keeps window creation outside the low-level keyboard hook.
        messages = new HwndSource(new HwndSourceParameters("Tangerine drag messages") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }); messages.AddHook(Message);
        callback = Hook; handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0); if (handle == IntPtr.Zero) { messages.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    IntPtr Message(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if (message == CancelProbe) { handled = true; probe?.Close(); probe = null; }
        if (message == ProbeDrag)
        {
            handled = true;
            if (lparam.ToInt32() != request || Environment.TickCount64 - requestedAt > 400 || !settings.GlobalDragEnabled || (Native.GetAsyncKeyState(1) & 0x8000) == 0) return IntPtr.Zero;
            probe?.Close(); var ticket = request; var mode = (WheelMode)wparam.ToInt32();
            var current = new FileDragProbe(files => { if (!disposed && ticket == request && settings.GlobalDragEnabled && (Native.GetAsyncKeyState(1) & 0x8000) != 0) trigger(files, mode); });
            probe = current; current.Closed += (_, _) => { if (ReferenceEquals(probe, current)) probe = null; }; current.ShowAtCursor();
        }
        return IntPtr.Zero;
    }
    static string? KeyName(int key) => key is 16 or 160 or 161 ? "Shift" : key is 17 or 162 or 163 ? "Ctrl" : key is 18 or 164 or 165 ? "Alt" : null;
    IntPtr Hook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int key = Marshal.ReadInt32(data); int msg = message.ToInt32(); bool down = msg is 0x100 or 0x104, up = msg is 0x101 or 0x105;
            if (up) pressed.Remove(key);
            if (down && key == 27) { request++; Send(CancelProbe, IntPtr.Zero, IntPtr.Zero); }
            var name = KeyName(key);
            if (down && name != null && pressed.Add(key) && settings.GlobalDragEnabled && (Native.GetAsyncKeyState(1) & 0x8000) != 0)
            {
                WheelMode? mode = name == settings.ConvertKey ? WheelMode.Convert : name == settings.ToolsKey ? WheelMode.Tools : name == settings.ActionsKey ? WheelMode.Actions : null;
                if (mode.HasValue)
                {
                    var ticket = ++request; requestedAt = Environment.TickCount64;
                    // Sent messages are serviced inside OLE's modal loop even
                    // when it filters posted messages. Send from a worker so
                    // the keyboard hook itself always returns immediately.
                    Send(ProbeDrag, (IntPtr)(int)mode.Value, (IntPtr)ticket);
                }
            }
        }
        return CallNextHookEx(handle, code, message, data);
    }
    void Send(int message, IntPtr wparam, IntPtr lparam) { var hwnd = messages.Handle; ThreadPool.QueueUserWorkItem(state => SendMessageTimeout(hwnd, message, wparam, lparam, 2, 500, out _)); }
    public void Dispose() { if (disposed) return; disposed = true; request++; probe?.Close(); probe = null; if (handle != IntPtr.Zero) { UnhookWindowsHookEx(handle); handle = IntPtr.Zero; } messages.Dispose(); }
}
