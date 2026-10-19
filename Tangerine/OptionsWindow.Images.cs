using ImageMagick;
using Orientation = System.Windows.Controls.Orientation;
namespace Tangerine;

public sealed partial class OptionsWindow
{
    void LoadPhoto()
    {
        try
        {
            using var image = new MagickImage(input); image.AutoOrient(); imageWidth = (int)image.Width; imageHeight = (int)image.Height; photoScale = Math.Min(1, 1000.0 / Math.Max(imageWidth, imageHeight)); image.Resize((uint)Math.Max(1, imageWidth * photoScale), (uint)Math.Max(1, imageHeight * photoScale)); photo = image.ToByteArray(MagickFormat.Png);
            surface = new SelectionSurface { Editable = operation is "crop" or "redact", IsRedaction = operation == "redact", ShadeOutside = operation == "crop", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; surface.SetImage(PanelUi.Bitmap(photo), imageWidth, imageHeight, 410, 365);
            surface.SelectionChanged += r => SetRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            imageFrame = new Border { Child = surface, Background = operation == "crop" ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(25, 25, 25)), CornerRadius = new CornerRadius(12), Padding = new Thickness(9), MinHeight = 300, Margin = new Thickness(0, 0, 0, 14) }; Body.Children.Add(imageFrame);
        }
        catch (Exception ex) { ShowError("Preview unavailable: " + ex.Message); }
    }
    async Task RefreshPhoto()
    {
        if (operation == "collage") { await RefreshCollage(); return; }
        if (photo == null || surface == null || lifetime.IsCancellationRequested) return;
        try
        {
            var options = ReadOptions().PreviewCopy(photoScale); var version = ++renderVersion; var data = photo;
            var result = await Task.Run(() =>
            {
                using var image = new MagickImage(data);
                if (operation == "edit") Engine.AdjustImage(image, options);
                if (operation == "annotate") Engine.AnnotateImage(image, options);
                if (operation == "redact" && options.Width > 0 && options.Height > 0 && options.X + options.Width <= image.Width && options.Y + options.Height <= image.Height) Engine.RedactImage(image, options);
                if (operation == "background") { using var framed = Engine.FrameImage(image, options); return (Data: framed.ToByteArray(MagickFormat.Png), Width: (int)framed.Width, Height: (int)framed.Height); }
                return (Data: image.ToByteArray(MagickFormat.Png), Width: (int)image.Width, Height: (int)image.Height);
            }, lifetime.Token);
            if (version != renderVersion || lifetime.IsCancellationRequested) return;
            var bitmap = PanelUi.Bitmap(result.Data);
            if (operation is "background" or "edit") surface.SetImage(bitmap, result.Width, result.Height, 410, 365); else surface.ShowImage(bitmap);
            if (operation == "redact") surface.IsRedaction = !options.BlurRedaction;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is ArgumentException or FormatException or MagickException or OverflowException) {/* Invalid intermediate typing is reported on Apply. */}
    }
    void BuildCompress()
    {
        Body.MinHeight = 305; Body.Children.Add(PanelUi.Note(Inputs.Length == 1 ? Path.GetFileName(input) : $"{Inputs.Length} selected files"));
        var size = Inputs.Where(File.Exists).Sum(p => new FileInfo(p).Length); var label = PanelUi.Text("Original size: " + PanelUi.Size(size), 15); label.FontWeight = FontWeights.SemiBold; label.Margin = new Thickness(0, 10, 0, 13); Body.Children.Add(label);
        if (kind == FileKind.Pdf) { Section("Lossless compression"); Body.Children.Add(PanelUi.Note("Optimize the PDF's content streams while keeping its pages and image quality. Already optimized PDFs may stay the same size.")); return; }
        Section("Compression preset"); Field("Quality", Options.Quality);
        Body.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = Choice(new[] { "Balanced", "Strong" }, "Quality", new object[] { 82, 45 }) });
        Body.Children.Add(PanelUi.Note("Smaller files with less quality loss.")); Slider("Quality", "Quality", Options.Quality, 1, 100, "%", 1);
        if (kind == FileKind.Image) { Combo("Image size", "MaxDimension", new (string, object)[] { ("Keep original dimensions", 0), ("Up to 2,560 px", 2560), ("Up to 1,920 px", 1920), ("Up to 1,280 px", 1280) }); Body.Children.Add(PanelUi.Divider()); Check("Remove metadata", "StripMetadata", true); }
        else Body.Children.Add(PanelUi.Note(kind == FileKind.Audio ? "Uncompressed WAV and AIFF files are saved as lossless FLAC." : "Keeps the original dimensions and audio track."));
    }
    void BuildRectangle()
    {
        Field("X", 0); Field("Y", 0); Field("Width", Math.Max(1, imageWidth)); Field("Height", Math.Max(1, imageHeight));
        if (operation == "crop")
        {
            Body.Children.Add(PanelUi.Row("Aspect ratio", Segments(new[] { "Free", "1:1", "16:9", "9:16", "4:3", "3:4" }, 0, i => surface?.SetRatio(new[] { 0d, 1, 16d / 9, 9d / 16, 4d / 3, 3d / 4 }[i])), 94));
        }
        else
        {
            Field("BlurRedaction", false); Body.Children.Add(PanelUi.Row("Style", new Border { HorizontalAlignment = HorizontalAlignment.Right, Child = Choice(new[] { "Solid", "Blur" }, "BlurRedaction", new object[] { false, true }) }));
            ColorSwatches("RedactionColor", "Color", new[] { "#000000", "#FFFFFF", "#FF5900", "#506A88" });
        }
        var size = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) }; size.Children.Add(PanelUi.Text("W", 11, PanelUi.Muted)); var width = fields["Width"]; width.Margin = new Thickness(6, 0, 12, 0); size.Children.Add(width); size.Children.Add(PanelUi.Text("H", 11, PanelUi.Muted)); var height = fields["Height"]; height.Margin = new Thickness(6, 0, 6, 0); size.Children.Add(height); size.Children.Add(PanelUi.Text("px", 11, PanelUi.Muted)); Body.Children.Add(size);
        var coordinates = new StackPanel { Orientation = Orientation.Horizontal }; coordinates.Children.Add(PanelUi.Text("X", 11)); fields["X"].Margin = new Thickness(8, 0, 15, 0); coordinates.Children.Add(fields["X"]); coordinates.Children.Add(PanelUi.Text("Y", 11)); fields["Y"].Margin = new Thickness(8, 0, 0, 0); coordinates.Children.Add(fields["Y"]); Body.Children.Add(new Expander { Header = "Position", Content = coordinates });
        Body.Children.Add(PanelUi.Note(kind == FileKind.Video && operation == "redact" ? "Drag the box or its handles. This region is covered for the entire clip." : "Drag to select an area. Move the selection or resize it using the orange handles."));
        if (media != null) Body.Children.Add(media.Transport);
    }
    void BuildEdit()
    {
        Section("Light"); Slider("Exposure", "Exposure", 0, -3, 3, "EV", .1); Slider("Brightness", "Brightness", 100, 0, 200, "%"); Slider("Contrast", "Contrast", 0, -100, 100);
        Section("Color"); Slider("Saturation", "Saturation", 100, 0, 200, "%"); Slider("Hue", "Hue", 0, -180, 180, "°");
        Section("Orientation"); Field("Rotation", 0); Body.Children.Add(PanelUi.Row("Rotate", Choice(new[] { "0°", "90°", "180°", "270°" }, "Rotation", new object[] { 0, 90, 180, 270 })));
    }
    void BuildAnnotation()
    {
        Field("AnnotationShape", "Text"); Body.Children.Add(Choice(new[] { "Aa  Text", "↗  Arrow", "□  Box", "○  Ellipse" }, "AnnotationShape", new object[] { "Text", "Arrow", "Rectangle", "Ellipse" }, 0, true));
        AddText("Text", "Text", "Your annotation"); Slider("Size", "FontSize", 36, 8, 160, "px"); ColorSwatches("Color", "Color", new[] { "#FF5900", "#ED4545", "#FFFFFF", "#000000", "#477BEB" });
        Field("X", Math.Min(30, Math.Max(0, imageWidth / 8))); Field("Y", Math.Min(60, Math.Max(1, imageHeight / 4))); Field("Width", Math.Max(1, imageWidth / 3)); Field("Height", Math.Max(1, imageHeight / 3));
        var positioning = new StackPanel(); foreach (var key in new[] { "X", "Y", "Width", "Height" }) positioning.Children.Add(PanelUi.Row(key, fields[key])); Body.Children.Add(new Expander { Header = "Position & shape size", Content = positioning });
        if (surface != null) { surface.Cursor = Cursors.Cross; surface.MouseLeftButtonDown += (_, e) => { var p = e.GetPosition(surface); Set("X", (int)(p.X / surface.Scale)); Set("Y", (int)(p.Y / surface.Scale)); }; }
        Body.Children.Add(PanelUi.Note("Click the preview to place the annotation. Adjust its size and color below."));
    }
    void BuildBackground()
    {
        if (imageFrame != null) imageFrame.Background = new SolidColorBrush(Color.FromRgb(235, 235, 235));
        Field("Color", "#FF859E"); Field("BackgroundEnd", "#FFD05B");
        var presets = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        foreach (var pair in new[] { ("#FF859E", "#FFD05B"), ("#9C86F5", "#FAC7B8"), ("#65CDD2", "#D9F484"), ("#4779DF", "#B7E1F0"), ("#FF9145", "#DD4FB6"), ("#323F53", "#7992AC"), ("#FFFFFF", "#FFFFFF"), ("#EDC95A", "#EDC95A"), ("#86B966", "#86B966"), ("#55A5A3", "#55A5A3"), ("#4264CD", "#4264CD"), ("#262E3B", "#262E3B") })
        {
            var button = PanelUi.Button("", () => { Set("Color", pair.Item1); Set("BackgroundEnd", pair.Item2); }); button.Width = 31; button.Height = 30; button.Padding = new Thickness(0); button.Margin = new Thickness(0, 0, 6, 6); button.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(pair.Item1), (Color)ColorConverter.ConvertFromString(pair.Item2), 45); AutomationProperties.SetName(button, "Background " + pair.Item1 + " to " + pair.Item2); presets.Children.Add(button);
        }
        Body.Children.Add(PanelUi.Row("Background", presets, 86)); Slider("Padding", "Padding", 80, 0, 400, "px"); Slider("Corners", "Radius", 24, 0, 160, "px"); Slider("Shadow", "Shadow", 24, 0, 100, "px");
        Field("BackgroundRatio", 0); Body.Children.Add(PanelUi.Row("Ratio", Choice(new[] { "Auto", "1:1", "16:9", "4:5" }, "BackgroundRatio", new object[] { 0d, 1d, 16d / 9, 4d / 5 })));
    }
    void ColorSwatches(string key, string label, string[] colors)
    {
        Field(key, colors[0]); var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var color in colors) { var b = PanelUi.Button("", () => Set(key, color)); b.Width = 24; b.Height = 24; b.MinHeight = 24; b.Padding = new Thickness(0); b.Margin = new Thickness(4, 0, 0, 0); b.Background = (Brush)new BrushConverter().ConvertFromString(color)!; b.ToolTip = color; AutomationProperties.SetName(b, label + " " + color); row.Children.Add(b); }
        Body.Children.Add(PanelUi.Row(label, row));
    }
}
