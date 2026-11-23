using System.Text.Json;
using Convert.Core;

namespace Convert;

/// <summary>Remembered choices, in %LOCALAPPDATA%\Convert\settings.json.</summary>
public sealed class SettingsStore
{
    public Dictionary<string, string> LastTarget { get; set; } = [];
    public Dictionary<string, ConvSettings> Settings { get; set; } = [];
    public bool TrashOriginals { get; set; }
    public bool ShowAdvanced { get; set; }
    /// <summary>Where converted files go: "beside" the original, the chosen "folder", or "ask" each time.</summary>
    public string SaveTo { get; set; } = "beside";
    public string? SaveFolder { get; set; }
    /// <summary>The folder picked the last time Convert asked, offered first the next time.</summary>
    public string? LastAskedFolder { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Convert", "settings.json");

    // The same names as the Mac's saved settings; reading ignores case, so files from the first
    // Windows version still load.
    private static readonly JsonSerializerOptions Json = ConvSettings.Json;

    public static SettingsStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(FilePath), Json) ?? new SettingsStore();
        }
        catch (Exception) { }
        return new SettingsStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception) { }
    }
}
