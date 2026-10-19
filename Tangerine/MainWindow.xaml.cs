namespace Tangerine;
public partial class MainWindow : Window
{
    readonly App app;
    string[] selected = Array.Empty<string>(); bool actions;
    public MainWindow(App app)
    {
        this.app = app; InitializeComponent(); CategoryList.ItemsSource = Catalog.Categories; CategoryList.SelectedIndex = 0;
        ConvertWheel.SelectionChanged += x => ConvertHint.Text = x?.Description ?? ""; ToolsWheel.SelectionChanged += x => ToolsHint.Text = x?.Description ?? "";
        ConvertWheel.Invoked += x => Invoke(x, actions ? WheelMode.Actions : WheelMode.Convert); ToolsWheel.Invoked += x => Invoke(x, WheelMode.Tools);
        JobList.ItemsSource = app.Jobs; app.Jobs.CollectionChanged += (_, _) => EmptyHistory.Visibility = app.Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHistory.Visibility = app.Jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Closing += (_, e) => { if (!app.Exiting && app.Settings.MinimizeToTray) { e.Cancel = true; Hide(); } else if (!app.Exiting) { e.Cancel = true; app.Exit(); } };
        Refresh();
    }
    public void Refresh()
    {
        if (ConvertWheel == null) return;
        var key = actions ? app.Settings.ActionsKey : app.Settings.ConvertKey;
        ConvertKey.Text = key.ToLowerInvariant(); ToolsKey.Text = app.Settings.ToolsKey.ToLowerInvariant();
        static string Glyph(string name) => name switch { "Shift" => "⇧", "Ctrl" => "⌃", _ => "⌥" };
        ConvertGlyph.Text = Glyph(key); ToolsGlyph.Text = Glyph(app.Settings.ToolsKey);
        var cat = CategoryList.SelectedItem as Category ?? Catalog.Categories[0]; var sample = selected.Length > 0 ? selected[0] : "sample." + cat.Sample;
        Heading.Text = actions ? "File actions" : selected.Select(Catalog.Kind).Distinct().Count() > 1 ? "Selected files" : cat.Heading; ConvertCaption.Text = actions ? "File actions" : "Convert formats";
        var tools = selected.Length > 0 ? Catalog.ToolsForSelection(selected) : Catalog.Tools(sample); bool pair = !actions && tools.Count > 0;
        ToolsPanel.Visibility = Divider.Visibility = pair ? Visibility.Visible : Visibility.Collapsed; Grid.SetColumnSpan(ConvertPanel, pair ? 1 : 3);
        ConvertWheel.SetItems(actions ? Catalog.Actions : selected.Length > 0 ? Catalog.ConversionsForSelection(selected) : Catalog.Conversions(sample)); ToolsWheel.SetItems(tools);
        SelectedTitle.Text = selected.Length == 0 ? "Drop your files here" : selected.Length == 1 ? Path.GetFileName(selected[0]) : $"{selected.Length} files selected";
        SelectedDetail.Text = selected.Length == 0 ? $"Or drag in Explorer, tap {key}, and drop onto a format." : actions ? "Choose a file action above." : selected.Length > 1 ? "Choose a format or tool for all files. You can review settings for each file." : "Click a format or tool above. Output: " + (string.IsNullOrEmpty(app.Settings.OutputDirectory) ? "beside your files." : app.Settings.OutputDirectory);
        SelectedTitle.ToolTip = string.Join(Environment.NewLine, selected);
        if (selected.Length > 0 && Catalog.Extension(selected[0]) == "pages") SelectedDetail.Text = "Export Apple Pages to DOCX on a Mac first. File actions are available here.";
        ClearButton.Visibility = selected.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    public void SetFiles(string[] files)
    {
        if (files.Length == 0) return;
        selected = Catalog.Selection(files);
        var cat = Catalog.Categories.FirstOrDefault(x => x.Kind == Catalog.Kind(files[0])); if (cat != null) CategoryList.SelectedItem = cat;
        actions = selected.Any(Directory.Exists) || (Catalog.ConversionsForSelection(selected).Count == 0 && Catalog.ToolsForSelection(selected).Count == 0); Refresh();
    }
    void CategoryChanged(object sender, SelectionChangedEventArgs e) { if (!IsInitialized) return; actions = false; if (selected.Length > 0 && CategoryList.SelectedItem is Category cat && Catalog.Kind(selected[0]) != cat.Kind) selected = Array.Empty<string>(); Refresh(); }
    public static string[] Browse(Window? owner = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Choose files to transform", Filter = "All files|*.*" }; return dlg.ShowDialog(owner) == true ? dlg.FileNames : Array.Empty<string>();
    }
    async void Invoke(WheelItem item, WheelMode mode)
    {
        if (selected.Length == 0)
        {
            var files = Browse(this); if (files.Length == 0) return; SetFiles(files);
            if (mode != WheelMode.Actions && !(mode == WheelMode.Convert ? Catalog.ConversionsForSelection(selected) : Catalog.ToolsForSelection(selected)).Any(x => x.Id == item.Id)) { app.ShowWheel(selected, mode); return; }
        }
        await app.Execute(selected, item, mode, this);
    }
    void BrowseClick(object s, RoutedEventArgs e) { var files = Browse(this); if (files.Length > 0) SetFiles(selected.Concat(files).ToArray()); }
    void ClearClick(object s, RoutedEventArgs e) { selected = Array.Empty<string>(); actions = false; Refresh(); }
    void SettingsClick(object s, RoutedEventArgs e) { new SettingsWindow(app) { Owner = this }.ShowDialog(); Refresh(); }
    void ActionsClick(object s, RoutedEventArgs e) { CategoryList.SelectedItem = null; actions = true; Refresh(); }
    void CancelClick(object s, RoutedEventArgs e) => app.CancelCurrent();
    void RevealClick(object s, RoutedEventArgs e) { if ((s as Button)?.Tag is Job j && j.Output != null) App.Reveal(j.Output); }
    void OnDragOver(object s, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    void OnDrop(object s, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) SetFiles(files); e.Effects = DragDropEffects.Copy; e.Handled = true; }
}
