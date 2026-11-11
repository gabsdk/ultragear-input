namespace UltraGearInput.Core;

public sealed record SettingsLoad(Settings Settings, SettingsError? Error);

public sealed class SettingsStore(string filePath)
{
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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

    public void SaveProtocol(DdcProtocol ddcProtocol, DisplayDescriptor display)
    {
        var loaded = Load();
        if (loaded.Error is { } error)
        {
            throw new SettingsException(error);
        }
        Save(loaded.Settings.WithProtocol(ddcProtocol, display));
    }

    public void EnsureFileExists()
    {
        if (!File.Exists(FilePath))
        {
            Save(new Settings { Inputs = InputSource.Defaults });
        }
    }
}
