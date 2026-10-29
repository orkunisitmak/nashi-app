namespace Nashi.Linux;

internal static class Paths
{
    /// $XDG_DATA_HOME/Nashi (usually ~/.local/share/Nashi): Library, preferences
    /// and optional user tools, outside the replaceable application folder.
    internal static string Data=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolderOption.DoNotVerify),"Nashi");
}
