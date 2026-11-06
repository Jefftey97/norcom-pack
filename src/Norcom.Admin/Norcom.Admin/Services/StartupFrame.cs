using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Norcom.Admin.Services;

public static class StartupFrame
{
    public static readonly Color Backdrop = Color.FromRgb(0x07, 0x0A, 0x12);

    public static void Present(Window window)
    {
        window.Background = new SolidColorBrush(Backdrop);
        if (window.Background.CanFreeze)
        {
            window.Background.Freeze();
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Backdrop;
        }

        if (OperatingSystem.IsWindows())
        {
            PaintNative(hwnd);
        }

        var width = double.IsNaN(window.Width) ? 1180 : window.Width;
        var height = double.IsNaN(window.Height) ? 760 : window.Height;
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        window.Show();
    }

    private static void PaintNative(IntPtr hwnd)
    {
        var brush = CreateSolidBrush(0x00120A07);
        if (brush != IntPtr.Zero)
        {
            SetClassLongPtr(hwnd, -10, brush);
        }

        var dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        var caption = 0x00120A07;
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34, ref caption, sizeof(int));
        InvalidateRect(hwnd, IntPtr.Zero, true);
    }

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
}
