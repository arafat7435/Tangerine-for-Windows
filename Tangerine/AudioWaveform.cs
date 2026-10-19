namespace Tangerine;
internal static class AudioWaveform
{
    internal static BitmapSource Render(string pcm, bool stereo)
    {
        const int bars = 132, width = 1000, height = 260; var peaks = new double[2, bars];
        using (var stream = File.OpenRead(pcm)) using (var reader = new BinaryReader(stream))
        {
            long frames = stream.Length / 4; for (long frame = 0; frame < frames; frame++) { var bucket = Math.Min(bars - 1, (int)(frame * bars / Math.Max(1, frames))); for (int c = 0; c < 2; c++) peaks[c, bucket] = Math.Max(peaks[c, bucket], Math.Abs((double)reader.ReadInt16())); }
        }
        var max = Math.Max(1, peaks.Cast<double>().Max()); var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            int rows = stereo ? 2 : 1; for (int c = 0; c < rows; c++)
            {
                var rowHeight = height / (double)rows; double left = stereo ? 65 : 12, available = width - left - 12, center = c * rowHeight + rowHeight / 2;
                if (stereo) { var label = new FormattedText(c == 0 ? "L" : "R", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, PanelUi.Muted, 1); dc.DrawText(label, new Point(20, center - 15)); }
                for (int i = 0; i < bars; i++) { double level = stereo ? peaks[c, i] : Math.Max(peaks[0, i], peaks[1, i]); double barHeight = Math.Max(2, level / max * (rowHeight - 35)); var x = left + i * available / bars; dc.DrawRoundedRectangle(PanelUi.Orange, null, new Rect(x, center - barHeight / 2, 3.5, barHeight), 1.75, 1.75); }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
