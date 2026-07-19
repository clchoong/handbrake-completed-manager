using HandBrakeCompletedManager.Core;
using HandBrakeCompletedManager.Infrastructure;

var parseResult = CompletionEventParser.Parse(
    args,
    Environment.GetEnvironmentVariable);

if (!parseResult.IsSuccess)
{
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
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Unable to record the completed encode: {exception.Message}");
    return 1;
}
