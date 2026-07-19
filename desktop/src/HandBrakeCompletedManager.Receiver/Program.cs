using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

var logger = new DiagnosticLogger(StoragePaths.ResolveLogsDirectory(), "Receiver");
var parseResult = CompletionEventParser.Parse(
    args,
    Environment.GetEnvironmentVariable);

if (!parseResult.IsSuccess)
{
    await logger.LogAsync(DiagnosticLogLevel.Warning, "A completion event was rejected because required input was missing or invalid.");
    Console.Error.WriteLine(parseResult.Error);
    return 2;
}

try
{
    var databasePath = StoragePaths.ResolveDatabasePath();
    var repository = new CompletedEncodeRepository(databasePath);
    await repository.InitializeAsync();

    var completedEncode = CompletedEncodeCapture.Create(parseResult.Event!);
    var inserted = await repository.AddAsync(completedEncode);

    Console.WriteLine(inserted
        ? $"Recorded completed encode: {completedEncode.DestinationPath}"
        : $"Completion event already recorded: {completedEncode.DestinationPath}");
    await logger.LogAsync(
        DiagnosticLogLevel.Information,
        inserted ? "A completed encode was recorded." : "A duplicate completion event was ignored.");
    return 0;
}
catch (Exception exception)
{
    await logger.LogAsync(DiagnosticLogLevel.Error, "A completed encode could not be recorded.", exception);
    Console.Error.WriteLine($"Unable to record the completed encode: {exception.Message}");
    return 1;
}
