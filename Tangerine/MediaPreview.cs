using System.Windows.Threading;
using Orientation = System.Windows.Controls.Orientation;
namespace Tangerine;

internal sealed class MediaPreview : IDisposable
{
    readonly Engine engine; readonly string input; readonly CancellationToken token; readonly string directory;
    readonly MediaElement player = new() { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform, Visibility = Visibility.Hidden };
    readonly System.Windows.Controls.Image poster = new() { Stretch = Stretch.Uniform };
    readonly Button play; readonly Slider seek = new() { Minimum = 0, Maximum = 1 }; readonly TextBlock clock = PanelUi.Text("Loading…", 11, PanelUi.Muted);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    bool updating, playing, opened, disposed; double position; double? stopAt;
    string? originalPlayback; SelectionSurface? overlay;
    internal Border View { get; }
    internal StackPanel Transport { get; } = new();
    internal double Duration { get; private set; }
    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal BitmapSource? Poster { get; private set; }
    internal BitmapSource? Strip { get; private set; }
    internal double Position => opened ? player.Position.TotalSeconds : position;
    internal bool IsAudio => Catalog.Kind(input) == FileKind.Audio;
    internal bool IsPlaying => playing && opened;
    internal bool ChannelView { get; set; }
    internal event Action<string>? Error;
    internal MediaPreview(string input, AppSettings settings, CancellationToken token)
    {
        this.input = input; engine = new Engine(settings); this.token = token; directory = Path.Combine(Path.GetTempPath(), "TangerinePanels", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var grid = new Grid(); grid.Children.Add(poster); grid.Children.Add(player); View = new Border { Child = grid, Background = IsAudio ? new SolidColorBrush(Color.FromRgb(236, 236, 236)) : Brushes.Black, CornerRadius = new CornerRadius(12), Height = IsAudio ? 190 : 350, Margin = new Thickness(0, 0, 0, 14) };
        View.SizeChanged += (_, _) => grid.Clip = new RectangleGeometry(new Rect(0, 0, View.ActualWidth, View.ActualHeight), 12, 12);
        play = PanelUi.Button("▶", Toggle, "Play preview"); play.Width = 34; play.Background = Brushes.Transparent; play.Padding = new Thickness(4); play.IsEnabled = false;
        var row = new Grid { Margin = new Thickness(0, 3, 0, 7) }; row.Children.Add(play); play.HorizontalAlignment = HorizontalAlignment.Left; clock.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(clock); Transport.Children.Add(seek); Transport.Children.Add(row);
        seek.ValueChanged += (_, _) => { if (updating) return; position = seek.Value; if (opened) player.Position = TimeSpan.FromSeconds(position); UpdateClock(); };
        player.MediaOpened += (_, _) => { opened = true; play.IsEnabled = true; if (!playing) player.Pause(); player.Position = TimeSpan.FromSeconds(position); };
        player.MediaEnded += (_, _) => Pause(); player.MediaFailed += (_, _) => { opened = false; playing = false; play.Content = "▶"; player.Visibility = Visibility.Hidden; Error?.Invoke("Playback preview is unavailable for this file. You can still select a time and apply the tool."); };
        timer.Tick += (_, _) => { if (!opened || !playing) return; position = player.Position.TotalSeconds; updating = true; seek.Value = position; updating = false; UpdateClock(); if (stopAt.HasValue && position >= stopAt.Value) { Pause(); stopAt = null; } };
    }
    internal async Task Load()
    {
        var bounds = await engine.MediaBounds(input, token); Duration = bounds.Duration; Width = bounds.Width; Height = bounds.Height; seek.Maximum = Math.Max(.01, Duration);
        if (Duration <= 0) throw new InvalidDataException("This file has no playable duration.");
        var image = Path.Combine(directory, "poster.png");
        if (IsAudio) { var pcm = Path.Combine(directory, "waveform.pcm"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", input, "-ac", "2", "-ar", "8000", "-f", "s16le", pcm }, token); Poster = AudioWaveform.Render(pcm, ChannelView); }
        else await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-ss", Math.Min(Duration * .2, .7).ToString("0.###", CultureInfo.InvariantCulture), "-i", input, "-frames:v", "1", "-vf", "scale=1000:700:force_original_aspect_ratio=decrease", image }, token);
        token.ThrowIfCancellationRequested(); if (!IsAudio) Poster = PanelUi.Bitmap(await File.ReadAllBytesAsync(image, token)); poster.Source = Poster; Strip = Poster;
        if (overlay != null) { overlay.SetImage(Poster!, Width, Height, 410, 330); overlay.HidePicture(); View.Height = overlay.Height + 18; }
        if (!IsAudio)
        {
            var frames = new List<BitmapSource>(); for (int i = 0; i < 6; i++) { var frame = Path.Combine(directory, $"strip-{i}.png"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-ss", (Duration * i / 6).ToString("0.###", CultureInfo.InvariantCulture), "-i", input, "-frames:v", "1", "-vf", "scale=160:90:force_original_aspect_ratio=increase,crop=160:90", frame }, token); frames.Add(PanelUi.Bitmap(await File.ReadAllBytesAsync(frame, token))); }
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) for (int i = 0; i < frames.Count; i++) dc.DrawImage(frames[i], new Rect(i * 160, 0, 160, 90)); var strip = new RenderTargetBitmap(960, 90, 96, 96, PixelFormats.Pbgra32); strip.Render(visual); strip.Freeze(); Strip = strip;
        }
        var playback = input;
        if (IsAudio) { playback = Path.Combine(directory, "audio.wav"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", input, "-vn", "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", playback }, token); }
        else if (Catalog.Extension(input) is not ("mp4" or "mov" or "wmv")) { playback = Path.Combine(directory, "preview.mp4"); await Engine.Process(engine.Ffmpeg, new[] { "-v", "error", "-nostdin", "-i", input, "-vf", "scale=960:540:force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28", "-c:a", "aac", playback }, token); }
        token.ThrowIfCancellationRequested(); originalPlayback = playback; player.Source = new Uri(playback); player.Play(); player.Pause(); timer.Start(); play.IsEnabled = true; UpdateClock();
    }
    internal void AddOverlay(SelectionSurface selection) { overlay = selection; ((Grid)View.Child).Children.Add(selection); View.Padding = new Thickness(9); }
    internal void PlayOriginal() { if (originalPlayback == null) return; Pause(); opened = false; position = 0; player.Source = new Uri(originalPlayback); Play(); }
    internal void SetSpeed(double value) => player.SpeedRatio = value;
    internal void Seek(double seconds) { position = Math.Clamp(seconds, 0, Duration); seek.Value = position; if (opened) player.Position = TimeSpan.FromSeconds(position); UpdateClock(); }
    internal void PlayRange(double start, double end) { Seek(start); stopAt = end; Play(); }
    internal void Toggle() { if (playing) Pause(); else Play(); }
    void Play() { playing = true; player.Visibility = IsAudio ? Visibility.Hidden : Visibility.Visible; player.Play(); play.Content = "Ⅱ"; }
    void Pause() { playing = false; player.Pause(); play.Content = "▶"; }
    void UpdateClock() => clock.Text = $"{Time(Position)} / {Time(Duration)}";
    internal static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss\.ff", CultureInfo.InvariantCulture);
    internal async Task UseProcessedAudio(string operation, OperationOptions options)
    {
        Pause(); var output = Path.Combine(directory, "processed-" + Guid.NewGuid().ToString("N") + ".wav"); var args = new List<string> { "-v", "error", "-nostdin", "-i", input };
        if (operation == "bleep") args.AddRange(new[] { "-filter_complex", Engine.BleepFilter(options), "-map", "[out]" });
        else args.AddRange(new[] { "-af", operation == "normalize" ? Engine.LoudnessFilter(options) : Engine.ChannelFilter(options) });
        args.AddRange(new[] { "-ar", "44100", "-c:a", "pcm_s16le", output }); await Engine.Process(engine.Ffmpeg, args, token); token.ThrowIfCancellationRequested(); opened = false; position = 0; player.Source = new Uri(output); Play();
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); player.Close(); _ = Task.Run(async () => { for (int i = 0; i < 5; i++) { try { if (Directory.Exists(directory)) Directory.Delete(directory, true); return; } catch (IOException) { await Task.Delay(300); } catch (UnauthorizedAccessException) { return; } } }); }
}
