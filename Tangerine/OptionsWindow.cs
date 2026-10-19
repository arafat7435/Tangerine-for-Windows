using ImageMagick;
using System.Windows.Threading;
using Orientation = System.Windows.Controls.Orientation;
namespace Tangerine;

public sealed partial class OptionsWindow : MiniWindow
{
    public OperationOptions Options { get; } = new();
    public string[] Inputs { get; private set; }
    readonly Dictionary<string, TextBox> fields = new();
    readonly Dictionary<string, string> defaults = new();
    readonly List<Action> resetUi = new(); readonly string[] originalInputs;
    readonly string operation, input; readonly FileKind kind; readonly AppSettings settings;
    readonly CancellationTokenSource lifetime = new();
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly Button apply;
    SelectionSurface? surface; Border? imageFrame; byte[]? photo; double photoScale = 1; int imageWidth, imageHeight, renderVersion; bool syncing;
    MediaPreview? media; RangeTimeline? timeline;
    internal Task Ready { get; private set; } = Task.CompletedTask;
    internal Button PrimaryAction => apply;
    internal string? ConfirmLabel { get; set; }
    internal Size BackgroundPreviewSize => surface == null ? new() : new(surface.ActualWidth, surface.ActualHeight);
    public static bool Needed(string op) => op is "compress" or "metadata" or "crop" or "redact" or "edit" or "annotate" or "background" or "trim" or "speed" or "channels" or "snapshot" or "split" or "organize" or "bleep" or "collage" or "normalize" or "visualize" or "join" or "merge" or "createpdf";
    public OptionsWindow(string input, WheelItem item, AppSettings settings) : this(new[] { input }, item, settings) { }
    public OptionsWindow(string[] inputs, WheelItem item, AppSettings settings, OperationOptions? initial = null) : base(PanelTitle(item.Id, Catalog.Kind(inputs[0])), item.Id == "organize" ? 660 : 520)
    {
        Inputs = inputs.ToArray(); originalInputs = inputs.ToArray(); input = inputs[0]; kind = Catalog.Kind(input); operation = item.Id; this.settings = settings; Options.Quality = settings.Quality;
        apply = PanelUi.Button(ActionTitle(operation), Apply); apply.IsDefault = true; apply.FontWeight = FontWeights.Medium; FooterRight.Children.Add(apply);
        if (operation is not ("metadata" or "compress" or "join" or "merge" or "createpdf")) FooterLeft.Children.Add(PanelUi.Button("Reset", Reset));
        refresh.Tick += async (_, _) => { refresh.Stop(); await RefreshPhoto(); };
        Closed += (_, _) => { refresh.Stop(); lifetime.Cancel(); media?.Dispose(); };
        if (kind == FileKind.Image && operation is "crop" or "redact" or "edit" or "annotate" or "background") LoadPhoto();
        if (kind is FileKind.Video or FileKind.Audio && operation is not ("compress" or "metadata" or "join")) BuildMedia();
        switch (operation)
        {
            case "compress": BuildCompress(); break;
            case "metadata": BuildMetadata(); break;
            case "crop": case "redact": BuildRectangle(); break;
            case "edit": BuildEdit(); break;
            case "annotate": BuildAnnotation(); break;
            case "background": BuildBackground(); break;
            case "trim": case "bleep": case "snapshot": case "split": case "speed": case "channels": case "normalize": case "visualize": if (kind == FileKind.Pdf) BuildPdfSplit(); else BuildMediaControls(); break;
            case "organize": BuildPdfOrganizer(); break;
            case "collage": BuildCollage(); break;
            case "join": case "merge": case "createpdf": BuildFileOrder(); break;
        }
        if (operation is "crop" or "redact" && imageWidth > 0) SetRect(0, 0, imageWidth, imageHeight);
        if (operation == "redact" && imageWidth > 0) SetRect(imageWidth / 4, imageHeight / 4, Math.Max(1, imageWidth / 2), Math.Max(1, imageHeight / 3));
        Loaded += (_, _) => Ready = Initialize(initial);
    }
    async Task Initialize(OperationOptions? initial)
    {
        if (media != null) { apply.IsEnabled = false; await InitializeMedia(); } else if (operation == "organize") await LoadPdfPages(); else if (operation == "metadata") await LoadMetadata(); else if (operation == "collage") await RefreshCollage(); else if (photo != null) await RefreshPhoto();
        if (initial != null && !lifetime.IsCancellationRequested) { RestoreOptions(initial); if (photo != null) await RefreshPhoto(); }
        if (ConfirmLabel != null) apply.Content = ConfirmLabel;
    }
    internal void RestoreOptions(OperationOptions saved)
    {
        var copy = saved.Copy(); foreach (var property in typeof(OperationOptions).GetProperties()) property.SetValue(Options, property.GetValue(copy));
        syncing = true; try { foreach (var key in fields.Keys) Set(key, typeof(OperationOptions).GetProperty(key)!.GetValue(saved)!); } finally { syncing = false; }
        if (operation == "organize") RenderPages();
        if (surface != null && operation is "crop" or "redact") surface.SetSelection(new Rect(saved.X, saved.Y, Math.Max(1, saved.Width), Math.Max(1, saved.Height)), false);
        if (timeline != null) { timeline.SetRange(saved.Start, saved.Start + saved.Duration); timeline.Markers.Clear(); timeline.Markers.AddRange(saved.SplitPoints); timeline.InvalidateVisual(); UpdateRange(); }
        if (operation == "snapshot") media?.Seek(saved.Start); RenderSelections(); QueuePreview();
    }
    static string PanelTitle(string op, FileKind kind) => op switch
    {
        "compress" => "Compress",
        "metadata" => "Metadata",
        "edit" => "Edit Photo",
        "annotate" => "Annotate Photo",
        "background" => "Add Background",
        "crop" => kind == FileKind.Video ? "Crop Video" : "Crop Image",
        "redact" => kind == FileKind.Video ? "Redact Video" : "Redact Photo",
        "trim" => kind == FileKind.Audio ? "Trim Audio" : "Trim Video",
        "speed" => "Change Video Speed",
        "channels" => "Convert Audio Channels",
        "snapshot" => "Video Snapshots",
        "split" => kind == FileKind.Pdf ? "Split PDF" : "Split Video",
        "organize" => "Organize PDF",
        "bleep" => "Bleep Audio",
        "collage" => "Create Collage",
        "normalize" => "Normalize Volume",
        "visualize" => "Audio Visualizer",
        "join" => "Join Videos",
        "merge" => "Merge PDFs",
        "createpdf" => "Create PDF",
        _ => op
    };
    static string ActionTitle(string op) => op switch { "compress" => "Compress", "metadata" => "Remove All", "edit" => "Save Edited Copy", "annotate" => "Save Annotated Copy", "background" => "Save with Background", "redact" => "Save Redacted Copy", "channels" => "Convert Channels", "snapshot" => "Save Frames", "split" => "Split", "organize" => "Save Organized PDF", "bleep" => "Save Bleeped Copy", "collage" => "Create Collage", "normalize" => "Normalize Audio", "visualize" => "Create Visualization", "join" => "Join Videos", "merge" => "Merge PDFs", "createpdf" => "Create PDF", _ => "Apply" };
    TextBox Field(string key, object value, double width = 70)
    {
        if (fields.TryGetValue(key, out var existing)) return existing;
        var text = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", Width = width, HorizontalAlignment = HorizontalAlignment.Right }; fields[key] = text; defaults[key] = text.Text; AutomationProperties.SetName(text, key);
        text.TextChanged += (_, _) => { if (syncing) return; if (operation is "crop" or "redact" && surface != null) surface.SetSelection(new Rect(Math.Max(0, Number("X")), Math.Max(0, Number("Y")), Math.Max(1, Number("Width", imageWidth)), Math.Max(1, Number("Height", imageHeight))), false); QueuePreview(); }; return text;
    }
    void Set(string key, object value) { if (fields.TryGetValue(key, out var f)) f.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""; }
    double Number(string key, double fallback = 0) => fields.TryGetValue(key, out var f) && double.TryParse(f.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
    void SetRect(int x, int y, int w, int h) { syncing = true; Set("X", x); Set("Y", y); Set("Width", w); Set("Height", h); syncing = false; surface?.SetSelection(new Rect(x, y, Math.Max(1, w), Math.Max(1, h)), false); QueuePreview(); }
    void QueuePreview() { if (operation == "redact" && surface != null) { try { surface.RedactionFill = (Brush)new BrushConverter().ConvertFromString(fields.TryGetValue("RedactionColor", out var color) ? color.Text : "#000000")!; var blurEnabled = fields.TryGetValue("BlurRedaction", out var blur) && bool.TryParse(blur.Text, out var enabled) && enabled; surface.BlurSelection = blurEnabled && photo == null; surface.IsRedaction = !blurEnabled; } catch (FormatException) { } } if (photo != null || operation == "collage") { refresh.Stop(); refresh.Start(); } }
    void AddNumber(string label, string key, object value, double width = 84) => Body.Children.Add(PanelUi.Row(label, Field(key, value, width)));
    void AddText(string label, string key, string value) => Body.Children.Add(PanelUi.Row(label, Field(key, value, 250)));
    void Slider(string label, string key, double value, double min, double max, string unit = "", double step = 1)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        row.Children.Add(PanelUi.Text(label, 11, PanelUi.Muted)); var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, Margin = new Thickness(5, 0, 12, 0) }; AutomationProperties.SetName(slider, label); Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var f = Field(key, value, 48); f.FontSize = 11; f.ToolTip = label + (unit.Length > 0 ? " · " + unit : ""); f.Background = Brushes.Transparent; f.BorderThickness = new Thickness(0); f.Padding = new Thickness(3, 4, 3, 4); f.TextAlignment = System.Windows.TextAlignment.Right; var readout = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; readout.Children.Add(f); readout.Children.Add(PanelUi.Text(unit, 10, PanelUi.Muted)); Grid.SetColumn(readout, 2); row.Children.Add(readout); bool guard = false;
        slider.ValueChanged += (_, _) => { if (guard) return; guard = true; f.Text = slider.Value.ToString(step < 1 ? "0.##" : "0", CultureInfo.InvariantCulture); guard = false; }; f.TextChanged += (_, _) => { if (guard) return; if (double.TryParse(f.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)) { guard = true; slider.Value = Math.Clamp(n, min, max); guard = false; } }; Body.Children.Add(row);
    }
    void Section(string title) { var label = PanelUi.Text(title, 12, PanelUi.Muted); label.Margin = new Thickness(0, 15, 0, 7); label.FontWeight = FontWeights.SemiBold; Body.Children.Add(label); }
    void Combo(string label, string key, IEnumerable<(string Label, object Value)> items, int selected = 0)
    {
        var values = items.ToArray(); var field = Field(key, values[selected].Value); var combo = new ComboBox { ItemsSource = values.Select(x => x.Label).ToArray(), SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Right }; AutomationProperties.SetName(combo, label); combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) Set(key, values[combo.SelectedIndex].Value); }; field.TextChanged += (_, _) => combo.SelectedIndex = Array.FindIndex(values, v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) == field.Text); resetUi.Add(() => combo.SelectedIndex = selected); Body.Children.Add(PanelUi.Row(label, combo));
    }
    void Check(string label, string key, bool value) { var field = Field(key, value); var check = new CheckBox { Content = label, IsChecked = value }; check.Checked += (_, _) => Set(key, true); check.Unchecked += (_, _) => Set(key, false); field.TextChanged += (_, _) => check.IsChecked = bool.TryParse(field.Text, out var enabled) && enabled; Body.Children.Add(check); }
    FrameworkElement Choice(string[] labels, string key, object[] values, int selected = 0, bool orange = false)
    {
        var field = Field(key, values[selected]); var control = Segments(labels, selected, i => Set(key, values[i]), orange);
        field.TextChanged += (_, _) => ((Action<int>)control.Tag)(Array.FindIndex(values, v => Convert.ToString(v, CultureInfo.InvariantCulture) == field.Text)); return control;
    }
    FrameworkElement Segments(IEnumerable<string> labels, int selected, Action<int> changed, bool orange = false) { var control = PanelUi.Segments(labels, selected, changed, orange); resetUi.Add(() => { ((Action<int>)control.Tag)(selected); changed(selected); }); return control; }
    void Reset() { foreach (var pair in defaults) Set(pair.Key, pair.Value); foreach (var reset in resetUi) reset(); Inputs = originalInputs.ToArray(); if (operation is "collage" or "join" or "merge" or "createpdf") RenderFileOrder(); if (operation == "organize" && pdfPageCount > 0) { Options.PageEdits = Enumerable.Range(1, pdfPageCount).Select(i => new PdfPageChoice(i)).ToList(); RenderPages(); } if (surface != null && operation is "crop" or "redact") SetRect(0, 0, imageWidth, imageHeight); if (timeline != null) { timeline.SetRange(0, media?.Duration ?? 1, true); timeline.Markers.Clear(); timeline.InvalidateVisual(); } Options.SplitPoints.Clear(); Options.SnapshotTimes.Clear(); Options.BleepRanges.Clear(); RenderSelections(); QueuePreview(); ShowError(""); }
    internal OperationOptions ReadOptions() { foreach (var pair in fields) { var property = typeof(OperationOptions).GetProperty(pair.Key)!; property.SetValue(Options, Convert.ChangeType(pair.Value.Text, property.PropertyType, CultureInfo.InvariantCulture)); } if (operation == "snapshot" && media != null && Options.SnapshotTimes.Count == 0) Options.Start = Math.Min(media.Position, Math.Max(0, media.Duration - .001)); Options.Validate(operation); if (operation is "crop" or "redact" && imageWidth > 0 && (Options.X + (long)Options.Width > imageWidth || Options.Y + (long)Options.Height > imageHeight)) throw new ArgumentException($"Choose an area inside the {imageWidth} × {imageHeight} frame."); return Options; }
    void Apply() { try { ReadOptions(); if (operation == "organize" && Options.PageEdits.Count == 0) throw new ArgumentException("Keep at least one page."); DialogResult = true; } catch (Exception ex) { ShowError(ex is FormatException ? "Enter a valid number or color." : ex.Message); } }
}
