using System.Windows.Interop;
using System.Windows.Threading;
namespace Tangerine;
public sealed class OverlayWindow : Window
{
    internal static Action<string>? Diagnostic;
    readonly App app; readonly RadialWheel wheel = new(); readonly TextBlock hint = new() { Foreground = Brushes.White, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
    string[] files; WheelMode mode; readonly bool dragging; readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(120) }; DateTime opened = DateTime.UtcNow;
    public OverlayWindow(App app, string[] files, WheelMode mode, bool dragging)
    {
        this.app = app; this.files = files; this.mode = mode; this.dragging = dragging;
        Width = 328; Height = 360; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = !dragging; AllowDrop = true;
        var stack = new StackPanel { Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)), Margin = new Thickness(20, 5, 20, 0) };
        var keys = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 49, 51, 48)), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 5) }; keys.Child = new TextBlock { Text = $"{app.Settings.ConvertKey}  Convert    {app.Settings.ToolsKey}  Tools    {app.Settings.ActionsKey}  Actions", Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center }; stack.Children.Add(keys); stack.Children.Add(wheel);
        var label = new Border { Background = new SolidColorBrush(Color.FromArgb(225, 49, 51, 48)), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 5, 9, 5), HorizontalAlignment = HorizontalAlignment.Center }; label.Child = hint; stack.Children.Add(label); Content = stack;
        // OLE uses the transparent window's rendered region for hit testing.
        // Keep the drag target opaque; selection colors can still animate.
        Loaded += (_, _) => { if (!dragging) Motion.Reveal(stack, milliseconds: 120, opacity: .7); };
        wheel.SelectionChanged += x => hint.Text = (x?.Description ?? "") + (this.files.Length > 1 ? $" · {this.files.Length} files" : ""); wheel.Invoked += item => { if (!dragging) Apply(item); }; Refresh();
        DragOver += OnDragOver; Drop += OnDrop; DragEnter += OnDragOver; DragLeave += (_, _) => wheel.Hover(-1);
        KeyDown += (_, e) => { var key = e.Key == Key.System ? e.SystemKey : e.Key; if (key == Key.Escape) Close(); else if (key is Key.LeftShift or Key.RightShift) Switch(ModeFor("Shift")); else if (key is Key.LeftCtrl or Key.RightCtrl) Switch(ModeFor("Ctrl")); else if (key is Key.LeftAlt or Key.RightAlt) Switch(ModeFor("Alt")); };
        if (dragging) { timer.Tick += (_, _) => { if ((Native.GetAsyncKeyState(27) & 0x8000) != 0 || ((Native.GetAsyncKeyState(1) & 0x8000) == 0 && (DateTime.UtcNow - opened).TotalMilliseconds > 700)) Close(); }; timer.Start(); }
        else Deactivated += (_, _) => { if (IsVisible) Close(); };
        Closed += (_, _) => timer.Stop();
    }
    WheelMode ModeFor(string key) => key == app.Settings.ConvertKey ? WheelMode.Convert : key == app.Settings.ToolsKey ? WheelMode.Tools : WheelMode.Actions;
    internal Point TopPetalScreenPoint => wheel.PointToScreen(new Point(144, 50));
    internal Point TestScreenPoint(int index) => index < 0 ? wheel.PointToScreen(new Point(144, 144)) : wheel.PointToScreen(new Point(144 + 94 * Math.Cos((wheel.Items.Count == 3 ? Math.PI : -Math.PI / 2) + index * Math.PI * 2 / wheel.Items.Count), 144 + 94 * Math.Sin((wheel.Items.Count == 3 ? Math.PI : -Math.PI / 2) + index * Math.PI * 2 / wheel.Items.Count)));
    public void Switch(WheelMode value) { mode = value; Refresh(); }
    void Refresh() { var sample = files.FirstOrDefault() ?? "sample.jpg"; wheel.SetItems(mode == WheelMode.Actions ? Catalog.Actions : mode == WheelMode.Tools ? files.Length > 0 ? Catalog.ToolsForSelection(files) : Catalog.Tools(sample) : files.Length > 0 ? Catalog.ConversionsForSelection(files) : Catalog.Conversions(sample)); if (wheel.Items.Count == 0) hint.Text = "No common operation · " + app.Settings.ActionsKey + " for actions"; }
    public void ShowAtCursor()
    {
        Native.GetCursorPos(out var p); var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).WorkingArea;
        Show(); var dpi = VisualTreeHelper.GetDpi(this); int w = (int)(Width * dpi.DpiScaleX), h = (int)(Height * dpi.DpiScaleY);
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, (IntPtr)(-1), Math.Clamp(p.X - w / 2, area.Left, Math.Max(area.Left, area.Right - w)), Math.Clamp(p.Y - (int)(176 * dpi.DpiScaleY), area.Top, Math.Max(area.Top, area.Bottom - h)), w, h, 0x0010);
        if (!dragging) wheel.Focus();
    }
    void OnDragOver(object sender, DragEventArgs e)
    {
        Diagnostic?.Invoke("DragOver: " + string.Join(",", e.Data.GetFormats()) + " at " + e.GetPosition(wheel));
        if (e.Data.GetData(DataFormats.FileDrop) is string[] incoming && incoming.Length > 0)
        {
            if (!files.SequenceEqual(incoming)) { files = incoming; Refresh(); }
            var hit = wheel.Hit(e.GetPosition(wheel)); wheel.Hover(hit); e.Effects = hit >= 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else e.Effects = DragDropEffects.None; e.Handled = true;
    }
    void OnDrop(object sender, DragEventArgs e)
    {
        Diagnostic?.Invoke("Drop: " + e.GetPosition(wheel));
        var hit = wheel.Hit(e.GetPosition(wheel)); if (hit >= 0 && e.Data.GetData(DataFormats.FileDrop) is string[] incoming) { files = incoming; e.Effects = DragDropEffects.Copy; var item = wheel.Items[hit]; e.Handled = true; Apply(item); } else { e.Effects = DragDropEffects.None; e.Handled = true; Close(); }
    }
    void Apply(WheelItem item) { var selection = files; var selectedMode = mode; Close(); app.Dispatcher.BeginInvoke(async () => { if (selection.Length == 0) selection = MainWindow.Browse(); if (selection.Length > 0) await app.Execute(selection, item, selectedMode); }); }
}
