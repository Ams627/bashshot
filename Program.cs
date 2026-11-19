using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

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
        string windowClass = className.ToString();

        if (window == 0 || result == 0 ||
            windowClass is not ("mintty" or "CASCADIA_HOSTING_WINDOW" or "CASCADIA_HOSTING_WINDOW_CLASS" or "ConsoleWindowClass"))
        {
            Console.WriteLine($"window: {window}, className: {className}, result: {result}");
            return Fail("Focus Git Bash or its terminal window and run bashshot there.");
        }

        Rectangle area;
        if (windowClass.StartsWith("CASCADIA_HOSTING_WINDOW", StringComparison.Ordinal))
        {
            // Windows Terminal: capture only the terminal content, not the tab bar.
            if (GetTerminalContentBounds(window) is not { } content)
            {
                return Fail("Could not find the terminal content area in Windows Terminal.");
            }

            area = content;
        }
        else
        {
            if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out WindowRect frame,
                    Marshal.SizeOf<WindowRect>()) < 0)
            {
                return Fail("Could not get the terminal window bounds.");
            }

            area = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        }

        Rectangle bounds = Rectangle.Intersect(area, SystemInformation.VirtualScreen);

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

            // Windows 11 rounds window corners with an 8px radius at 96 DPI; allow a little extra.
            int cornerSize = (int)Math.Ceiling(10 * GetDpiForWindow(window) / 96.0);
            Size trimmed = GetTrimmedSize(bitmap, cornerSize);
            if (trimmed == bitmap.Size)
            {
                Clipboard.SetImage(bitmap);
            }
            else
            {
                using Bitmap cropped = bitmap.Clone(new Rectangle(Point.Empty, trimmed), bitmap.PixelFormat);
                Clipboard.SetImage(cropped);
            }

            return 0;
        }
        catch (ExternalException ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Returns the size of the image with the trailing border removed from the right and
    /// bottom: every right-most column and bottom-most row made up entirely of the border
    /// colour is trimmed. The border colour is taken from the right-most column, just above
    /// the bottom-right corner. Squares of <paramref name="cornerSize"/> pixels at the two
    /// bottom corners are ignored, because Windows 11 draws rounded window corners there.
    /// If nothing but border is found, the original size is returned.
    /// </summary>
    private static Size GetTrimmedSize(Bitmap bitmap, int cornerSize)
    {
        const int BytesPerPixel = 3; // Format24bppRgb
        int width = bitmap.Width;
        int height = bitmap.Height;
        cornerSize = Math.Min(cornerSize, Math.Min(width, height) / 4);

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = data.Stride;
            var pixels = new byte[stride * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int sample = (height - 1 - cornerSize) * stride + (width - 1) * BytesPerPixel;
            byte b = pixels[sample], g = pixels[sample + 1], r = pixels[sample + 2];

            bool IsContent(int x, int y)
            {
                if (y >= height - cornerSize && (x < cornerSize || x >= width - cornerSize))
                {
                    return false;
                }

                int i = y * stride + x * BytesPerPixel;
                return pixels[i] != b || pixels[i + 1] != g || pixels[i + 2] != r;
            }

            // Right-most column containing content.
            int maxX = -1;
            for (int x = width - 1; x >= 0 && maxX < 0; x--)
            {
                for (int y = 0; y < height; y++)
                {
                    if (IsContent(x, y))
                    {
                        maxX = x;
                        break;
                    }
                }
            }

            if (maxX < 0)
            {
                return bitmap.Size;
            }

            // Bottom-most row containing content, within the remaining columns.
            int maxY = -1;
            for (int y = height - 1; y >= 0 && maxY < 0; y--)
            {
                for (int x = 0; x <= maxX; x++)
                {
                    if (IsContent(x, y))
                    {
                        maxY = y;
                        break;
                    }
                }
            }

            return new Size(maxX + 1, maxY + 1);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// Returns the screen bounds of the visible terminal panes in a Windows Terminal
    /// window, excluding the tab bar and title bar. With split panes, the result is
    /// the union of all panes in the active tab.
    /// </summary>
    private static Rectangle? GetTerminalContentBounds(nint window)
    {
        try
        {
            AutomationElement root = AutomationElement.FromHandle(window);
            AutomationElementCollection controls = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "TermControl"));

            Rectangle? union = null;
            foreach (AutomationElement control in controls)
            {
                AutomationElement.AutomationElementInformation info = control.Current;
                System.Windows.Rect rect = info.BoundingRectangle;
                if (info.IsOffscreen || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                {
                    continue;
                }

                double right = rect.Right;

                // Exclude the pane's vertical scrollbar, which sits at its right edge.
                AutomationElement? scrollBar = control.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ScrollBar));
                if (scrollBar is not null)
                {
                    System.Windows.Rect bar = scrollBar.Current.BoundingRectangle;
                    if (!bar.IsEmpty && bar.Width > 0 && bar.Left > rect.Left && bar.Left < right)
                    {
                        right = bar.Left;
                    }
                }

                var pane = Rectangle.FromLTRB(
                    (int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
                    (int)Math.Round(right), (int)Math.Round(rect.Bottom));
                union = union is { } u ? Rectangle.Union(u, pane) : pane;
            }

            return union;
        }
        catch (ElementNotAvailableException)
        {
            return null;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(
        nint window, int attribute, out WindowRect bounds, int size);
}
