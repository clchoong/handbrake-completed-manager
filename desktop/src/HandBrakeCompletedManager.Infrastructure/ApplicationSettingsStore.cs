using System.Text.Json;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class ApplicationSettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.GetFullPath(settingsPath);

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return ApplicationSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
            return (settings ?? ApplicationSettings.Default).Normalize();
        }
        catch (JsonException)
        {
            return ApplicationSettings.Default;
        }
    }

    public async Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The application settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings.Normalize(),
                SerializerOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
