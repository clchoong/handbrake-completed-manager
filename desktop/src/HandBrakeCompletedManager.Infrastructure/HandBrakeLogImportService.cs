using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Infrastructure;

public sealed record HandBrakeLogImportItem(
    string LogPath,
    CompletionEvent? CompletionEvent,
    CompletedEncode? Record,
    string Status,
    bool CanImport);

public sealed record HandBrakeLogImportReview(
    string LogDirectory,
    IReadOnlyList<HandBrakeLogImportItem> Items)
{
    public int RecoverableCount => Items.Count(item => item.CanImport);
}

public sealed record HandBrakeLogImportResult(
    int Imported,
    int Duplicates,
    int Skipped,
    IReadOnlyList<string> Failures);

public sealed class HandBrakeLogImportService(CompletedEncodeRepository repository)
{
    private readonly CompletedEncodeRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public static string ResolveDefaultLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HandBrake",
        "logs");

    public async Task<HandBrakeLogImportReview> ReviewAsync(
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        var fullDirectory = Path.GetFullPath(logDirectory);
        if (!Directory.Exists(fullDirectory))
        {
            return new HandBrakeLogImportReview(fullDirectory, []);
        }

        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var existingFingerprints = (await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .Select(record => record.EventFingerprint)
            .ToHashSet(StringComparer.Ordinal);
        var reviewedFingerprints = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<HandBrakeLogImportItem>();

        foreach (var logPath in Directory.EnumerateFiles(fullDirectory, "*.txt", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(await ReviewFileAsync(
                logPath,
                existingFingerprints,
                reviewedFingerprints,
                cancellationToken).ConfigureAwait(false));
        }

        return new HandBrakeLogImportReview(fullDirectory, items);
    }

    public async Task<HandBrakeLogImportResult> ImportAsync(
        IEnumerable<HandBrakeLogImportItem> items,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var candidates = items.Where(item => item.CanImport && item.CompletionEvent is not null).ToArray();
        var imported = 0;
        var duplicates = 0;
        var skipped = 0;
        var failures = new List<string>();

        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            progress?.Report($"Importing {index + 1:N0} of {candidates.Length:N0}: {Path.GetFileName(candidate.LogPath)}");
            try
            {
                var refreshedRecord = CompletedEncodeCapture.Create(candidate.CompletionEvent!);
                if (!refreshedRecord.DestinationExists)
                {
                    skipped++;
                    continue;
                }

                if (await _repository.AddAsync(refreshedRecord, cancellationToken).ConfigureAwait(false))
                {
                    imported++;
                }
                else
                {
                    duplicates++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"{Path.GetFileName(candidate.LogPath)}: {exception.Message}");
            }
        }

        return new HandBrakeLogImportResult(imported, duplicates, skipped, failures);
    }

    private static async Task<HandBrakeLogImportItem> ReviewFileAsync(
        string logPath,
        IReadOnlySet<string> existingFingerprints,
        ISet<string> reviewedFingerprints,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false);
            var fallbackTime = new DateTimeOffset(File.GetLastWriteTimeUtc(logPath), TimeSpan.Zero);
            var parsed = HandBrakeActivityLogParser.Parse(content, fallbackTime);
            if (!parsed.IsRecoverable)
            {
                return new HandBrakeLogImportItem(logPath, null, null, parsed.Message, false);
            }

            var record = CompletedEncodeCapture.Create(parsed.CompletionEvent!);
            if (!record.DestinationExists)
            {
                return new HandBrakeLogImportItem(
                    logPath,
                    parsed.CompletionEvent,
                    record,
                    "Skipped — output file no longer exists",
                    false);
            }

            if (existingFingerprints.Contains(record.EventFingerprint) || !reviewedFingerprints.Add(record.EventFingerprint))
            {
                return new HandBrakeLogImportItem(
                    logPath,
                    parsed.CompletionEvent,
                    record,
                    "Already in completed history",
                    false);
            }

            return new HandBrakeLogImportItem(
                logPath,
                parsed.CompletionEvent,
                record,
                "Ready to import",
                true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HandBrakeLogImportItem(
                logPath,
                null,
                null,
                $"Unreadable log — {exception.Message}",
                false);
        }
    }
}
