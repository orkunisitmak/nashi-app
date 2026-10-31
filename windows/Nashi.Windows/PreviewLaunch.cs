using System.Diagnostics;

namespace Nashi.Windows;

/// Opt-in build-time sandbox for a locally published visual preview.
/// Official release builds keep their normal single-instance and data paths.
internal static class PreviewLaunch
{
    internal static bool Enabled
    {
        get
        {
#if NASHI_VISUAL_PREVIEW
            return true;
#else
            return false;
#endif
        }
    }

    internal static string InstanceName=>"Nashi.FluentPreview.v1."+Process.GetCurrentProcess().SessionId;

    internal static string DataDirectory=>Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Nashi.FluentPreview");
}
