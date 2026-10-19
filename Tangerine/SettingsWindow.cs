namespace Tangerine;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(App app)
    {
        Title = "Tangerine settings"; Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = panel; panel.Children.Add(new TextBlock { Text = "Make it yours.", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 24) });
        var global = new CheckBox { Content = "Open the wheel while dragging in Explorer", IsChecked = app.Settings.GlobalDragEnabled, Margin = new Thickness(0, 0, 0, 13) }; panel.Children.Add(global);
        var tray = new CheckBox { Content = "Keep running in the tray when the window closes", IsChecked = app.Settings.MinimizeToTray, Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(tray);
        var sounds = new CheckBox { Content = "Play a sound when the wheel selection changes", IsChecked = app.Settings.WheelSoundsEnabled, Margin = new Thickness(0, 0, 0, 20) }; panel.Children.Add(sounds);
        if (!WheelSound.Available) { sounds.IsEnabled = false; sounds.ToolTip = "This build has no sound asset. See docs/ASSETS.md to build with a clip you have permission to use."; panel.Children.Add(new TextBlock { Text = "Wheel sound is not included in this build.", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, -12, 0, 12) }); }
        var combos = new List<ComboBox>(); foreach (var row in new[] { ("Conversions", app.Settings.ConvertKey), ("File tools", app.Settings.ToolsKey), ("File actions", app.Settings.ActionsKey) }) { var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) }; grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); grid.Children.Add(new TextBlock { Text = row.Item1, VerticalAlignment = VerticalAlignment.Center }); var box = new ComboBox { ItemsSource = new[] { "Shift", "Ctrl", "Alt" }, SelectedItem = row.Item2 }; Grid.SetColumn(box, 1); grid.Children.Add(box); panel.Children.Add(grid); combos.Add(box); }
        panel.Children.Add(new TextBlock { Text = "Save output to", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) });
        var output = new TextBox { Text = app.Settings.OutputDirectory }; panel.Children.Add(output); panel.Children.Add(new TextBlock { Text = "Leave blank to save beside the original file.", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 14) });
        var choose = new Button { Content = "Choose output folder…", HorizontalAlignment = HorizontalAlignment.Left }; choose.Click += (_, _) => { var picker = new Microsoft.Win32.OpenFolderDialog(); if (picker.ShowDialog(this) == true) output.Text = picker.FolderName; }; panel.Children.Add(choose);
        panel.Children.Add(new TextBlock { Text = "Media engine folder", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) }); var engine = new TextBox { Text = app.Settings.EngineDirectory }; panel.Children.Add(engine); panel.Children.Add(new TextBlock { Text = "Folder containing ffmpeg.exe and ffprobe.exe.", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 15) });
        var status = new TextBlock { Text = $"Images & PDFs  ✓    FFmpeg  {(app.Engine.FindTool("ffmpeg") != null ? "✓" : "not found")}\nWord  {(Engine.HasOffice("Word.Application") ? "✓" : "not installed")}    Excel  {(Engine.HasOffice("Excel.Application") ? "✓" : "not installed")}    PowerPoint  {(Engine.HasOffice("PowerPoint.Application") ? "✓" : "not installed")}", Foreground = Brushes.Gray, FontSize = 11, LineHeight = 22 }; panel.Children.Add(status);
        var error = new TextBlock { Foreground = Brushes.IndianRed, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var save = new Button { Content = "Save settings", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; save.Click += (_, _) =>
        {
            try
            {
                var candidate = new AppSettings
                {
                    ConvertKey = (string)combos[0].SelectedItem,
                    ToolsKey = (string)combos[1].SelectedItem,
                    ActionsKey = (string)combos[2].SelectedItem,
                    GlobalDragEnabled = global.IsChecked == true,
                    MinimizeToTray = tray.IsChecked == true,
                    WheelSoundsEnabled = sounds.IsChecked == true,
                    Quality = app.Settings.Quality,
                    OutputDirectory = output.Text.Trim(),
                    EngineDirectory = engine.Text.Trim()
                };
                candidate.Save(); app.Settings.CopyFrom(candidate); DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }; panel.Children.Add(save);
    }
}
