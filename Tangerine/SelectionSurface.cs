using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
namespace Tangerine;

internal sealed class SelectionSurface : Canvas
{
    readonly System.Windows.Controls.Image picture = new() { Stretch = Stretch.Fill };
    readonly Rectangle box = new() { Stroke = PanelUi.Orange, StrokeThickness = 2, Fill = Brushes.Transparent };
    readonly System.Windows.Controls.Image blurred = new() { Stretch = Stretch.Fill, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 12 } };
    readonly Border blurWindow = new() { ClipToBounds = true, IsHitTestVisible = false };
    readonly List<Thumb> handles = new();
    readonly System.Windows.Shapes.Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)), IsHitTestVisible = false };
    Point anchor; Rect original; bool dragging, moving; double ratio;
    internal int PixelWidth { get; private set; }
    internal int PixelHeight { get; private set; }
    internal Rect Selection { get; private set; }
    internal double Scale => PixelWidth == 0 ? 1 : Width / PixelWidth;
    internal event Action<Rect>? SelectionChanged;
    bool redaction;
    internal bool IsRedaction { get => redaction; set { redaction = value; if (PixelWidth > 0) Draw(); } }
    internal bool ShadeOutside { get; set; } = true;
    internal bool BlurSelection { get; set; }
    internal Brush RedactionFill { get; set; } = Brushes.Black;
    internal bool Editable { get; set; } = true;
    internal SelectionSurface()
    {
        ClipToBounds = false; Focusable = true; Background = Brushes.Transparent; Children.Add(picture); Children.Add(shade); blurWindow.Child = blurred; Children.Add(blurWindow); Children.Add(box);
        foreach (var tag in new[] { "NW", "N", "NE", "E", "SE", "S", "SW", "W" })
        {
            var thumb = new Thumb { Width = 9, Height = 9, Tag = tag, Background = PanelUi.Orange, Cursor = tag is "N" or "S" ? Cursors.SizeNS : tag is "E" or "W" ? Cursors.SizeWE : tag is "NE" or "SW" ? Cursors.SizeNESW : Cursors.SizeNWSE };
            thumb.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='{TemplateBinding Background}' CornerRadius='2'/></ControlTemplate>");
            AutomationProperties.SetName(thumb, "Resize " + tag); thumb.DragStarted += (_, _) => { original = Selection; anchor = Mouse.GetPosition(this); };
            thumb.DragDelta += (_, _) => Resize(tag, Mouse.GetPosition(this)); handles.Add(thumb); Children.Add(thumb);
        }
        MouseLeftButtonDown += (_, e) => { if (!Editable || e.OriginalSource is Thumb) return; Focus(); anchor = e.GetPosition(this); original = Selection; var p = new Point(anchor.X / Scale, anchor.Y / Scale); moving = Selection.Contains(p) && (Selection.Width < PixelWidth || Selection.Height < PixelHeight); dragging = true; CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) => { if (!dragging || e.LeftButton != MouseButtonState.Pressed) return; var p = e.GetPosition(this); if (moving) { SetSelection(new Rect(Math.Clamp(original.X + (p.X - anchor.X) / Scale, 0, PixelWidth - original.Width), Math.Clamp(original.Y + (p.Y - anchor.Y) / Scale, 0, PixelHeight - original.Height), original.Width, original.Height)); } else { var x = Math.Clamp(Math.Min(anchor.X, p.X) / Scale, 0, PixelWidth - 1); var y = Math.Clamp(Math.Min(anchor.Y, p.Y) / Scale, 0, PixelHeight - 1); var w = Math.Clamp(Math.Abs(p.X - anchor.X) / Scale, 1, PixelWidth - x); var h = Math.Clamp(Math.Abs(p.Y - anchor.Y) / Scale, 1, PixelHeight - y); if (ratio > 0) { h = Math.Min(h, w / ratio); w = h * ratio; } SetSelection(new Rect(x, y, Math.Max(1, w), Math.Max(1, h))); } };
        MouseLeftButtonUp += (_, _) => { dragging = false; ReleaseMouseCapture(); }; LostMouseCapture += (_, _) => dragging = false;
        KeyDown += (_, e) => { int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1; var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0; var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0; if (dx != 0 || dy != 0) { SetSelection(new Rect(Math.Clamp(Selection.X + dx, 0, PixelWidth - Selection.Width), Math.Clamp(Selection.Y + dy, 0, PixelHeight - Selection.Height), Selection.Width, Selection.Height)); e.Handled = true; } };
    }
    internal void SetImage(BitmapSource image, int width, int height, double maxWidth = 440, double maxHeight = 365)
    {
        PixelWidth = width; PixelHeight = height; var scale = Math.Min(maxWidth / width, maxHeight / height); Width = width * scale; Height = height * scale; picture.Source = image; picture.Width = Width; picture.Height = Height;
        if (Selection.IsEmpty || Selection.Width == 0) Selection = new Rect(0, 0, width, height); Draw();
    }
    internal void ShowImage(BitmapSource image) { picture.Source = image; }
    internal void HidePicture() => picture.Visibility = Visibility.Collapsed;
    internal void SetSelection(Rect value, bool notify = true)
    {
        if (PixelWidth == 0) return; var x = Math.Clamp((int)Math.Round(value.X), 0, PixelWidth - 1); var y = Math.Clamp((int)Math.Round(value.Y), 0, PixelHeight - 1);
        Selection = new Rect(x, y, Math.Clamp((int)Math.Round(value.Width), 1, PixelWidth - x), Math.Clamp((int)Math.Round(value.Height), 1, PixelHeight - y)); Draw(); if (notify) SelectionChanged?.Invoke(Selection);
    }
    internal void SetRatio(double value) { ratio = value; if (value == 0) return; var w = (double)PixelWidth; var h = w / value; if (h > PixelHeight) { h = PixelHeight; w = h * value; } SetSelection(new Rect((PixelWidth - w) / 2, (PixelHeight - h) / 2, w, h)); }
    internal void Reset() => SetSelection(new Rect(0, 0, PixelWidth, PixelHeight));
    void Resize(string tag, Point point)
    {
        double dx = (point.X - anchor.X) / Scale, dy = (point.Y - anchor.Y) / Scale;
        double left = tag.Contains('W') ? Math.Clamp(original.Left + dx, 0, original.Right - 1) : original.Left, right = tag.Contains('E') ? Math.Clamp(original.Right + dx, original.Left + 1, PixelWidth) : original.Right;
        double top = tag.Contains('N') ? Math.Clamp(original.Top + dy, 0, original.Bottom - 1) : original.Top, bottom = tag.Contains('S') ? Math.Clamp(original.Bottom + dy, original.Top + 1, PixelHeight) : original.Bottom;
        if (ratio > 0) { var w = right - left; var h = bottom - top; if (w / h > ratio) w = h * ratio; else h = w / ratio; if (tag.Contains('W')) left = right - w; else right = left + w; if (tag.Contains('N')) top = bottom - h; else bottom = top + h; }
        SetSelection(new Rect(left, top, right - left, bottom - top));
    }
    void Draw()
    {
        var visibility = Editable ? Visibility.Visible : Visibility.Collapsed; box.Visibility = visibility; foreach (var h in handles) h.Visibility = visibility;
        if (!Editable) { shade.Visibility = Visibility.Collapsed; return; }
        var r = new Rect(Selection.X * Scale, Selection.Y * Scale, Selection.Width * Scale, Selection.Height * Scale); SetLeft(box, r.X); SetTop(box, r.Y); box.Width = r.Width; box.Height = r.Height; box.Fill = IsRedaction ? RedactionFill : Brushes.Transparent;
        blurWindow.Visibility = BlurSelection ? Visibility.Visible : Visibility.Collapsed;
        if (BlurSelection && picture.Source is BitmapSource bitmap) { int x = Math.Clamp((int)(Selection.X / PixelWidth * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1), y = Math.Clamp((int)(Selection.Y / PixelHeight * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1); int w = Math.Clamp((int)(Selection.Width / PixelWidth * bitmap.PixelWidth), 1, bitmap.PixelWidth - x), h = Math.Clamp((int)(Selection.Height / PixelHeight * bitmap.PixelHeight), 1, bitmap.PixelHeight - y); blurred.Source = new CroppedBitmap(bitmap, new Int32Rect(x, y, w, h)); SetLeft(blurWindow, r.X); SetTop(blurWindow, r.Y); blurWindow.Width = r.Width; blurWindow.Height = r.Height; }
        shade.Visibility = IsRedaction || !ShadeOutside ? Visibility.Collapsed : Visibility.Visible; shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Width, Height)), new RectangleGeometry(r));
        foreach (var h in handles) { var tag = (string)h.Tag; SetLeft(h, (tag.Contains('W') ? r.Left : tag.Contains('E') ? r.Right : r.Left + r.Width / 2) - 4.5); SetTop(h, (tag.Contains('N') ? r.Top : tag.Contains('S') ? r.Bottom : r.Top + r.Height / 2) - 4.5); }
    }
}
