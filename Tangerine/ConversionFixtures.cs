namespace Tangerine;
internal static class ConversionFixtures
{
    static Task Sta(Action action) { var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var thread = new Thread(() => { try { action(); result.SetResult(); } catch (Exception ex) { result.SetException(ex); } }) { IsBackground = true }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task; }
    static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    internal static Task Word(string path, string text, string image) => Sta(() =>
    {
        dynamic app = Engine.CreateOffice("Word.Application"); dynamic? doc = null; object alerts = app.DisplayAlerts, security = app.AutomationSecurity;
        try
        {
            app.DisplayAlerts = 0; app.AutomationSecurity = 3; doc = app.Documents.Add(); dynamic body = doc.Content; body.Text = text; Release((object)body);
            dynamic range = doc.Range(doc.Content.End - 1, doc.Content.End - 1); dynamic table = doc.Tables.Add(range, 2, 2); table.Cell(1, 1).Range.Text = "Item"; table.Cell(1, 2).Range.Text = "Count"; table.Cell(2, 1).Range.Text = "Oranges"; table.Cell(2, 2).Range.Text = "42"; Release((object)table); Release((object)range);
            dynamic end = doc.Range(doc.Content.End - 1, doc.Content.End - 1); end.InsertParagraphAfter(); Release((object)end); dynamic pictureRange = doc.Range(doc.Content.End - 1, doc.Content.End - 1); dynamic picture = doc.InlineShapes.AddPicture(image, false, true, pictureRange); Release((object)picture); Release((object)pictureRange);
            doc.SaveAs2(path, Path.GetExtension(path) switch { ".doc" => 0, ".rtf" => 6, _ => 16 });
        }
        finally { try { doc?.Close(false); } finally { Release((object?)doc); app.DisplayAlerts = alerts; app.AutomationSecurity = security; if ((int)app.Documents.Count == 0) app.Quit(false); Release((object)app); } }
    });
    internal static Task Sheet(string path, string marker, string unicode) => Sta(() =>
    {
        dynamic app = Engine.CreateOffice("Excel.Application"); dynamic? book = null; object alerts = app.DisplayAlerts, security = app.AutomationSecurity;
        try { app.DisplayAlerts = false; app.AutomationSecurity = 3; book = app.Workbooks.Add(); dynamic sheet = book.Worksheets[1]; try { sheet.Cells[1, 1] = marker; sheet.Cells[1, 2] = unicode; sheet.Cells[2, 1] = "Oranges"; sheet.Cells[2, 2] = 42; sheet.Cells[3, 1] = "Apples"; sheet.Cells[3, 2] = 7; sheet.Cells[4, 1] = "Total"; sheet.Cells[4, 2].Formula = "=SUM(B2:B3)"; sheet.Columns[1].ColumnWidth = 32; sheet.Columns[2].ColumnWidth = 24; sheet.Calculate(); } finally { Release((object)sheet); } book.SaveAs(path, Path.GetExtension(path) == ".xls" ? 56 : 51); }
        finally { try { book?.Close(false); } finally { Release((object?)book); app.DisplayAlerts = alerts; app.AutomationSecurity = security; if ((int)app.Workbooks.Count == 0) app.Quit(); Release((object)app); } }
    });
    internal static Task Slides(string path, string marker, string unicode) => Sta(() =>
    {
        dynamic app = Engine.CreateOffice("PowerPoint.Application"); dynamic? deck = null; object alerts = app.DisplayAlerts, security = app.AutomationSecurity;
        try { app.DisplayAlerts = 1; app.AutomationSecurity = 3; deck = app.Presentations.Add(0); for (int i = 1; i <= 2; i++) { dynamic slide = deck.Slides.Add(i, 12); try { dynamic shape = slide.Shapes.AddTextbox(1, 40f, 40f, 500f, 250f); shape.TextFrame.TextRange.Text = marker + "\r" + unicode + "\rPage " + i; Release((object)shape); } finally { Release((object)slide); } } deck.SaveAs(path, Path.GetExtension(path) == ".ppt" ? 1 : 24); }
        finally { try { deck?.Close(); } finally { Release((object?)deck); app.DisplayAlerts = alerts; app.AutomationSecurity = security; if ((int)app.Presentations.Count == 0) app.Quit(); Release((object)app); } }
    });
}
