using System.Runtime.InteropServices;

namespace DemonsSoulsLauncher;

public sealed record ResolutionChoice(int Width, int Height)
{
    public override string ToString() => $"{Width}×{Height}";
}


public static class DisplayModes
{
    public static IReadOnlyList<(int Width, int Height)> List()
    {
        var found = new List<(int Width, int Height)>();
        try
        {
            for (var mode = 0; ; mode++)
            {
                var device = new DevMode
                {
                    dmDeviceName = "",
                    dmFormName = "",
                    dmSize = (short)Marshal.SizeOf<DevMode>(),
                };
                if (!EnumDisplaySettings(null, mode, ref device))
                    break;
                found.Add((device.dmPelsWidth, device.dmPelsHeight));
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
        }
        found.Add((1920, 1080));
        found.Add((3440, 1440));
        return found
            .Where(size => Picture.Fits(size.Width, size.Height))
            .Distinct()
            .OrderBy(size => size.Width)
            .ThenBy(size => size.Height)
            .ToList();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
    }
}
