using System.Windows.Interop;
namespace Tangerine;
public static class InteractionTest
{
    public static async Task Run(App app, string directory)
    {
        Directory.CreateDirectory(directory); var reports = new List<object>();
        await WheelSoundAudit.Run(app, directory, reports);
        await DragActivationAudit.Run(app, directory, reports);
        using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Orange, 80, 60)) image.Write(Path.Combine(directory, "drag-fixture.png"));
        var source = new Window { Title = "Tangerine Windows integration check", Width = 250, Height = 100 };
        try { source.Show(); _ = WindowsShare.GetForWindow(new WindowInteropHelper(source).Handle); reports.Add(new { name = "Native Windows Share interop", passed = true }); }
        catch (Exception ex) { reports.Add(new { name = "Native Windows Share interop", passed = false, error = ex.ToString() }); }
        finally { source.Close(); }
        await NativeDragAudit.Run(app, directory, reports);
        await PanelFocusAudit.Run(app, Path.Combine(directory, "panel-focus"), reports);
        await DesktopAudit.Run(app, directory, reports);
        File.WriteAllText(Path.Combine(directory, "interaction-results.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
}
