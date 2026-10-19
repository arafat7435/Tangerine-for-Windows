namespace Tangerine;

internal sealed class RangeTimeline : FrameworkElement
{
    internal double Duration { get; set; } = 1;
    internal double Start { get; private set; }
    internal double End { get; private set; } = 1;
    internal BitmapSource? Picture { get; set; }
    internal bool RangeEnabled { get; set; } = true;
    internal List<double> Markers { get; } = new();
    internal event Action<double, double>? RangeChanged;
    int mode; double anchor, oldStart, oldEnd;
    internal RangeTimeline() { Height = 82; Cursor = Cursors.Hand; Focusable = true; MouseLeftButtonDown += Down; MouseMove += Move; MouseLeftButtonUp += (_, _) => { mode = 0; ReleaseMouseCapture(); }; LostMouseCapture += (_, _) => mode = 0; }
    internal void SetRange(double start, double end, bool notify = false) { Start = Math.Clamp(start, 0, Math.Max(0, Duration - .01)); End = Math.Clamp(end, Start + .01, Math.Max(Start + .01, Duration)); InvalidateVisual(); if (notify) RangeChanged?.Invoke(Start, End); }
    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight); dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(234, 234, 234)), null, rect, 8, 8); dc.PushClip(new RectangleGeometry(rect, 8, 8));
        if (Picture != null) dc.DrawImage(Picture, rect);
        var span = Math.Max(.01, Duration); var a = Start / span * ActualWidth; var b = End / span * ActualWidth;
        if (RangeEnabled) { dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Rect(0, 0, a, ActualHeight)); dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Rect(b, 0, Math.Max(0, ActualWidth - b), ActualHeight)); dc.DrawRectangle(null, new Pen(PanelUi.Orange, 2), new Rect(a, 1, Math.Max(0, b - a), ActualHeight - 2)); foreach (var x in new[] { a, b }) { dc.DrawRoundedRectangle(PanelUi.Orange, null, new Rect(Math.Clamp(x - 5, 0, Math.Max(0, ActualWidth - 10)), 0, 10, ActualHeight), 3, 3); dc.DrawLine(new Pen(Brushes.White, 2), new Point(Math.Clamp(x, 5, ActualWidth - 5), ActualHeight / 2 - 9), new Point(Math.Clamp(x, 5, ActualWidth - 5), ActualHeight / 2 + 9)); } }
        foreach (var marker in Markers) { var x = marker / span * ActualWidth; dc.DrawLine(new Pen(PanelUi.Orange, 2), new Point(x, 0), new Point(x, ActualHeight)); }
        dc.Pop();
    }
    void Down(object sender, MouseButtonEventArgs e) { if (!RangeEnabled) return; Focus(); anchor = e.GetPosition(this).X; oldStart = Start; oldEnd = End; var a = Start / Duration * ActualWidth; var b = End / Duration * ActualWidth; mode = Math.Abs(anchor - a) < 14 ? 1 : Math.Abs(anchor - b) < 14 ? 2 : 3; CaptureMouse(); e.Handled = true; }
    void Move(object sender, MouseEventArgs e) { if (mode == 0 || ActualWidth == 0) return; var delta = (e.GetPosition(this).X - anchor) / ActualWidth * Duration; if (mode == 1) SetRange(Math.Clamp(oldStart + delta, 0, End - .01), End, true); else if (mode == 2) SetRange(Start, Math.Clamp(oldEnd + delta, Start + .01, Duration), true); else { var length = oldEnd - oldStart; var start = Math.Clamp(oldStart + delta, 0, Duration - length); SetRange(start, start + length, true); } }
}
