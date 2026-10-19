using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
namespace Tangerine;
public sealed partial class Engine
{
    public static void ImagesToPdf(string[] inputs, string target)
    {
        using var doc = new PdfDocument();
        foreach (var p in inputs) { using var images = new ImageMagick.MagickImageCollection(p); foreach (var image in images) { image.AutoOrient(); using var stream = new MemoryStream(image.ToByteArray(ImageMagick.MagickFormat.Png)); using var ximage = XImage.FromStream(stream); var page = doc.AddPage(); page.Width = XUnit.FromPoint(image.Width * .75); page.Height = XUnit.FromPoint(image.Height * .75); using var gfx = XGraphics.FromPdfPage(page); gfx.DrawImage(ximage, 0, 0, page.Width.Point, page.Height.Point); } }
        doc.Save(target);
    }
    public static void MergePdf(string[] inputs, string target)
    {
        using var result = new PdfDocument(); foreach (var input in inputs) { using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import); foreach (var page in source.Pages) result.AddPage(page); }
        result.Save(target);
    }
    public static int[] PageOrder(string text, int count)
    {
        if (string.IsNullOrWhiteSpace(text)) return Enumerable.Range(1, count).ToArray();
        var pages = new List<int>(); foreach (var segment in text.Split(','))
        {
            var parts = segment.Trim().Split('-'); if (parts.Length > 2 || !int.TryParse(parts[0], out int start)) throw new ArgumentException("Use page numbers such as 1, 3-5, 2.");
            int end = start; if (parts.Length == 2 && !int.TryParse(parts[1], out end)) throw new ArgumentException("Invalid page range.");
            if (start < 1 || start > count || end < 1 || end > count) throw new ArgumentException($"Page numbers must be between 1 and {count}.");
            int step = end >= start ? 1 : -1; for (int p = start; ; p += step) { pages.Add(p); if (p == end) break; }
        }
        return pages.ToArray();
    }
    static List<string> PdfTool(string input, string target, string id, OperationOptions o)
    {
        if (id is "split" or "organize")
        {
            using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            if (id == "split")
            {
                var folder = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(input) + " Pages"); Directory.CreateDirectory(folder);
                foreach (var p in PageOrder(o.Pages, source.PageCount)) { using var page = new PdfDocument(); page.AddPage(source.Pages[p - 1]); page.Save(Unique(Path.Combine(folder, $"Page {p:000}.pdf"))); }
                return new() { folder };
            }
            using var organized = new PdfDocument(); var order = o.PageEdits.Count > 0 ? o.PageEdits : PageOrder(o.Pages, source.PageCount).Select(p => new PdfPageChoice(p, o.Rotation)).ToList(); foreach (var choice in order) { if (choice.Number < 1 || choice.Number > source.PageCount || choice.Rotation % 90 != 0) throw new ArgumentException("Choose valid PDF pages and rotations."); var page = organized.AddPage(source.Pages[choice.Number - 1]); page.Rotate = ((page.Rotate + choice.Rotation) % 360 + 360) % 360; }
            organized.Save(target);
        }
        else
        {
            if (id is not ("compress" or "metadata")) throw new NotSupportedException("This PDF tool is not supported.");
            using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            if (id == "metadata") { doc.Info.Elements.Clear(); doc.Internals.Catalog.Elements.Remove("/Metadata"); }
            doc.Options.CompressContentStreams = true; doc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression; doc.Save(target);
        }
        return new() { target };
    }
    async Task<List<string>> PdfConvert(string input, string target, string format, CancellationToken ct)
    {
        if (format is "png" or "jpg")
        {
            var dir = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(input) + " " + format.ToUpperInvariant()); Directory.CreateDirectory(dir); PdfRaster.Render(input, dir, format, ct); return new() { dir };
        }
        if (format is "txt" or "md" or "epub")
        {
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(input); var text = string.Join("\n\n", pdf.GetPages().Select(p => p.Text));
            if (string.IsNullOrWhiteSpace(text)) throw new NotSupportedException("This PDF has no extractable text. Use PNG, JPG, DOCX or PPTX to keep its scanned pages. OCR is not available.");
            if (format == "epub") WriteEpub(text, target, Path.GetFileNameWithoutExtension(input)); else File.WriteAllText(target, text, new UTF8Encoding(false)); return new() { target };
        }
        if (format is "pptx" or "xlsx") return await PdfToOffice(input, target, format, ct);
        // Word's PDF import can show a modal prompt even when DisplayAlerts is off.
        // Extract text first; image-only PDFs become a document of page images.
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(input))
        {
            var text = string.Join("\n\n", pdf.GetPages().Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p)));
            if (!string.IsNullOrWhiteSpace(text)) { var txt = Path.Combine(Path.GetDirectoryName(target)!, "pdf-content.txt"); File.WriteAllText(txt, text, new UTF8Encoding(false)); return await OfficeConvert(txt, target, format, FileKind.Document, ct); }
        }
        var images = Path.Combine(Path.GetDirectoryName(target)!, "pdf-pages"); Directory.CreateDirectory(images); var rendered = PdfRaster.Render(input, images, "png", ct, 144);
        var html = Path.Combine(Path.GetDirectoryName(target)!, "pdf-content.html"); File.WriteAllText(html, "<html><body>" + string.Join("", rendered.Select((p, i) => $"<p{(i == 0 ? "" : " style='page-break-before:always'")}><img src='{new Uri(p).AbsoluteUri}' width='600'></p>")) + "</body></html>");
        return await OfficeConvert(html, target, format, FileKind.Document, ct);
    }
}

