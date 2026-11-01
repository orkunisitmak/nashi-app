using System.IO;
using System.Runtime.InteropServices;

namespace Nashi.Core;

public static class MicrosoftOffice
{
    public static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                var wordType = Type.GetTypeFromProgID("Word.Application");
                return wordType is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool CanHandle(string inExt, string outFormat)
    {
        inExt = inExt.ToLowerInvariant();
        outFormat = outFormat.ToLowerInvariant();
        if (inExt is "docx" or "doc" or "rtf" or "odt" or "txt" or "html")
        {
            return outFormat is "pdf" or "docx" or "txt" or "html" or "rtf";
        }
        if (inExt is "xlsx" or "xls" or "csv" or "tsv" or "ods")
        {
            return outFormat is "pdf" or "xlsx" or "csv" or "tsv";
        }
        if (inExt is "pptx" or "ppt")
        {
            return outFormat is "pdf" or "pptx" or "jpg" or "png";
        }
        return false;
    }

    public static bool TryConvert(string input, string format, string output)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var inExt = Path.GetExtension(input).TrimStart('.').ToLowerInvariant();
        var fullIn = Path.GetFullPath(input);
        var fullOut = Path.GetFullPath(output);

        try
        {
            if (inExt is "docx" or "doc" or "rtf" or "odt" or "txt" or "html")
            {
                return ConvertWord(fullIn, format, fullOut);
            }
            if (inExt is "xlsx" or "xls" or "csv" or "tsv" or "ods")
            {
                return ConvertExcel(fullIn, format, fullOut);
            }
            if (inExt is "pptx" or "ppt")
            {
                return ConvertPowerPoint(fullIn, format, fullOut);
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static bool ConvertWord(string input, string format, string output)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var type = Type.GetTypeFromProgID("Word.Application");
        if (type is null) return false;
        dynamic? app = null;
        dynamic? doc = null;
        try
        {
            app = Activator.CreateInstance(type);
            if (app is null) return false;
            app.Visible = false;
            app.DisplayAlerts = 0;
            doc = app.Documents.Open(input, ReadOnly: true, Visible: false);
            int formatCode = format.ToLowerInvariant() switch
            {
                "pdf" => 17,
                "docx" => 16,
                "doc" => 0,
                "txt" => 2,
                "html" => 8,
                "rtf" => 6,
                _ => -1
            };
            if (formatCode < 0) return false;
            doc.SaveAs2(output, formatCode);
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (doc is not null) { doc.Close(false); Marshal.FinalReleaseComObject(doc); } } catch { }
            try { if (app is not null) { app.Quit(); Marshal.FinalReleaseComObject(app); } } catch { }
        }
    }

    private static bool ConvertExcel(string input, string format, string output)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var type = Type.GetTypeFromProgID("Excel.Application");
        if (type is null) return false;
        dynamic? app = null;
        dynamic? wb = null;
        try
        {
            app = Activator.CreateInstance(type);
            if (app is null) return false;
            app.Visible = false;
            app.DisplayAlerts = false;
            wb = app.Workbooks.Open(input, ReadOnly: true);
            var fmt = format.ToLowerInvariant();
            if (fmt == "pdf")
            {
                wb.ExportAsFixedFormat(0, output);
            }
            else
            {
                int formatCode = fmt switch
                {
                    "xlsx" => 51,
                    "csv" => 6,
                    "tsv" => -4158,
                    _ => -1
                };
                if (formatCode == -1) return false;
                wb.SaveAs(output, formatCode);
            }
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (wb is not null) { wb.Close(false); Marshal.FinalReleaseComObject(wb); } } catch { }
            try { if (app is not null) { app.Quit(); Marshal.FinalReleaseComObject(app); } } catch { }
        }
    }

    private static bool ConvertPowerPoint(string input, string format, string output)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var type = Type.GetTypeFromProgID("PowerPoint.Application");
        if (type is null) return false;
        dynamic? app = null;
        dynamic? pres = null;
        try
        {
            app = Activator.CreateInstance(type);
            if (app is null) return false;
            pres = app.Presentations.Open(input, ReadOnly: 1, Untitled: 0, WithWindow: 0);
            int formatCode = format.ToLowerInvariant() switch
            {
                "pdf" => 32,
                "pptx" => 11,
                "jpg" => 17,
                "png" => 18,
                _ => -1
            };
            if (formatCode < 0) return false;
            pres.SaveAs(output, formatCode);
            return File.Exists(output) && new FileInfo(output).Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (pres is not null) { pres.Close(); Marshal.FinalReleaseComObject(pres); } } catch { }
            try { if (app is not null) { app.Quit(); Marshal.FinalReleaseComObject(app); } } catch { }
        }
    }
}
