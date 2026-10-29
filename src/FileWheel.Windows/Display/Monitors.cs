using FileWheel.Core.Input;
using static FileWheel.Windows.Native.NativeMethods;

namespace FileWheel.Windows.Display;

/// <summary>Ecrans connectes, en pixels physiques, avec leur DPI effectif.</summary>
public static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(), szDevice = "" };
            if (GetMonitorInfoW(h, ref mi))
            {
                uint dpi = 96;
                try { if (GetDpiForMonitor(h, 0, out var dx, out _) == 0) dpi = dx; } catch { }
                list.Add(new MonitorInfo(
                    new PixelRect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
                    new PixelRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
                    (int)dpi));
            }
            return true;
        }, IntPtr.Zero);
        if (list.Count == 0) list.Add(new MonitorInfo(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96));
        return list;
    }

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }
}
