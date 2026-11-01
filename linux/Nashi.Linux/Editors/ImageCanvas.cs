using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;

namespace Nashi.Linux.Editors;

/// The editor preview (macOS ImageEditPreview): the image fitted on a near-black rounded stage, the panel's marks over
/// it, and the pointer in source pixels.
public sealed class ImageCanvas : Control
{
    const double Inset = 18;
    private Bitmap? bitmap;
    private Size space = new(1, 1);
    private bool dragging;

    public EditorPanel? Panel { get; set; }
    /// Whether the panel takes the pointer (off while the result is shown).
    public bool Editing { get; set; }

    /// Shows `image`, whose coordinate space is `size` source pixels (the image may be a smaller copy).
    public void Show(Bitmap? image, Size size)
    {
        if (!ReferenceEquals(bitmap, image) && bitmap is not null && !Keep(bitmap)) bitmap.Dispose();
        bitmap = image;
        space = size;
        InvalidateVisual();
    }

    /// The bitmap the window keeps for Original (not disposed when replaced).
    public Bitmap? Kept { get; set; }
    bool Keep(Bitmap b) => ReferenceEquals(b, Kept) || ReferenceEquals(b, Panel?.Frame);

    /// Source pixels → control coordinates.
    public Matrix View
    {
        get
        {
            var area = new Rect(Bounds.Size).Deflate(Inset);
            var zoom = Math.Min(area.Width / space.Width, area.Height / space.Height);
            if (!double.IsFinite(zoom) || zoom <= 0) zoom = 1;
            var x = area.X + (area.Width - space.Width * zoom) / 2;
            var y = area.Y + (area.Height - space.Height * zoom) / 2;
            return Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(x, y);
        }
    }

    public double Zoom => View.M11;

    /// Control coordinates → source pixels.
    public Point ToImage(Point point) => View.Invert().Transform(point);

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF0, 0, 0, 0)), null, new RoundedRect(new Rect(Bounds.Size), 20));
        if (bitmap is null) return;
        var view = View;
        var target = new Rect(space).TransformToAABB(view);
        context.DrawImage(bitmap, new Rect(bitmap.Size), target);
        if (Editing) Panel?.Draw(context, view, view.M11);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!Editing || Panel is not { Interactive: true } panel || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        dragging = true;
        e.Pointer.Capture(this);
        panel.Press(ToImage(e.GetPosition(this)), e.KeyModifiers, 10 / Zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!Editing || Panel is not { Interactive: true } panel) return;
        var point = ToImage(e.GetPosition(this));
        if (dragging) { panel.Drag(point, e.KeyModifiers); InvalidateVisual(); }
        else Cursor = new Cursor(panel.Cursor(point, 10 / Zoom));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!dragging || Panel is null) return;
        dragging = false;
        e.Pointer.Capture(null);
        Panel.Release(ToImage(e.GetPosition(this)), e.KeyModifiers);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (dragging && Panel is not null) { dragging = false; Panel.Release(new Point(double.NaN, double.NaN), KeyModifiers.None); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    /// Straight RGBA pixels of a Magick image (any thread).
    public static (byte[] Pixels, int Width, int Height) Pixels(MagickImage image)
    {
        using var copy = (MagickImage)image.Clone();
        copy.Alpha(AlphaAction.Set);
        return (copy.GetPixelsUnsafe().ToByteArray(PixelMapping.RGBA) ?? [], (int)copy.Width, (int)copy.Height);
    }

    /// An Avalonia bitmap of straight RGBA pixels (UI thread).
    public static WriteableBitmap Bitmap((byte[] Pixels, int Width, int Height) image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < image.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(image.Pixels, y * image.Width * 4, buffer.Address + y * buffer.RowBytes, image.Width * 4);
        return bitmap;
    }
}
