namespace DemonsSoulsLauncher;

public static class Picture
{
    public const int UiWidth = 3840;
    public const int OutputHeight = 2160;
    public const int MinWidth = 1280;
    public const int MinHeight = 720;

    public static bool IsSixteenByNine(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return false;
        return Math.Abs((double)width / height - 16.0 / 9.0) <= 0.02;
    }

    public static bool Fits(int width, int height)
    {
        if (width < MinWidth || height < MinHeight)
            return false;
        return (double)width / height >= 16.0 / 9.0 - 0.02;
    }

    public static bool IsWide(int width, int height) =>
        Fits(width, height) && !IsSixteenByNine(width, height);

    public static int OutputWidth(int width, int height) =>
        (int)Math.Round(OutputHeight * (double)width / height, MidpointRounding.AwayFromZero);

    public static (int Width, int Height) Resolve(LauncherSettings settings)
    {
        if (Fits(settings.Width, settings.Height))
            return (settings.Width, settings.Height);
        return settings.Ultrawide ? (3440, 1440) : (1920, 1080);
    }
}
