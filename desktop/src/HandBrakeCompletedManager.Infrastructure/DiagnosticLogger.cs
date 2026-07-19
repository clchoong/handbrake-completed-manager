using System.Text;

namespace HandBrakeCompletedManager.Infrastructure;

public enum DiagnosticLogLevel
{
    Information,
    Warning,
    Error
}

public sealed class DiagnosticLogger(
    string logDirectory,
    string component,
    Func<DateTimeOffset>? clock = null)
{
    private readonly string _logDirectory = Path.GetFullPath(logDirectory);
    private readonly string _component = Sanitize(component);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string LogDirectory => _logDirectory;

    public async Task<bool> LogAsync(
        DiagnosticLogLevel level,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var timestamp = _clock();
            Directory.CreateDirectory(_logDirectory);
            var logPath = Path.Combine(
                _logDirectory,
                $"handbrake-completed-manager-{timestamp:yyyyMMdd}.log");
            var exceptionText = exception is null
                ? string.Empty
                : $" | {exception.GetType().Name}: {Sanitize(exception.Message)}";
            var line = $"{timestamp:O} [{level}] [{_component}] {Sanitize(message)}{exceptionText}";

            await using var stream = new FileStream(
                logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            return true;
        }
        catch (Exception writeException) when (
            writeException is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
}
