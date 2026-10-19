namespace Tangerine;

public sealed class BatchWindow : MiniWindow
{
    readonly App app; readonly AppSettings settings; readonly StackPanel rows = new(), controls = new();
    readonly TextBlock summary = PanelUi.Text("", 14), settingsSummary = PanelUi.Note("");
    readonly ProgressBar progress = new() { Minimum = 0, Height = 4, Foreground = PanelUi.Orange, Background = PanelUi.Line, Margin = new Thickness(0, 10, 0, 14) };
    readonly Button start, cancel; readonly Dictionary<BatchEntry, (TextBlock Status, Button Open)> statusRows = new();
    readonly CancellationTokenSource lifetime = new(); bool running, finished, editing;
    public BatchPlan Plan { get; }
    internal Task Completion { get; private set; } = Task.CompletedTask;
    public BatchWindow(App app, string[] inputs, WheelItem item, WheelMode mode, AppSettings? settingsOverride = null) : base(item.Description + " · Batch", 640)
    {
        this.app = app; settings = settingsOverride ?? app.Settings; Plan = new(inputs, item, mode);
        summary.FontWeight = FontWeights.SemiBold; Body.Children.Add(summary);
        Body.Children.Add(PanelUi.Note("Process selected files together. Each file gets its own output."));
        var toolbar = new WrapPanel { Margin = new Thickness(0, 5, 0, 8) };
        if (mode == WheelMode.Tools && OptionsWindow.Needed(item.Id))
        {
            var all = PanelUi.Button("Settings for all…", () => _ = Edit(null)); all.ToolTip = "Choose settings once. This replaces any individual settings."; all.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(all);
        }
        toolbar.Children.Add(PanelUi.Button("Add files +", () => AddFiles(MainWindow.Browse(this)))); controls.Children.Add(toolbar);
        if (Plan.Spatial) { var scaling = new CheckBox { Content = "Scale the selection to each file's dimensions", IsChecked = true }; scaling.Checked += (_, _) => Plan.ScaleSelection = true; scaling.Unchecked += (_, _) => Plan.ScaleSelection = false; controls.Children.Add(scaling); }
        controls.Children.Add(settingsSummary); Body.Children.Add(controls); Body.Children.Add(progress); Body.Children.Add(rows);
        Body.Children.Add(PanelUi.Note("Outputs: " + (string.IsNullOrWhiteSpace(settings.OutputDirectory) ? "beside each original file" : settings.OutputDirectory) + ". Originals are kept. A failed file does not stop the remaining files."));
        start = PanelUi.Button("", () => { Completion = Start(); }); start.IsDefault = true; FooterRight.Children.Add(start);
        cancel = PanelUi.Button("Cancel", CancelOrClose); FooterLeft.Children.Add(cancel);
        Closing += (_, _) => lifetime.Cancel(); AllowDrop = true;
        DragOver += (_, e) => { e.Effects = !running && !finished && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); e.Handled = true; }; Render();
    }
    void CancelOrClose() { if (running) { lifetime.Cancel(); cancel.IsEnabled = false; } else Close(); }
    internal void AddFiles(string[] files)
    {
        if (running || finished || editing) return;
        foreach (var path in Catalog.Selection(files))
        {
            if (Plan.Entries.Any(e => string.Equals(e.Input, path, StringComparison.OrdinalIgnoreCase))) continue;
            if (Directory.Exists(path) || !(Plan.Mode == WheelMode.Convert ? Catalog.Extension(path) == Plan.Item.Id || Catalog.Conversions(path).Any(x => x.Id == Plan.Item.Id) : Catalog.ToolsForSelection(Plan.Entries.Select(x => x.Input).Append(path).ToArray()).Any(x => x.Id == Plan.Item.Id))) { ShowError($"{Path.GetFileName(path)} does not support {Plan.Item.Description} with this selection."); continue; }
            Plan.Entries.Add(new(path, Plan.Item));
        }
        Render();
    }
    async Task Edit(BatchEntry? entry)
    {
        if (running || finished || editing || Plan.Entries.Count == 0) return;
        editing = true; controls.IsEnabled = false; start.IsEnabled = false;
        try
        {
            var reference = entry ?? Plan.Entries[0]; var engine = new Engine(settings);
            OperationOptions? saved = entry?.CustomOptions?.Copy();
            if (saved == null && Plan.CommonOptions != null) saved = await Plan.OptionsFor(reference, engine, settings.Quality, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            var dialog = new OptionsWindow(new[] { reference.Input }, Plan.Item, settings, saved) { Owner = this };
            dialog.PrimaryAction.Content = dialog.ConfirmLabel = entry == null ? "Use for all files" : "Use for this file";
            if (dialog.ShowDialog() == true) { if (entry == null) await Plan.SetCommon(reference.Input, dialog.Options, engine, lifetime.Token); else entry.CustomOptions = dialog.Options.Copy(); ShowError(""); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { editing = false; controls.IsEnabled = true; Render(); }
    }
    void Render()
    {
        if (running) return;
        rows.Children.Clear(); statusRows.Clear();
        foreach (var entry in Plan.Entries)
        {
            var grid = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel(); var name = PanelUi.Text(Path.GetFileName(entry.Input), 13); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = entry.Input; labels.Children.Add(name);
            var state = PanelUi.Text(entry.CustomOptions != null ? "Custom settings" : Plan.CommonOptions != null ? "Shared settings" : Plan.NeedsSettings ? "Choose settings" : "Ready", 11, PanelUi.Muted); state.Margin = new Thickness(0, 5, 12, 0); labels.Children.Add(state); grid.Children.Add(labels);
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(buttons, 1); grid.Children.Add(buttons);
            if (Plan.Mode == WheelMode.Tools && OptionsWindow.Needed(Plan.Item.Id)) { var edit = PanelUi.Button("Edit", () => _ = Edit(entry), "Edit " + Path.GetFileName(entry.Input)); edit.Margin = new Thickness(0, 0, 6, 0); buttons.Children.Add(edit); }
            buttons.Children.Add(PanelUi.Button("×", () => { Plan.Entries.Remove(entry); Render(); }, "Remove " + Path.GetFileName(entry.Input)));
            var open = PanelUi.Button("Show output", () => ShowOutputs(entry)); open.Visibility = Visibility.Collapsed; buttons.Children.Add(open); statusRows[entry] = (state, open);
            rows.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromRgb(238, 238, 238)), Margin = new Thickness(0, 0, 0, 8), Child = grid });
        }
        summary.Text = $"{Plan.Entries.Count} files selected"; progress.Maximum = Math.Max(1, Plan.Entries.Count); progress.Value = 0;
        settingsSummary.Text = Plan.CommonOptions != null ? "Shared settings from " + Path.GetFileName(Plan.ReferenceFile) + ". Use Edit for individual changes." : Plan.NeedsSettings ? "Set options for all files, or edit files individually." : "Ready to process. Add or remove files before starting.";
        if (Plan.Item.Id is "trim" or "bleep" or "snapshot" or "split") settingsSummary.Text += " Times use seconds in each file; use Edit for different lengths.";
        start.Content = $"Apply to {Plan.Entries.Count} files"; start.IsEnabled = Plan.CanRun && !editing;
    }
    internal async Task Start()
    {
        if (running || finished || editing || !Plan.CanRun) return;
        running = true; controls.IsEnabled = false; start.IsEnabled = false; ShowError("");
        foreach (var row in rows.Children.OfType<Border>()) if (row.Child is Grid grid) foreach (var stack in grid.Children.OfType<StackPanel>()) foreach (var button in stack.Children.OfType<Button>()) button.Visibility = Visibility.Collapsed;
        try { await app.ExecuteBatch(Plan, settings, lifetime.Token, Update); }
        catch (OperationCanceledException) { foreach (var entry in Plan.Entries.Where(e => e.State == "Ready")) { entry.State = entry.Job.Status = "Cancelled"; Update(entry); } }
        catch (Exception ex) { ShowError(ex.Message); }
        finally
        {
            running = false; finished = true; cancel.Content = "Close"; cancel.IsEnabled = true;
            var done = Plan.Entries.Count(e => e.State == "Done"); var failed = Plan.Entries.Count(e => e.State == "Failed"); var cancelled = Plan.Entries.Count(e => e.State == "Cancelled");
            summary.Text = $"{done} completed · {failed} failed" + (cancelled > 0 ? $" · {cancelled} cancelled" : "");
            settingsSummary.Text = "Finished. Use Show output to open completed files in Explorer.";
            start.Visibility = Visibility.Collapsed;
        }
    }
    void Update(BatchEntry entry)
    {
        if (!statusRows.TryGetValue(entry, out var row)) return;
        row.Status.Text = entry.State == "Failed" ? "Failed · " + entry.Error : entry.State; row.Status.ToolTip = entry.Error;
        row.Status.Foreground = entry.State == "Failed" ? Brushes.Firebrick : entry.State == "Done" ? new SolidColorBrush(Color.FromRgb(69, 125, 85)) : PanelUi.Muted;
        row.Open.Visibility = entry.State == "Done" ? Visibility.Visible : Visibility.Collapsed;
        int completed = Plan.Entries.Count(e => e.State is "Done" or "Failed" or "Cancelled"); Motion.Number(progress, System.Windows.Controls.Primitives.RangeBase.ValueProperty, completed, 180); summary.Text = $"{completed} of {Plan.Entries.Count} finished";
        Motion.Reveal(row.Status, milliseconds: 140, opacity: .7); if (entry.State == "Done") Motion.Reveal(row.Open, milliseconds: 140);
    }
    void ShowOutputs(BatchEntry entry)
    {
        if (entry.Outputs.Count == 1) { App.Reveal(entry.Outputs[0]); return; }
        new BatchOutputsWindow(entry) { Owner = this }.ShowDialog();
    }
}
internal sealed class BatchOutputsWindow : MiniWindow
{
    internal BatchOutputsWindow(BatchEntry entry) : base("Outputs · " + Path.GetFileName(entry.Input), 500)
    {
        foreach (var output in entry.Outputs) { var button = PanelUi.Button(Path.GetFileName(output), () => App.Reveal(output)); button.Margin = new Thickness(0, 0, 0, 6); Body.Children.Add(button); }
        FooterRight.Children.Add(PanelUi.Button("Close", Close));
    }
}
