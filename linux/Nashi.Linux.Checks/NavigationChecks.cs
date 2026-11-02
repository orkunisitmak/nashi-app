using Avalonia;
using Avalonia.Threading;
using Nashi.Core;
using Nashi.Linux;

static class NavigationChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(XdndTarget.EventType(new IntPtr(unchecked((long)0x71BC123400000021))) == 33
            && XdndTarget.EventType(new IntPtr(unchecked((long)0x59AD56780000001F))) == 31,
            "Native ClientMessage and SelectionNotify types ignore nonzero XEvent padding on 64-bit Linux");
        var fixture = Path.GetTempFileName();
        try
        {
            var protocol = new XdndProtocol((_, _) => true);
            DropContent? hovered = null;
            protocol.Hover += (content, _, _) => hovered = content;
            protocol.Enter(["text/uri-list"]);
            protocol.Position(80, 90);
            protocol.Reply(System.Text.Encoding.UTF8.GetBytes(new Uri(fixture).AbsoluteUri));
            check(protocol.Content.Any && hovered is { Any: true }, "A stationary drag fills the wheel when its file data arrives, without another position message");
        }
        finally { File.Delete(fixture); }
        var create = XdndTarget.Create;
        var read = XdndTarget.ReadDrag;
        // The source owns the pointer grab: no ordinary motion or XDND hover reaches the overlay.
        XdndTarget.Create = (_, _, _, _, _, _, _) => new CancellationTokenSource();
        XdndTarget.ReadDrag = _ => null;
        var sampler = new PointerSampler(new GestureDetector { Key = Modifiers.None });
        var flow = new WheelFlow(new Toolchain("/tmp/nashi-navigation-no-tools", ""), null, _ => { }, () => Task.CompletedTask) { Sampler = sampler };
        try
        {
            flow.ShowForDrag(new PixelPoint(400, 300));
            var window = flow.LastWheel!;
            window.Know(new DropContent(["/tmp/nashi-navigation.pdf"], []));
            Dispatcher.UIThread.RunJobs();
            var wheel = window.Wheel;
            void Sample(Point point)
            {
                var local = wheel.TranslatePoint(point, window)!.Value;
                var scaling = window.ScreenRect().Scaling;
                sampler.Sample(window.Position.X + (int)Math.Round(local.X * scaling), window.Position.Y + (int)Math.Round(local.Y * scaling), PointerSampler.ControlMask | PointerSampler.Button1Mask);
                Dispatcher.UIThread.RunJobs();
            }
            Point Petal(int index)
            {
                var angle = ((wheel.Segments.Count == 3 ? 180 : 90) - index * 360.0 / wheel.Segments.Count) * Math.PI / 180;
                return new(wheel.Bounds.Width / 2 + 100 * Math.Cos(angle), wheel.Bounds.Height / 2 - 100 * Math.Sin(angle));
            }
            void Rest(Point point, int milliseconds)
            {
                var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
                do { Sample(point); Thread.Sleep(10); } while (DateTime.UtcNow < until);
                Dispatcher.UIThread.RunJobs();
            }
            foreach (var index in Enumerable.Range(0, wheel.Segments.Count).Where(i => wheel.Segments[i].Group is not null).ToArray())
            {
                Rest(Petal(index), 400);
                check(wheel.OpenGroupIndex == index, "Ctrl-drag pointer samples open " + wheel.Segments[index].Title + " without XDND motion");
                if (wheel.OpenGroupIndex != index) continue;
                var span = Wheel.SubSpan(360.0 / wheel.Segments.Count, wheel.SubItems.Count);
                var angle = Wheel.SubMid((wheel.Segments.Count == 3 ? 180 : 90) - index * 360.0 / wheel.Segments.Count, 0, wheel.SubItems.Count, span) * Math.PI / 180;
                var radius = (Wheel.SubInner + Wheel.SubOuter) / 2;
                Sample(new(wheel.Bounds.Width / 2 + radius * Math.Cos(angle), wheel.Bounds.Height / 2 - radius * Math.Sin(angle)));
                check(wheel.SubHighlightedIndex == 0, "Ctrl-drag reaches the outer format ring of " + wheel.Segments[index].Title);
                Sample(new(wheel.Bounds.Width / 2, wheel.Bounds.Height / 2));
            }
            flow.FlipDrag();
            check(wheel.Sides[wheel.Side].Key == "tools" && wheel.Segments.All(i => !i.Id.StartsWith("side:")), "Ctrl-drag Tab switches directly to Tools with no navigation petal");
            flow.FlipDrag();
            check(wheel.Sides[wheel.Side].Key == "convert", "The next Ctrl-drag Tab returns to Convert");
            window.Close();
            Sample(Petal(0)); // A closed wheel must ignore later samples.

            // A source can answer after the first short read, without ever sending an XDND hover.
            var attempts = 0;
            XdndTarget.ReadDrag = _ => Interlocked.Increment(ref attempts) == 1 ? null
                : new DropContent(["/tmp/nashi-navigation.pdf"], []);
            flow.ShowForDrag(new PixelPoint(400, 300));
            var pending = flow.LastWheel!;
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!pending.Known && DateTime.UtcNow < deadline) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            check(pending.Known && pending.Wheel.Sides.Count > 0, "A source that misses the first read still fills the drag wheel, without an XDND hover");
            pending.Close();
        }
        finally
        {
            flow.LastWheel?.Close();
            XdndTarget.Create = create;
            XdndTarget.ReadDrag = read;
        }
    }
}
