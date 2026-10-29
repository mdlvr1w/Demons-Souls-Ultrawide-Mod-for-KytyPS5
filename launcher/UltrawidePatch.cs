using System.IO;
using System.Text;

namespace DemonsSoulsLauncher;

public static class UltrawidePatch
{
    public sealed record Entry(string Offset, string Off, string On);

    public static IReadOnlyList<Entry> Entries(int width, int height)
    {
        if (!Picture.IsWide(width, height))
            throw new ArgumentOutOfRangeException(nameof(width));
        var output = (uint)Picture.OutputWidth(width, height);
        var sceneWidth = (uint)width;
        var sceneHeight = (uint)height;
        var aspect = "c74304" + U32(unchecked((uint)BitConverter.SingleToInt32Bits((float)width / height)));
        var rdx = "48ba" + U32(output) + U32((uint)Picture.OutputHeight);
        var rcx = "48b9" + U32(output) + U32((uint)Picture.OutputHeight);
        return new Entry[]
        {
            new("90ae7f", "c74304398ee33f", aspect),
            new("90ac3f", "48ba000f000070080000", rdx),
            new("90a6ab", "48b9000f000070080000", rcx),
            new("90b217", "48b9000f000070080000", rcx),
            new("250eb48", "4006000080070000000a0000000f0000800a0000",
                U32(1600) + U32(1920) + U32(sceneWidth) + U32(3840) + U32(2688)),
            new("237f264", "000a0000a0050000000f000070080000000f000070080000",
                U32(sceneWidth) + U32(sceneHeight) + U32(output) + U32((uint)Picture.OutputHeight)
                + U32(output) + U32((uint)Picture.OutputHeight)),
            new("24f9550", "000a0000a00500000000000000000000",
                U32(sceneWidth) + U32(sceneHeight) + U32(0) + U32(0)),
            new("90af74", "bf000f0000", "bf" + U32(output)),
            new("90b061", "bf000f0000", "bf" + U32(output)),
            new("916355", "8bb5ecfeffff", "be" + U32(output) + "90"),
        };
    }

    public static void Write(string path, int width, int height)
    {
        var entries = Entries(width, height);
        var output = Picture.OutputWidth(width, height);
        var json = new StringBuilder();
        json.Append("{\n");
        json.Append(" \"id\": \"PPSA01341\",\n");
        json.Append(" \"version\": \"01.007.000\",\n");
        json.Append(" \"process\": \"eboot.bin\",\n");
        json.Append(" \"guide\": [\n");
        json.Append($"  \"{width}x{height} gameplay. Output {output}x{Picture.OutputHeight}. The composition DLL centers the 3840x2160 interface.\"\n");
        json.Append(" ],\n");
        json.Append(" \"mods\": [\n");
        json.Append("  {\n");
        json.Append($"   \"name\": \"{width}x{height} gameplay, native UI\",\n");
        json.Append("   \"enabled\": true,\n");
        json.Append("   \"memory\": [\n");
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var comma = i == entries.Count - 1 ? "" : ",";
            json.Append("    {\n");
            json.Append($"     \"offset\": \"{entry.Offset}\",\n");
            json.Append($"     \"off\": \"{entry.Off}\",\n");
            json.Append($"     \"on\": \"{entry.On}\"\n");
            json.Append($"    }}{comma}\n");
        }
        json.Append("   ]\n");
        json.Append("  }\n");
        json.Append(" ]\n");
        json.Append("}\n");
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    static string U32(uint value) => Convert.ToHexString(BitConverter.GetBytes(value)).ToLowerInvariant();
}
