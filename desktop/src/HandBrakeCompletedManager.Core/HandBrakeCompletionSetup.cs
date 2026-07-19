namespace HandBrakeCompletedManager.Core;

public sealed record HandBrakeCompletionSetup(
    string ReceiverPath,
    string Arguments,
    bool ReceiverExists)
{
    public const string RecommendedArguments =
        "--source {source} --destination {destination} " +
        "--destination-folder {destination_folder} --exit-code {exit_code}";

    public static HandBrakeCompletionSetup Create(string receiverPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiverPath);

        var fullPath = Path.GetFullPath(receiverPath);
        return new HandBrakeCompletionSetup(
            fullPath,
            RecommendedArguments,
            File.Exists(fullPath));
    }
}
