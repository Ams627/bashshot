using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const int ExtendedFrameBounds = 9;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0)
        {
            if (args is ["--help"] or ["-h"])
            {
                Console.WriteLine("Usage: bashshot   Copy the focused terminal window to the clipboard.");
                return 0;
            }

            Console.Error.WriteLine("Usage: bashshot");
            return 2;
        }

        nint window = GetForegroundWindow();
        var className = new StringBuilder(256);
        var result = GetClassNameW(window, className, className.Capacity);

        if (window == 0 || result == 0 ||
            className.ToString() is not ("mintty" or "CASCADIA_HOSTING_WINDOW" or "CASCADIA_HOSTING_WINDOW_CLASS" or "ConsoleWindowClass"))
        {
            Console.WriteLine($"window: {window}, className: {className}, result: {result}");
            return Fail("Focus Git Bash or its terminal window and run bashshot there.");
        }

        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out WindowRect frame,
                Marshal.SizeOf<WindowRect>()) < 0)
        {
            return Fail("Could not get the terminal window bounds.");
        }

        Rectangle bounds = Rectangle.Intersect(
            Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom),
            SystemInformation.VirtualScreen);

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return Fail("The terminal window has no visible area.");
        }

        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                if (GetForegroundWindow() != window)
                {
                    return Fail("The terminal lost focus before capture.");
                }

                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            Clipboard.SetImage(bitmap);
            return 0;
        }
        catch (ExternalException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"bashshot: {message}");
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint window, StringBuilder className, int maxCount);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(
        nint window, int attribute, out WindowRect bounds, int size);
}
