using System.IO;
using System.IO.Compression;

namespace DemonsSoulsLauncher;

public static class SaveArchive
{
    public static string? Export(string kytyFolder, string destinationFolder)
    {
        if (GameHost.EmulatorIsRunning())
            return "Close the game before exporting the save.";

        var saveRoot = Path.Combine(kytyFolder, "_SaveData");
        var gameSave = Path.Combine(saveRoot, GameHost.TitleId);
        var capacity = Path.Combine(saveRoot, ".kyty-capacity", GameHost.TitleId);
        if (!Directory.Exists(gameSave))
            return "No save was found in the Kyty folder.";

        Directory.CreateDirectory(destinationFolder);
        var zipPath = Path.Combine(destinationFolder, $"DemonsSouls-Save-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        if (File.Exists(zipPath))
            zipPath = Path.Combine(destinationFolder, $"DemonsSouls-Save-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddTree(zip, gameSave, GameHost.TitleId);
            if (Directory.Exists(capacity))
                AddTree(zip, capacity, Path.Combine(".kyty-capacity", GameHost.TitleId));
        }
        return null;
    }

    public static string? Restore(string kytyFolder, string zipPath)
    {
        if (GameHost.EmulatorIsRunning())
            return "Close the game before bringing a save back.";
        if (!File.Exists(zipPath))
            return "That save file was not found.";

        var saveRoot = Path.Combine(kytyFolder, "_SaveData");
        var rootFull = Path.GetFullPath(saveRoot) + Path.DirectorySeparatorChar;
        try
        {
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var usable = zip.Entries.Any(entry =>
            {
                var name = entry.FullName.Replace('\\', '/');
                return name.Contains($"{GameHost.TitleId}/", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("USR-DATA", StringComparison.OrdinalIgnoreCase);
            });
            if (!usable)
                return "That file is not a Demon's Souls save from this launcher.";
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.GetFullPath(Path.Combine(saveRoot, relative));
                if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return "That save file contains a path that was not restored.";
            }
        }

        Directory.CreateDirectory(saveRoot);
        var backup = Path.Combine(saveRoot, "_backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        CopyIfPresent(Path.Combine(saveRoot, GameHost.TitleId), Path.Combine(backup, GameHost.TitleId));
        CopyIfPresent(
            Path.Combine(saveRoot, ".kyty-capacity", GameHost.TitleId),
            Path.Combine(backup, ".kyty-capacity", GameHost.TitleId));

        DeleteIfPresent(Path.Combine(saveRoot, GameHost.TitleId));
        DeleteIfPresent(Path.Combine(saveRoot, ".kyty-capacity", GameHost.TitleId));

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.GetFullPath(Path.Combine(saveRoot, relative));
                if (!destination.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                    return "The save file contains a path that was not restored.";
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }
        return null;
        }
        catch (InvalidDataException)
        {
            return "That file is not a Demon's Souls save from this launcher.";
        }
        catch (IOException)
        {
            return "The save could not be read or written.";
        }
    }

    private static void AddTree(ZipArchive zip, string source, string entryRoot)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var name = Path.Combine(entryRoot, relative).Replace('\\', '/');
            zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
        }
    }

    private static void CopyIfPresent(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
