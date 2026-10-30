using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Nashi.Linux;

/// The macOS success card (Sources/NashiApp/SuccessToast.swift): borderless, topmost, never takes focus,
/// bottom-center of the screen under the pointer. It stays 3 s (errors 6 s), then fades out over 0.28 s.
/// One at a time: presenting a new card closes the previous one.
public sealed partial class SuccessToast : Window
{
    private static class Copy
    {
        public const string Done = "Nicely done.";
        public const string DoneOne = "Nicely done. 1 file is ready.";
        public const string DoneMany = "Nicely done. {0} files are ready.";
        public const string Complete = "Your task is complete.";
    }

    public static readonly TimeSpan Visible = TimeSpan.FromSeconds(3), ErrorVisible = TimeSpan.FromSeconds(6), Fade = TimeSpan.FromSeconds(0.28);

    private readonly string? folder;
    private readonly TimeSpan visible;

    /// The card on screen, if any.
    public static SuccessToast? Current { get; private set; }

    public SuccessToast() : this("", null, null, true, Visible) { }

    private SuccessToast(string title, string? detail, string? folder, bool happy, TimeSpan visible)
    {
        InitializeComponent();
        this.folder = folder;
        this.visible = visible;
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailText.IsVisible = detail is not null;
        ShowButton.IsVisible = folder is not null;
        MarkImage.Source = happy ? MainWindow.HappyMark : MainWindow.Mark;
        Opened += (_, _) => Desktop.PlaceAtBottom(this);
        Closed += (_, _) => { if (Current == this) Current = null; };
    }

    public static string Heading(int count) => count == 0 ? L10n.T(Copy.Done) : count == 1 ? L10n.T(Copy.DoneOne) : L10n.F(Copy.DoneMany, count);

    /// Finished work: how many files, the first one's name, and Show in folder.
    public static SuccessToast Done(IReadOnlyList<string> outputs, string folder) =>
        new(Heading(outputs.Count), outputs.Count > 0 ? Path.GetFileName(outputs[0]) : L10n.T(Copy.Complete), folder, true, Visible);

    /// A notice or error in the same card; the text is already localized.
    public static SuccessToast Notice(string message) => new(message, null, null, false, ErrorVisible);

    public void Present()
    {
        Current?.Close();
        Current = this;
        Show();
        DispatcherTimer.RunOnce(() =>
        {
            Card.Opacity = 0;
            DispatcherTimer.RunOnce(Close, Fade);
        }, visible);
    }

    private void OnShowInFolder(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("xdg-open", [folder!]) { UseShellExecute = false })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { } // no xdg-open: nothing to show the folder with
        Close();
    }
}
