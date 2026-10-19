namespace Tangerine;
public sealed partial class OptionsWindow
{
    readonly StackPanel selections = new(); TextBlock? rangeLabel;
    void BuildMedia()
    {
        media = new MediaPreview(input, settings, lifetime.Token) { ChannelView = operation == "channels" }; media.Error += ShowError;
        if (operation is "crop" or "redact")
        {
            surface = new SelectionSurface { Editable = true, IsRedaction = operation == "redact", ShadeOutside = operation == "crop", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }; surface.SelectionChanged += r => SetRect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            media.AddOverlay(surface); imageFrame = media.View; Body.Children.Add(media.View);
        }
        else { if (kind == FileKind.Audio && operation is "trim" or "bleep") { media.View.Height = 0; media.View.Margin = new Thickness(0); } Body.Children.Add(media.View); }
    }
    async Task InitializeMedia()
    {
        try
        {
            await media!.Load(); if (lifetime.IsCancellationRequested) return; imageWidth = media.Width; imageHeight = media.Height;
            if (surface != null && media.Poster != null) { SetRect(0, 0, imageWidth, imageHeight); if (operation == "redact") SetRect(imageWidth / 4, imageHeight / 4, imageWidth / 2, imageHeight / 3); }
            if (fields.ContainsKey("Duration")) Set("Duration", media.Duration.ToString("0.###", CultureInfo.InvariantCulture));
            if (timeline != null) { timeline.Duration = media.Duration; timeline.Picture = media.Strip; timeline.SetRange(0, media.Duration); timeline.InvalidateVisual(); UpdateRange(); }
            apply.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested) ShowError(ex.Message); }
    }
    void BuildMediaControls()
    {
        if (media == null) { AddNumber("Start · seconds", "Start", 0); AddNumber("Duration · seconds", "Duration", 10); return; }
        switch (operation)
        {
            case "trim":
            case "bleep":
                Field("Start", 0); Field("Duration", 10); rangeLabel = PanelUi.Text("Drag the ends to trim", 11, PanelUi.Muted); rangeLabel.Margin = new Thickness(0, 0, 0, 8); Body.Children.Add(rangeLabel);
                timeline = new RangeTimeline { Height = kind == FileKind.Audio ? 135 : 74, Margin = new Thickness(0, 0, 0, 9) }; timeline.RangeChanged += (start, end) => { Set("Start", start.ToString("0.###", CultureInfo.InvariantCulture)); Set("Duration", (end - start).ToString("0.###", CultureInfo.InvariantCulture)); media.Seek(start); UpdateRange(); }; Body.Children.Add(timeline);
                var times = new Grid(); var left = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left }; left.Children.Add(PanelUi.Text("Start", 11, PanelUi.Muted)); fields["Start"].Margin = new Thickness(8, 0, 0, 0); left.Children.Add(fields["Start"]); times.Children.Add(left); var right = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; right.Children.Add(PanelUi.Text("Duration", 11, PanelUi.Muted)); fields["Duration"].Margin = new Thickness(8, 0, 0, 0); fields["Duration"].ToolTip = "Seconds"; right.Children.Add(fields["Duration"]); times.Children.Add(right); Body.Children.Add(times);
                Body.Children.Add(media.Transport); Body.Children.Add(PanelUi.Button("▶  Play Selected Range", () => media.PlayRange(Number("Start"), Number("Start") + Number("Duration", media.Duration))));
                if (operation == "bleep")
                {
                    Body.Children.Add(PanelUi.Divider()); Body.Children.Add(PanelUi.Button("+  Add Bleep Range", () => { Options.BleepRanges.Add(new TimeRange(Number("Start"), Math.Min(media.Duration, Number("Start") + Number("Duration")))); RenderSelections(); })); Body.Children.Add(selections); AddAudioPreviewButton("▶  Preview with Bleeps");
                }
                break;
            case "speed":
                Slider("Playback speed", "Speed", 1, .25, 4, "×", .25); Body.Children.Add(Choice(new[] { "0.25×", "0.5×", "1×", "1.5×", "2×", "4×" }, "Speed", new object[] { .25, .5, 1d, 1.5, 2d, 4d }, 2)); fields["Speed"].TextChanged += (_, _) => media.SetSpeed(Math.Clamp(Number("Speed", 1), .25, 8)); Body.Children.Add(media.Transport); break;
            case "normalize":
                Body.Children.Add(media.Transport); Slider("Target loudness", "TargetLoudness", -16, -36, -5, "LUFS"); Slider("Loudness range", "LoudnessRange", 11, 1, 20, "LU"); Slider("True peak limit", "TruePeak", -1.5, -9, 0, "dBTP", .1); Body.Children.Add(PanelUi.Note("−16 LUFS is a balanced default for spoken audio and music. The true peak limit helps prevent clipping.")); AddAudioPreviewButton("▶  Preview Normalized Audio"); break;
            case "channels":
                Body.Children.Add(media.Transport); Field("Channels", 2); Body.Children.Add(PanelUi.Row("Output", Choice(new[] { "Mono", "Stereo" }, "Channels", new object[] { 1, 2 }, 1))); Slider("Left volume", "LeftVolume", 100, 0, 200, "%"); Slider("Right volume", "RightVolume", 100, 0, 200, "%"); AddAudioPreviewButton("▶  Play Output"); break;
            case "snapshot":
                Field("Start", 0); Body.Children.Add(media.Transport); Body.Children.Add(PanelUi.Divider()); Body.Children.Add(PanelUi.Button("+  Add Frame", () => { var time = Math.Min(media.Position, Math.Max(0, media.Duration - .001)); if (!Options.SnapshotTimes.Contains(time)) Options.SnapshotTimes.Add(time); RenderSelections(); })); Body.Children.Add(selections); Body.Children.Add(PanelUi.Note("Choose a time, then add a frame. With no selection, the current frame is saved.")); break;
            case "split":
                Field("Duration", 10); Body.Children.Add(media.Transport); timeline = new RangeTimeline { RangeEnabled = false, Height = 58 }; Body.Children.Add(timeline);
                Body.Children.Add(PanelUi.Button("✂  Add Split", () => { var time = media.Position; if (time > .01 && time < media.Duration - .01 && !Options.SplitPoints.Any(p => Math.Abs(p - time) < .02)) { Options.SplitPoints.Add(time); timeline.Markers.Add(time); timeline.InvalidateVisual(); RenderSelections(); } }));
                Body.Children.Add(selections); AddNumber("Clip duration · sec", "Duration", 10); Body.Children.Add(PanelUi.Note("Add split points, or leave the timeline empty to split into equal-duration clips.")); break;
            case "visualize":
                media.View.Background = Brushes.Black; Body.Children.Add(media.Transport); Section("Output"); Body.Children.Add(PanelUi.Row("Style", PanelUi.Text("Orange waveform"))); Body.Children.Add(PanelUi.Row("Canvas", PanelUi.Text("Landscape · 1280 × 720"))); Body.Children.Add(PanelUi.Note("Creates an MP4 with the original audio and an animated waveform.")); break;
        }
    }
    void UpdateRange() { if (rangeLabel != null && timeline != null) rangeLabel.Text = $"Drag the ends to {(operation == "bleep" ? "select a bleep" : "trim")}                 {MediaPreview.Time(timeline.End - timeline.Start)} selected"; }
    void RenderSelections()
    {
        selections.Children.Clear();
        if (operation == "bleep") foreach (var range in Options.BleepRanges.ToArray()) selections.Children.Add(PanelUi.Row($"{MediaPreview.Time(range.Start)} – {MediaPreview.Time(range.End)}", PanelUi.Button("×", () => { Options.BleepRanges.Remove(range); RenderSelections(); }, "Remove bleep range")));
        var values = operation == "snapshot" ? Options.SnapshotTimes : Options.SplitPoints;
        if (operation is "snapshot" or "split") foreach (var time in values.Order().ToArray()) selections.Children.Add(PanelUi.Row(MediaPreview.Time(time), PanelUi.Button("×", () => { values.Remove(time); timeline?.Markers.Remove(time); timeline?.InvalidateVisual(); RenderSelections(); }, "Remove selected time")));
        if (operation == "snapshot") apply.Content = ConfirmLabel ?? (Options.SnapshotTimes.Count == 0 ? "Save Current Frame" : $"Save {Options.SnapshotTimes.Count} Frame{(Options.SnapshotTimes.Count == 1 ? "" : "s")}");
    }
    void AddAudioPreviewButton(string title) { var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) }; var original = PanelUi.Button("Original", () => media!.PlayOriginal()); original.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(original); var button = new Button { Content = title }; button.Click += async (_, _) => { button.IsEnabled = false; try { await media!.UseProcessedAudio(operation, ReadOptions()); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex.Message); } finally { button.IsEnabled = true; } }; row.Children.Add(button); Body.Children.Add(row); }
}
