using System.Text.Json;
using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed class HandBrakeConnectionStore(string settingsPath)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath = Path.GetFullPath(settingsPath);

    public async Task<IReadOnlyList<HandBrakeConnectionState>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<List<HandBrakeConnectionState>>(
                stream,
                SerializerOptions,
                cancellationToken) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveConnectedAsync(
        string executablePath,
        DateTimeOffset testedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var connections = (await LoadAsync(cancellationToken)).ToList();
        connections.RemoveAll(connection =>
            connection.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        connections.Add(new HandBrakeConnectionState(
            Path.GetFullPath(executablePath),
            IsConnected: true,
            testedAtUtc.ToUniversalTime()));

        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The connection settings path has no parent directory.");
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
            await JsonSerializer.SerializeAsync(stream, connections, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
