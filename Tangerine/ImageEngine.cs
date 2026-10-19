using ImageMagick;
using ImageMagick.Drawing;
namespace Tangerine;
public sealed partial class Engine
{
    static List<string> ConvertImage(string input, string target, string format, OperationOptions options)
    {
        if (format == "pdf") { ImagesToPdf(new[] { input }, target); return new() { target }; }
        using var images = new MagickImageCollection(input);
        foreach (var image in images) { image.AutoOrient(); image.Quality = (uint)options.Quality; }
        // A decoded AVIF retains its AV1 encoder settings. Normalize through PNG
        // before HEIF encoding so ImageMagick selects the HEVC encoder.
        void Write(IMagickImage<byte> image, string path)
        {
            if (format == "heic" && Catalog.Extension(input) == "avif")
            {
                using var normalized = new MagickImage(image.ToByteArray(MagickFormat.Png)); normalized.Quality = (uint)options.Quality; normalized.Write(path);
            }
            else image.Write(path);
        }
        if (images.Count == 1) { Write(images[0], target); return new() { target }; }
        if (format == "tiff") { images.Write(target); return new() { target }; }
        var directory = Path.Combine(Path.GetDirectoryName(target)!, Catalog.Stem(input) + " " + format.ToUpperInvariant()); Directory.CreateDirectory(directory);
        for (int i = 0; i < images.Count; i++) Write(images[i], Path.Combine(directory, $"Page {i + 1:000}.{format}"));
        return new() { directory };
    }
    List<string> ImageTool(string input, string target, string id, OperationOptions o)
    {
        using (var frames = new MagickImageCollection(input))
        {
            if (frames.Count > 1)
            {
                if (id is not ("compress" or "metadata")) throw new NotSupportedException("For editing, convert this multi-page image to separate PNG pages first.");
                foreach (var frame in frames) { frame.AutoOrient(); if (id == "metadata" || o.StripMetadata) frame.Strip(); if (id == "compress") { frame.Quality = (uint)o.Quality; if (o.MaxDimension > 0 && (frame.Width > o.MaxDimension || frame.Height > o.MaxDimension)) frame.Resize((uint)o.MaxDimension, (uint)o.MaxDimension); } }
                frames.Write(target); return new() { target };
            }
        }
        using var image = new MagickImage(input); image.AutoOrient();
        if (id is "annotate" or "background" or "redact") target = Path.ChangeExtension(target, "png");
        switch (id)
        {
            case "compress": image.Quality = (uint)o.Quality; if (o.MaxDimension > 0 && (image.Width > o.MaxDimension || image.Height > o.MaxDimension)) image.Resize((uint)o.MaxDimension, (uint)o.MaxDimension); if (o.StripMetadata) image.Strip(); break;
            case "metadata": image.Strip(); break;
            case "crop":
                ValidateRectangle(image.Width, image.Height, o); image.Crop(new MagickGeometry(o.X, o.Y, (uint)o.Width, (uint)o.Height)); image.ResetPage(); break;
            case "redact":
                ValidateRectangle(image.Width, image.Height, o); RedactImage(image, o); image.Strip(); break;
            case "edit": AdjustImage(image, o); break;
            case "annotate":
                AnnotateImage(image, o); break;
            case "background":
                using (var background = FrameImage(image, o)) background.Write(target); return new() { target };
            default: throw new NotSupportedException(id);
        }
        image.Write(target); return new() { target };
    }
    internal static void AdjustImage(MagickImage image, OperationOptions o) { image.Modulate(new Percentage(Math.Clamp(o.Brightness * Math.Pow(2, o.Exposure), 0, 1000)), new Percentage(o.Saturation), new Percentage(100 + o.Hue / 1.8)); if (o.Contrast != 0) image.BrightnessContrast(new Percentage(0), new Percentage(o.Contrast)); image.Rotate(o.Rotation); }
    internal static void RedactImage(MagickImage image, OperationOptions o) { if (o.BlurRedaction) { using var region = image.Clone(); region.Crop(new MagickGeometry(o.X, o.Y, (uint)o.Width, (uint)o.Height)); region.ResetPage(); region.Blur(0, Math.Max(5, Math.Min(o.Width, o.Height) / 6.0)); image.Composite(region, o.X, o.Y, CompositeOperator.Over); } else new Drawables().FillColor(new MagickColor(o.RedactionColor)).StrokeColor(new MagickColor(o.RedactionColor)).Rectangle(o.X, o.Y, o.X + o.Width - 1, o.Y + o.Height - 1).Draw(image); }
    internal static void AnnotateImage(MagickImage image, OperationOptions o)
    {
        var color = new MagickColor(o.Color); var draw = new Drawables().StrokeColor(color).StrokeWidth(Math.Max(2, o.FontSize / 10.0)).FillColor(MagickColors.Transparent);
        if (o.AnnotationShape == "Text") draw.Font("Segoe UI").FontPointSize(o.FontSize).StrokeColor(MagickColors.Transparent).FillColor(color).Text(o.X, Math.Max(o.FontSize, o.Y), o.Text);
        else if (o.AnnotationShape == "Rectangle") draw.Rectangle(o.X, o.Y, o.X + o.Width, o.Y + o.Height);
        else if (o.AnnotationShape == "Ellipse") draw.Ellipse(o.X + o.Width / 2.0, o.Y + o.Height / 2.0, o.Width / 2.0, o.Height / 2.0, 0, 360);
        else { double x = o.X + o.Width, y = o.Y + o.Height, a = Math.Atan2(o.Height, o.Width), length = Math.Max(10, o.FontSize * .7); draw.Line(o.X, o.Y, x, y).Line(x, y, x - length * Math.Cos(a - .5), y - length * Math.Sin(a - .5)).Line(x, y, x - length * Math.Cos(a + .5), y - length * Math.Sin(a + .5)); }
        draw.Draw(image);
    }
    internal static MagickImage FrameImage(MagickImage image, OperationOptions o)
    {
        double w = image.Width + o.Padding * 2.0, h = image.Height + o.Padding * 2.0; if (o.BackgroundRatio > 0) { if (w / h < o.BackgroundRatio) w = h * o.BackgroundRatio; else h = w / o.BackgroundRatio; }
        var background = new MagickImage($"gradient:{o.Color}-{o.BackgroundEnd}", (uint)Math.Ceiling(w), (uint)Math.Ceiling(h));
        if (o.Radius > 0) { using var mask = new MagickImage(MagickColors.Transparent, image.Width, image.Height); new Drawables().FillColor(MagickColors.White).StrokeColor(MagickColors.White).RoundRectangle(0, 0, image.Width - 1, image.Height - 1, o.Radius, o.Radius).Draw(mask); image.HasAlpha = true; image.Composite(mask, CompositeOperator.DstIn); }
        if (o.Shadow > 0) { using var shadow = image.Clone(); shadow.BackgroundColor = MagickColors.Black; shadow.Shadow(0, (int)(o.Shadow * .3), o.Shadow * .5, new Percentage(24)); background.Composite(shadow, Gravity.Center, CompositeOperator.Over); }
        background.Composite(image, Gravity.Center, CompositeOperator.Over); return background;
    }
    static void ValidateRectangle(uint width, uint height, OperationOptions o)
    {
        if (o.Width <= 0 || o.Height <= 0 || o.X < 0 || o.Y < 0 || o.X + (long)o.Width > width || o.Y + (long)o.Height > height) throw new ArgumentException($"Choose a rectangle inside the {width} × {height} image.");
    }
}
