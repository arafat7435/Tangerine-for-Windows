using ImageMagick;
using PdfSharp.Pdf.IO;
using Orientation = System.Windows.Controls.Orientation;
namespace Tangerine;

public sealed partial class OptionsWindow
{
    readonly StackPanel metadataRows = new(), fileRows = new();
    readonly List<(string Group, string Name, string Value)> metadata = new();
    readonly WrapPanel pageCards = new(); readonly Dictionary<int, BitmapSource> pagePictures = new();
    TextBox? search; System.Windows.Controls.Image? collagePreview; int pdfPageCount;
    void BuildMetadata()
    {
        Body.MinHeight = 370; Body.Children.Add(PanelUi.Note(Path.GetFileName(input))); search = new TextBox { ToolTip = "Search metadata", HorizontalAlignment = HorizontalAlignment.Stretch }; AutomationProperties.SetName(search, "Search metadata"); Body.Children.Add(PanelUi.Row("Search metadata", search)); Body.Children.Add(metadataRows); search.TextChanged += (_, _) => RenderMetadata();
        Body.Children.Add(PanelUi.Divider()); Body.Children.Add(PanelUi.Note("Remove All creates a copy without embedded metadata. For photos, this can also remove camera and processing information."));
    }
    async Task LoadMetadata()
    {
        try
        {
            var entries = await Task.Run(async () =>
            {
                var list = new List<(string, string, string)> { ("File", "Name", Path.GetFileName(input)), ("File", "Size", PanelUi.Size(new FileInfo(input).Length)), ("File", "Format", Catalog.Extension(input).ToUpperInvariant()) };
                if (kind == FileKind.Image) { using var image = new MagickImage(input); list.Add(("Image", "Dimensions", $"{image.Width} × {image.Height}")); list.Add(("Image", "Color space", image.ColorSpace.ToString())); list.Add(("Image", "Depth", image.Depth + " bits")); foreach (var name in image.AttributeNames) list.Add(("Attributes", name, image.GetAttribute(name) ?? "")); var exif = image.GetExifProfile(); if (exif != null) foreach (var value in exif.Values) list.Add(("EXIF", value.Tag.ToString(), value.GetValue()?.ToString() ?? "")); foreach (var profile in image.ProfileNames) list.Add(("Profiles", profile, "Embedded profile")); }
                else if (kind == FileKind.Pdf) { using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import); list.Add(("PDF", "Pages", pdf.PageCount.ToString())); foreach (var pair in new[] { ("Title", pdf.Info.Title), ("Author", pdf.Info.Author), ("Subject", pdf.Info.Subject), ("Keywords", pdf.Info.Keywords), ("Creator", pdf.Info.Creator) }) if (!string.IsNullOrWhiteSpace(pair.Item2)) list.Add(("Document", pair.Item1, pair.Item2)); }
                else if (kind is FileKind.Audio or FileKind.Video) { var engine = new Engine(settings); using var json = JsonDocument.Parse(await Engine.Process(engine.FindTool("ffprobe")!, new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", input }, lifetime.Token)); if (json.RootElement.TryGetProperty("format", out var format)) { foreach (var pair in format.EnumerateObject().Where(p => p.Name is "duration" or "bit_rate" or "format_long_name")) list.Add(("Media", pair.Name, pair.Value.ToString())); if (format.TryGetProperty("tags", out var tags)) foreach (var tag in tags.EnumerateObject()) list.Add(("Tags", tag.Name, tag.Value.ToString())); } foreach (var stream in json.RootElement.GetProperty("streams").EnumerateArray()) { var group = "Stream " + stream.GetProperty("index"); foreach (var pair in stream.EnumerateObject().Where(p => p.Name is "codec_long_name" or "width" or "height" or "sample_rate" or "channels")) list.Add((group, pair.Name, pair.Value.ToString())); } }
                return list;
            }, lifetime.Token);
            if (lifetime.IsCancellationRequested) return; metadata.AddRange(entries); RenderMetadata();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError("Could not read metadata: " + ex.Message); }
    }
    void RenderMetadata()
    {
        metadataRows.Children.Clear(); var query = search?.Text ?? ""; foreach (var group in metadata.Where(x => string.IsNullOrEmpty(query) || (x.Name + " " + x.Value).Contains(query, StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.Group))
        {
            metadataRows.Children.Add(PanelUi.Divider()); var heading = PanelUi.Text(group.Key, 11, PanelUi.Muted); heading.FontWeight = FontWeights.SemiBold; metadataRows.Children.Add(heading);
            foreach (var item in group) { var value = PanelUi.Text(item.Value.Length > 250 ? item.Value[..250] + "…" : item.Value, 12); value.ToolTip = item.Value; metadataRows.Children.Add(PanelUi.Row(item.Name, value, 155)); }
        }
    }
    void BuildFileOrder()
    {
        Body.MinHeight = 290; Body.Children.Add(PanelUi.Note(operation == "join" ? "Playback order" : "Document order")); Body.Children.Add(fileRows); RenderFileOrder();
    }
    void RenderFileOrder()
    {
        fileRows.Children.Clear(); for (int i = 0; i < Inputs.Length; i++)
        {
            int index = i; var grid = new Grid { Margin = new Thickness(12, 9, 10, 9) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.Children.Add(PanelUi.Text((i + 1).ToString(), 12, PanelUi.Muted)); var icon = PanelUi.Text(Catalog.Kind(Inputs[i]) == FileKind.Pdf ? "▤" : Catalog.Kind(Inputs[i]) == FileKind.Video ? "▣" : "▧", 24, PanelUi.Muted); Grid.SetColumn(icon, 1); grid.Children.Add(icon);
            var name = PanelUi.Text(Path.GetFileName(Inputs[i]), 13); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = Inputs[i]; Grid.SetColumn(name, 2); grid.Children.Add(name);
            var arrows = new StackPanel(); var up = PanelUi.Button("⌃", () => MoveInput(index, -1), "Move file up"); up.IsEnabled = i > 0; up.MinHeight = 20; up.Padding = new Thickness(5, 0, 5, 0); var down = PanelUi.Button("⌄", () => MoveInput(index, 1), "Move file down"); down.IsEnabled = i < Inputs.Length - 1; down.MinHeight = 20; down.Padding = new Thickness(5, 0, 5, 0); arrows.Children.Add(up); arrows.Children.Add(down); Grid.SetColumn(arrows, 3); grid.Children.Add(arrows);
            fileRows.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(238, 238, 238)), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 8), Child = grid });
        }
    }
    void MoveInput(int index, int delta) { var target = index + delta; if (target < 0 || target >= Inputs.Length) return; (Inputs[index], Inputs[target]) = (Inputs[target], Inputs[index]); RenderFileOrder(); if (operation == "collage") QueuePreview(); }
    void BuildCollage()
    {
        collagePreview = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, MaxHeight = 330 }; Body.Children.Add(new Border { Child = collagePreview, Background = Brushes.White, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 15), CornerRadius = new CornerRadius(10) });
        Combo("Columns", "CollageColumns", Enumerable.Range(1, Math.Min(8, Math.Max(2, Inputs.Length))).Select(i => (i.ToString(), (object)i)), Math.Min(1, Inputs.Length - 1));
        Combo("Canvas", "CollageRatio", new (string, object)[] { ("Square · 1:1", 1d), ("Landscape · 16:9", 16d / 9), ("Portrait · 4:5", 4d / 5) });
        Combo("Width", "CollageWidth", new (string, object)[] { ("1,024 px", 1024), ("2,048 px", 2048), ("4,096 px", 4096) }, 1);
        Combo("Image fit", "CollageFill", new (string, object)[] { ("Fit entire images", false), ("Fill cells · crop edges", true) });
        Slider("Spacing", "CollageGap", 24, 0, 100, "px"); ColorSwatches("Color", "Background", new[] { "#FFFFFF", "#1D1D1D", "#F3E7D3", "#FF5900" });
        Body.Children.Add(new Expander { Header = "Image order", Content = fileRows }); RenderFileOrder();
    }
    async Task RefreshCollage()
    {
        if (collagePreview == null || lifetime.IsCancellationRequested) return;
        try { var options = ReadOptions(); var inputs = Inputs.ToArray(); var version = ++renderVersion; var data = await Task.Run(() => { using var result = Engine.MakeCollage(inputs, options, lifetime.Token, Math.Min(1, 500.0 / options.CollageWidth)); return result.ToByteArray(MagickFormat.Png); }, lifetime.Token); if (version == renderVersion && !lifetime.IsCancellationRequested) collagePreview.Source = PanelUi.Bitmap(data); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex.Message); }
    }
    void BuildPdfSplit() { Body.Children.Add(PanelUi.Note(Path.GetFileName(input))); AddText("Pages", "Pages", ""); Body.Children.Add(PanelUi.Note("Leave blank for all pages, or enter a selection such as 1, 3–5. Each selected page is saved as a separate PDF.")); Body.MinHeight = 210; }
    void BuildPdfOrganizer() { Body.MinHeight = 420; Body.Children.Add(PanelUi.Note("Reorder, rotate, duplicate or remove pages.")); Body.Children.Add(pageCards); apply.IsEnabled = false; }
    async Task LoadPdfPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TangerinePanels", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var data = await Task.Run(() => { using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import); var files = PdfRaster.Render(input, directory, "png", lifetime.Token, 36, 100); return (Count: pdf.PageCount, Images: files.Select(File.ReadAllBytes).ToArray()); }, lifetime.Token);
            if (lifetime.IsCancellationRequested) return; pdfPageCount = data.Count; for (int i = 0; i < data.Images.Length; i++) pagePictures[i + 1] = PanelUi.Bitmap(data.Images[i]); Options.PageEdits = Enumerable.Range(1, data.Count).Select(i => new PdfPageChoice(i)).ToList(); RenderPages(); apply.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    void RenderPages()
    {
        pageCards.Children.Clear(); for (int i = 0; i < Options.PageEdits.Count; i++)
        {
            int index = i; var page = Options.PageEdits[i]; var card = new StackPanel(); var header = new Grid(); header.Children.Add(PanelUi.Text("Page " + page.Number, 11, PanelUi.Muted)); var remove = PanelUi.Button("×", () => { Options.PageEdits.RemoveAt(index); RenderPages(); }, "Remove page " + page.Number); remove.HorizontalAlignment = HorizontalAlignment.Right; remove.MinHeight = 20; remove.Padding = new Thickness(5, 0, 5, 0); header.Children.Add(remove); card.Children.Add(header);
            if (pagePictures.TryGetValue(page.Number, out var bitmap)) card.Children.Add(new System.Windows.Controls.Image { Source = bitmap, Width = 118, Height = 147, Stretch = Stretch.Uniform, Margin = new Thickness(0, 7, 0, 7), LayoutTransform = new RotateTransform(page.Rotation) }); else card.Children.Add(new TextBlock { Text = "PDF", Height = 147, VerticalAlignment = VerticalAlignment.Center });
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var action in new[] { ("‹", (Action)(() => MovePage(index, -1)), "Move page left"), ("↻", () => { Options.PageEdits[index] = page with { Rotation = (page.Rotation + 90) % 360 }; RenderPages(); }, "Rotate page"), ("⧉", () => { Options.PageEdits.Insert(index + 1, page); RenderPages(); }, "Duplicate page"), ("›", () => MovePage(index, 1), "Move page right") }) { var button = PanelUi.Button(action.Item1, action.Item2, action.Item3); button.Padding = new Thickness(7, 2, 7, 2); button.MinHeight = 25; button.Margin = new Thickness(0, 0, 2, 0); actions.Children.Add(button); }
            card.Children.Add(actions);
            pageCards.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), BorderBrush = PanelUi.Line, BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 12), Width = 154, Child = card });
        }
    }
    void MovePage(int index, int delta) { var target = index + delta; if (target < 0 || target >= Options.PageEdits.Count) return; (Options.PageEdits[index], Options.PageEdits[target]) = (Options.PageEdits[target], Options.PageEdits[index]); RenderPages(); }
}
