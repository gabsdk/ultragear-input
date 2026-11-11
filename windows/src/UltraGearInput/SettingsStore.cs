using UltraGearInput.Core;

namespace UltraGearInput;

internal sealed record SettingsLoad(Settings Settings, SettingsError? Error);

internal sealed class SettingsStore(string filePath)
{
    public static SettingsStore Standard { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Text.AppName,
        "settings.json"));

    public string FilePath { get; } = filePath;

    public SettingsLoad Load()
    {
        if (!File.Exists(FilePath))
        {
            return new SettingsLoad(new Settings(), null);
        }
        try
        {
            return new SettingsLoad(Settings.Decode(File.ReadAllText(FilePath)), null);
        }
        catch (SettingsException exception)
        {
            return new SettingsLoad(new Settings(), exception.Error);
        }
        catch (IOException)
        {
            return new SettingsLoad(new Settings(), SettingsError.Malformed);
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, settings.Encode());
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public void EnsureFileExists()
    {
        if (!File.Exists(FilePath))
        {
            Save(new Settings { Inputs = InputSource.Defaults });
        }
    }
}
