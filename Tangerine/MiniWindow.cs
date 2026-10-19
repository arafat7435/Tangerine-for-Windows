using System.Windows.Media.Effects;
using Orientation = System.Windows.Controls.Orientation;
namespace Tangerine;

public class MiniWindow : Window
{
    protected readonly StackPanel Body = new();
    protected readonly StackPanel FooterLeft = new() { Orientation = Orientation.Horizontal };
    protected readonly StackPanel FooterRight = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    protected readonly TextBlock Error = new() { Foreground = new SolidColorBrush(Color.FromRgb(183, 64, 38)), TextWrapping = TextWrapping.Wrap, FontSize = 11, Visibility = Visibility.Collapsed };
    protected readonly ScrollViewer BodyScroll;
    public MiniWindow(string title, double width = 520)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Foreground = PanelUi.Ink; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Tangerine;component/PanelStyles.xaml", UriKind.Relative) });
        var root = new Border { Margin = new Thickness(18), CornerRadius = new CornerRadius(24), Background = PanelUi.Surface, BorderBrush = PanelUi.Line, BorderThickness = new Thickness(1), Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 7, Opacity = .13, Color = Colors.Black } };
        var layout = new DockPanel(); root.Child = layout; Content = root;
        Loaded += (_, _) => Motion.Reveal(root, 8);
        // Run once after the native surface exists, outside the source's Drop
        // callback. All tool, metadata and batch panels share this focus handoff.
        EventHandler? firstRender = null; firstRender = async (_, _) => { ContentRendered -= firstRender; await WindowActivation.OnFirstRender(this); }; ContentRendered += firstRender;
        var header = new Border { Height = 54, BorderBrush = PanelUi.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        var grid = new Grid(); header.Child = grid;
        grid.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(50, 0, 50, 0), TextTrimming = TextTrimming.CharacterEllipsis });
        var close = new Button { Content = "×", Width = 28, Height = 28, MinHeight = 0, FontSize = 21, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0), ToolTip = "Close · Esc" };
        close.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border Background='{TemplateBinding Background}' CornerRadius='14'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
        AutomationProperties.SetName(close, "Close panel"); close.Click += (_, _) => Close(); grid.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button && e.LeftButton == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { } };
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var footer = new Border { BorderBrush = PanelUi.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 15, 18, 15) };
        var footerLayout = new DockPanel(); footer.Child = footerLayout; DockPanel.SetDock(Error, Dock.Top); footerLayout.Children.Add(Error);
        var buttons = new Grid(); buttons.Children.Add(FooterLeft); buttons.Children.Add(FooterRight); footerLayout.Children.Add(buttons); DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        BodyScroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(18, 14, 18, 18), MaxHeight = Math.Max(220, SystemParameters.WorkArea.Height - 205) }; layout.Children.Add(BodyScroll);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    protected void ShowError(string message) { Error.Text = message; Error.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible; Error.Margin = new Thickness(0, 0, 0, 10); }
}

internal static class PanelUi
{
    internal static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(69, 69, 69)), Muted = new SolidColorBrush(Color.FromRgb(132, 132, 132)), Surface = new SolidColorBrush(Color.FromRgb(248, 248, 248)), Line = new SolidColorBrush(Color.FromRgb(222, 222, 222)), Orange = new SolidColorBrush(Color.FromRgb(250, 94, 37));
    internal static TextBlock Text(string text, double size = 12, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    internal static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 9, 0, 9) };
    internal static Border Divider() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 14, 0, 12) };
    internal static Button Button(string text, Action action, string? name = null) { var b = new Button { Content = text }; AutomationProperties.SetName(b, name ?? text); b.Click += (_, _) => action(); return b; }
    internal static Grid Row(string label, UIElement value, double labelWidth = 125) { var row = new Grid { Margin = new Thickness(0, 5, 0, 5) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.Children.Add(Text(label, 12, Muted)); Grid.SetColumn(value, 1); row.Children.Add(value); return row; }
    internal static FrameworkElement Segments(IEnumerable<string> labels, int selected, Action<int> changed, bool orange = false)
    {
        var grid = new UniformGridAdapter(); var border = new Border { Background = new SolidColorBrush(Color.FromRgb(233, 233, 233)), CornerRadius = new CornerRadius(6), Padding = new Thickness(2), Child = grid };
        var buttons = new List<Button>(); void Select(int index) { for (int n = 0; n < buttons.Count; n++) { buttons[n].Background = n == index ? (orange ? Orange : new SolidColorBrush(Color.FromRgb(201, 201, 201))) : Brushes.Transparent; buttons[n].Foreground = n == index && orange ? Brushes.White : Ink; } }
        int i = 0; foreach (var label in labels) { var index = i++; var b = new Button { Content = label, Padding = new Thickness(9, 3, 9, 3), MinHeight = 25, FontSize = 11 }; AutomationProperties.SetName(b, label); b.Click += (_, _) => { Select(index); changed(index); }; buttons.Add(b); grid.Children.Add(b); }
        border.Tag = (Action<int>)Select; Select(selected); return border;
    }
    internal static BitmapSource Bitmap(byte[] bytes) { using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap; }
    internal static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.#} MB" : $"{bytes / 1024.0:0.#} KB";
    sealed class UniformGridAdapter : System.Windows.Controls.Primitives.UniformGrid { public UniformGridAdapter() { Rows = 1; } }
}
