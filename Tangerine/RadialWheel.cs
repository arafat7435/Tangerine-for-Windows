using System.Windows.Shapes;
using System.Windows.Media.Effects;
namespace Tangerine;

public sealed class RadialWheel : Canvas
{
    const double C = 144, Outer = 132, Inner = 51;
    readonly List<System.Windows.Shapes.Path> petals = new();
    readonly List<StackPanel> labels = new();
    int hovered = -1;
    readonly TextBlock center = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Border pill = new() { CornerRadius = new CornerRadius(22), Background = Brushes.White, Padding = new Thickness(13, 9, 13, 9) };
    public IReadOnlyList<WheelItem> Items { get; private set; } = Array.Empty<WheelItem>();
    public int SelectedIndex { get; private set; } = -1;
    public WheelItem? Selected => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
    public event Action<WheelItem?>? SelectionChanged;
    public event Action<WheelItem>? Invoked;
    public RadialWheel()
    {
        Width = Height = 288; Background = Brushes.Transparent; Focusable = true;
        MouseMove += (_, e) => Hover(Hit(e.GetPosition(this)));
        MouseLeave += (_, _) => hovered = -1;
        MouseLeftButtonUp += (_, e) => { var i = Hit(e.GetPosition(this)); if (i >= 0) { Select(i); Invoked?.Invoke(Items[i]); e.Handled = true; } };
        KeyDown += (_, e) => { if (Items.Count == 0) return; if (e.Key is Key.Right or Key.Down) { Select((SelectedIndex + 1) % Items.Count); e.Handled = true; } else if (e.Key is Key.Left or Key.Up) { Select((SelectedIndex + Items.Count - 1) % Items.Count); e.Handled = true; } else if (e.Key == Key.Enter && Selected != null) { Invoked?.Invoke(Selected); e.Handled = true; } };
        AutomationProperties.SetName(this, "Radial file menu. Use arrow keys to select and Enter to apply.");
    }
    public void SetItems(IReadOnlyList<WheelItem> items)
    {
        Items = items; Children.Clear(); petals.Clear(); labels.Clear(); SelectedIndex = hovered = -1;
        var back = new Ellipse { Width = 278, Height = 278, Fill = new SolidColorBrush(Color.FromArgb(225, 232, 235, 235)), Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Opacity = .08 } };
        SetLeft(back, 5); SetTop(back, 5); Children.Add(back);
        for (int i = 0; i < items.Count; i++)
        {
            var path = new System.Windows.Shapes.Path { Data = Petal(i, items.Count), Stroke = new SolidColorBrush(Color.FromRgb(205, 210, 210)), StrokeThickness = 1, Fill = PetalBrush(), IsHitTestVisible = false };
            Children.Add(path); petals.Add(path);
            var a = Angle(i, items.Count); var r = 94.0;
            var panel = new StackPanel { Width = 82, IsHitTestVisible = false }; panel.SetValue(TextElement.ForegroundProperty, new SolidColorBrush(Colors.Black));
            if (items[i].Icon != "") panel.Children.Add(new TextBlock { Text = items[i].Id == "compress" ? "↘↖" : items[i].Icon, FontFamily = new FontFamily(items[i].Id == "compress" ? "Segoe UI" : "Segoe MDL2 Assets"), FontSize = items[i].Id == "compress" ? 13 : 18, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) });
            panel.Children.Add(new TextBlock { Text = items[i].Label, FontSize = items[i].Icon == "" ? 15 : (items.Count > 7 ? 8.5 : 9.5), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            SetLeft(panel, C + Math.Cos(a) * r - 41); SetTop(panel, C + Math.Sin(a) * r - (items[i].Icon == "" ? 10 : 21)); Children.Add(panel); labels.Add(panel);
        }
        var disc = new Ellipse { Width = 91, Height = 91, Fill = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), IsHitTestVisible = false }; SetLeft(disc, C - 45.5); SetTop(disc, C - 45.5); Children.Add(disc);
        pill.Child = center; pill.IsHitTestVisible = false; pill.Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = .06 }; Children.Add(pill); SetTop(pill, C - 19);
        if (items.Count > 0) Select(0); else { center.Text = "No tools"; PositionPill(); }
    }
    static Brush PetalBrush() => new RadialGradientBrush { GradientStops = new GradientStopCollection { new(Color.FromArgb(245, 252, 253, 253), 0), new(Color.FromArgb(242, 235, 238, 239), 1) } };
    static double Angle(int i, int n) => (n == 3 ? Math.PI : -Math.PI / 2) + 2 * Math.PI * i / n;
    static Point P(double r, double a) => new(C + r * Math.Cos(a), C + r * Math.Sin(a));
    static Geometry Petal(int index, int count)
    {
        // Round the four corners of each annular sector, leaving a consistent four-pixel gutter.
        double angle = Angle(index, count), half = Math.PI / count, start = angle - half, end = angle + half;
        if (count == 1) { start = Math.PI / 2 + .017; end = start + Math.PI * 2 - .04; }
        double os = start + 2.3 / Outer, oe = end - 2.3 / Outer, ins = start + 2.3 / Inner, ine = end - 2.3 / Inner;
        double co = Math.Min(.14, (oe - os) * .20), ci = Math.Min(.18, (ine - ins) * .18);
        var g = new StreamGeometry(); using (var c = g.Open())
        {
            c.BeginFigure(P(Inner + 10, ins), true, true);
            c.LineTo(P(Outer - 18, os), true, false); c.QuadraticBezierTo(P(Outer, os), P(Outer, os + co), true, false);
            c.ArcTo(P(Outer, oe - co), new Size(Outer, Outer), 0, oe - os - 2 * co > Math.PI, SweepDirection.Clockwise, true, false);
            c.QuadraticBezierTo(P(Outer, oe), P(Outer - 18, oe), true, false); c.LineTo(P(Inner + 10, ine), true, false);
            c.QuadraticBezierTo(P(Inner, ine), P(Inner, ine - ci), true, false);
            c.ArcTo(P(Inner, ins + ci), new Size(Inner, Inner), 0, ine - ins - 2 * ci > Math.PI, SweepDirection.Counterclockwise, true, false);
            c.QuadraticBezierTo(P(Inner, ins), P(Inner + 10, ins), true, false);
        }
        g.Freeze(); return g;
    }
    public int Hit(Point p)
    {
        for (int i = 0; i < petals.Count; i++) if (petals[i].Data.FillContains(p)) return i;
        return -1;
    }
    internal void Hover(int index)
    {
        if (index < 0 || index >= Items.Count) { hovered = -1; return; }
        var entered = index != hovered; hovered = index; var alreadySelected = index == SelectedIndex;
        Select(index);
        // Initial visual selection is silent; actually entering that block is not.
        if (entered && alreadySelected) PlaySelectionSound();
    }
    void PlaySelectionSound() { if (IsLoaded && IsVisible && Application.Current is App app && app.Settings.WheelSoundsEnabled) app.WheelSound.Play(); }
    public void Select(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        if (index == SelectedIndex) return; var previous = SelectedIndex; SelectedIndex = index;
        if (previous >= 0) PlaySelectionSound();
        for (int i = 0; i < petals.Count; i++) if (i == index || i == previous)
            {
                var on = i == index; var fill = (RadialGradientBrush)petals[i].Fill; var stroke = (SolidColorBrush)petals[i].Stroke; var text = (SolidColorBrush)labels[i].GetValue(TextElement.ForegroundProperty);
                Motion.Tint(fill.GradientStops[0], GradientStop.ColorProperty, on ? Color.FromRgb(255, 88, 0) : Color.FromArgb(245, 252, 253, 253), animate: IsLoaded);
                Motion.Tint(fill.GradientStops[1], GradientStop.ColorProperty, on ? Color.FromRgb(255, 88, 0) : Color.FromArgb(242, 235, 238, 239), animate: IsLoaded);
                Motion.Tint(stroke, SolidColorBrush.ColorProperty, on ? Colors.Transparent : Color.FromRgb(205, 210, 210), animate: IsLoaded);
                Motion.Tint(text, SolidColorBrush.ColorProperty, on ? Colors.White : Colors.Black, animate: IsLoaded);
            }
        center.Text = Items[index].Label; center.FontSize = Items[index].Icon == "" ? 13 : 9; PositionPill(); SelectionChanged?.Invoke(Selected);
        if (IsLoaded) Motion.Reveal(pill, milliseconds: 110, opacity: .72);
    }
    void PositionPill() { pill.Measure(new Size(110, 50)); SetLeft(pill, C - pill.DesiredSize.Width / 2); }
}