public static class PdfRaster
{
    static readonly object Gate = new(); static bool initialized;
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_InitLibrary();
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string path, string? password);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDFBitmap_CreateEx(int w, int h, int format, IntPtr buffer, int stride);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDFBitmap_FillRect(IntPtr bmp, int left, int top, int w, int h, uint color);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_RenderPageBitmap(IntPtr bmp, IntPtr page, int x, int y, int w, int h, int rotate, int flags);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] static extern void FPDFBitmap_Destroy(IntPtr bmp);
    public static List<string> Render(string input, string directory, string format, CancellationToken ct, int dpi = 300, int maxPages = int.MaxValue)
    {
        lock (Gate)
        {
            if (!initialized) { FPDF_InitLibrary(); initialized = true; }
            var doc = FPDF_LoadDocument(input, null); if (doc == IntPtr.Zero) throw new InvalidOperationException("Cannot open this PDF. It may be damaged or password-protected.");
            var output = new List<string>(); try
            {
                for (int i = 0; i < Math.Min(maxPages, FPDF_GetPageCount(doc)); i++)
                {
                    ct.ThrowIfCancellationRequested(); var page = FPDF_LoadPage(doc, i); if (page == IntPtr.Zero) throw new InvalidOperationException("Cannot render PDF page.");
                    try
                    {
                        int w = (int)Math.Ceiling(FPDF_GetPageWidth(page) * dpi / 72), h = (int)Math.Ceiling(FPDF_GetPageHeight(page) * dpi / 72); if (w <= 0 || h <= 0 || (long)w * h > 80_000_000) throw new InvalidOperationException("This PDF page is too large to render at 300 DPI.");
                        int size = checked(w * h * 4); var memory = Marshal.AllocHGlobal(size); try
                        {
                            var bmp = FPDFBitmap_CreateEx(w, h, 4, memory, w * 4); if (bmp == IntPtr.Zero) throw new OutOfMemoryException(); try
                            {
                                FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF); FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, 1);
                                var image = BitmapSource.Create(w, h, dpi, dpi, PixelFormats.Bgra32, null, memory, size, w * 4); image.Freeze(); BitmapEncoder encoder = format == "jpg" ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); var file = Path.Combine(directory, $"Page {i + 1:000}.{format}"); using var stream = File.Create(file); encoder.Save(stream); output.Add(file);
                            }
                            finally { FPDFBitmap_Destroy(bmp); }
                        }
                        finally { Marshal.FreeHGlobal(memory); }
                    }
                    finally { FPDF_ClosePage(page); }
                }
            }
            finally { FPDF_CloseDocument(doc); }
            return output;
        }
    }
}
