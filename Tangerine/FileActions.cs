using System.Collections.Specialized;
using System.Windows.Interop;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Clipboard = System.Windows.Clipboard;
namespace Tangerine;
public static class FileActions
{
    public static async Task Execute(App app, string[] files, string action, Window? owner)
    {
        if (files.Length == 0 || files.Any(p => !File.Exists(p) && !Directory.Exists(p))) throw new FileNotFoundException("Choose files that are still available.");
        switch (action)
        {
            case "copypath": Clipboard.SetText(string.Join(Environment.NewLine, files)); break;
            case "copy": var collection = new StringCollection(); collection.AddRange(files); Clipboard.SetFileDropList(collection); break;
            case "terminal": Process.Start(TerminalInfo(files[0])); break;
            case "float": new ShelfWindow(files).Show(); break;
            case "move":
                var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Move originals to…" }; if (picker.ShowDialog(owner) != true) return;
                MoveFiles(files, picker.FolderName); break;
            case "share":
                app.ShowMain(); var window = Application.Current.MainWindow; var hwnd = new WindowInteropHelper(window).Handle;
                var manager = WindowsShare.GetForWindow(hwnd);
                Windows.Foundation.TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
                handler = async (_, args) => { manager.DataRequested -= handler; var deferral = args.Request.GetDeferral(); try { var items = new List<IStorageItem>(); foreach (var file in files) { if (Directory.Exists(file)) items.Add(await StorageFolder.GetFolderFromPathAsync(file)); else items.Add(await StorageFile.GetFileFromPathAsync(file)); } args.Request.Data.Properties.Title = "Share with Tangerine"; args.Request.Data.SetStorageItems(items); } catch (Exception ex) { args.Request.FailWithDisplayText(ex.Message); } finally { deferral.Complete(); } };
                manager.DataRequested += handler; try { WindowsShare.Show(hwnd); } catch { manager.DataRequested -= handler; throw; }
                break;
            default: throw new NotSupportedException("Unknown file action: " + action);
        }
        await Task.CompletedTask;
    }
    internal static ProcessStartInfo TerminalInfo(string path) => new("powershell.exe") { UseShellExecute = true, WorkingDirectory = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path))! };
    internal static List<string> MoveFiles(string[] files, string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The destination folder is no longer available.");
        var sources = files.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var plan = new List<(string Source, string Target)>(); var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("The source is no longer available.", source);
            var prefix = source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Directory.Exists(source) && (directory.Equals(source, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || sources.Any(other => other != source && other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))) throw new IOException("Do not move a folder inside itself or select both a folder and its contents.");
            var target = Path.Combine(directory, Path.GetFileName(source));
            if (source.Equals(target, StringComparison.OrdinalIgnoreCase)) { plan.Add((source, source)); continue; }
            var candidate = Engine.Unique(target); int n = 2;
            var extension = target.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(target);
            while (reserved.Contains(candidate)) candidate = Engine.Unique(Path.Combine(directory, Catalog.Stem(target) + " " + (n++) + extension));
            reserved.Add(candidate); plan.Add((source, candidate));
        }
        static void Move(string source, string target)
        {
            if (Directory.Exists(source))
            {
                if (Path.GetPathRoot(source)!.Equals(Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase)) Directory.Move(source, target);
                else
                {
                    if (File.Exists(target) || Directory.Exists(target)) throw new IOException("The destination name is already in use: " + target);
                    try { Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(source, target, false); }
                    catch (Exception error)
                    {
                        // Cross-volume directory moves can fail after moving only
                        // some children. Recover this entry before the batch rollback.
                        try { if (Directory.Exists(target)) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(target, source, false); }
                        catch (Exception recovery) { throw new IOException($"The folder move could not finish. Some files remain at: {target}. Original folder: {source}.", new AggregateException(error, recovery)); }
                        throw;
                    }
                }
            }
            else File.Move(source, target, false);
        }
        var moved = new List<(string Source, string Target)>();
        try { foreach (var entry in plan) { if (entry.Source.Equals(entry.Target, StringComparison.OrdinalIgnoreCase)) continue; Move(entry.Source, entry.Target); moved.Add(entry); } }
        catch (Exception error) { var recovery = new List<string>(); foreach (var entry in moved.AsEnumerable().Reverse()) { try { Move(entry.Target, entry.Source); } catch { recovery.Add(entry.Target); } } if (recovery.Count > 0) throw new IOException("The move could not finish. Some files remain at: " + string.Join(", ", recovery), error); throw; }
        return plan.Select(x => x.Target).ToList();
    }
}
internal static class WindowsShare
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShareInterop
    {
        IntPtr GetForWindow(IntPtr hwnd, ref Guid iid);
        void ShowShareUIForWindow(IntPtr hwnd);
    }
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string text, int length, out IntPtr value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IntPtr factory);
    static IShareInterop Factory()
    {
        const string name = "Windows.ApplicationModel.DataTransfer.DataTransferManager"; Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out var str));
        try { var iid = typeof(IShareInterop).GUID; Marshal.ThrowExceptionForHR(RoGetActivationFactory(str, ref iid, out var pointer)); try { return (IShareInterop)Marshal.GetObjectForIUnknown(pointer); } finally { Marshal.Release(pointer); } }
        finally { WindowsDeleteString(str); }
    }
    internal static DataTransferManager GetForWindow(IntPtr hwnd) { var factory = Factory(); try { var iid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C"); var p = factory.GetForWindow(hwnd, ref iid); try { return WinRT.MarshalInspectable<DataTransferManager>.FromAbi(p); } finally { Marshal.Release(p); } } finally { Marshal.ReleaseComObject(factory); } }
    internal static void Show(IntPtr hwnd) { var factory = Factory(); try { factory.ShowShareUIForWindow(hwnd); } finally { Marshal.ReleaseComObject(factory); } }
}
public sealed class ShelfWindow : MiniWindow
{
    readonly ListBox list = new() { BorderThickness = new Thickness(0), Background = Brushes.Transparent, SelectionMode = SelectionMode.Extended }; readonly ObservableCollection<string> files = new(); Point start;
    public ShelfWindow(string[] paths) : base("Floating Shelf", 370)
    {
        Title = "Tangerine · Floating shelf"; Topmost = true; AllowDrop = true;
        Body.Children.Add(PanelUi.Note("Drop files here. Drag them into another app when you need them."));
        var clear = PanelUi.Button("Clear Shelf", () => files.Clear()); FooterRight.Children.Add(clear);
        list.Height = 220; list.ItemsSource = files; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.ItemContainerStyle = new Style(typeof(ListBoxItem)); list.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); list.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        list.ItemTemplate = System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:local='clr-namespace:Tangerine;assembly=Tangerine'><DataTemplate.Resources><local:ShelfFileNameConverter x:Key='FileName'/></DataTemplate.Resources><Border Padding='12,13' Margin='0,3' CornerRadius='9' Background='#EEEEEE'><TextBlock Text='{Binding Converter={StaticResource FileName}}' TextTrimming='CharacterEllipsis' FontSize='12' ToolTip='{Binding}'/></Border></DataTemplate>") as DataTemplate; Body.Children.Add(list);
        foreach (var path in paths) files.Add(path); DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }; Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] additions) foreach (var p in additions) if (!files.Contains(p)) files.Add(p); e.Handled = true; };
        list.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(list); list.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(list) - start).Length > 8 && list.SelectedItems.Count > 0) { var data = new DataObject(DataFormats.FileDrop, list.SelectedItems.Cast<string>().ToArray()); DragDrop.DoDragDrop(list, data, DragDropEffects.Copy); } };
    }
}
public sealed class ShelfFileNameConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value?.ToString() ?? ""; var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)); return string.IsNullOrEmpty(name) ? path : name;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
