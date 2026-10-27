using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DemonsSoulsLauncher;

public sealed class GameInfo
{
    public string Title { get; init; } = "Demon's Souls";
    public string Detail { get; init; } = "";
    public ImageSource? Backdrop { get; init; }
    public ImageSource? Tile { get; init; }

    public static GameInfo Load(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return new GameInfo();

        var sys = Path.Combine(gameFolder, "sce_sys");
        var title = "Demon's Souls";
        var detail = "";
        var paramPath = Path.Combine(sys, "param.json");
        if (File.Exists(paramPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(paramPath));
                var root = document.RootElement;
                var titleId = root.TryGetProperty("titleId", out var id) ? id.GetString() : "";
                var version = root.TryGetProperty("contentVersion", out var ver) ? ver.GetString() : "";
                if (root.TryGetProperty("localizedParameters", out var names))
                {
                    var language = names.TryGetProperty("defaultLanguage", out var def)
                        ? def.GetString()
                        : "en-GB";
                    if (!string.IsNullOrEmpty(language)
                        && names.TryGetProperty(language, out var block)
                        && block.TryGetProperty("titleName", out var name)
                        && !string.IsNullOrWhiteSpace(name.GetString()))
                    {
                        title = name.GetString()!;
                    }
                }
                detail = string.Join("   ·   ", new[] { titleId, version }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return new GameInfo
        {
            Title = title,
            Detail = detail,
            Backdrop = ReadImage(Path.Combine(sys, "pic0.png")),
            Tile = ReadImage(Path.Combine(sys, "icon0.png")),
        };
    }

    private static ImageSource? ReadImage(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
