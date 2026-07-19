namespace HandBrakeCompletedManager.Core;

public sealed record ReplacementRecoveryReview(
    bool ShouldDisplay,
    bool BlocksNewCopy,
    string Message);

public static class ReplacementRecoveryAdvisor
{
    public static ReplacementRecoveryReview Review(
        ReplacementOperation operation,
        bool temporaryFileExists)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var blocksNewCopy = temporaryFileExists ||
            operation.Status is ReplacementOperationStatus.Planned or ReplacementOperationStatus.InProgress;
        var shouldDisplay = blocksNewCopy ||
            operation.Status is ReplacementOperationStatus.Failed or ReplacementOperationStatus.Cancelled;
        if (!shouldDisplay)
        {
            return new ReplacementRecoveryReview(false, false, string.Empty);
        }

        var copied = $"{FormatBytes(operation.BytesCopied)} of {FormatBytes(operation.DestinationSize)}";
        var message = operation.VerificationStatus == ReplacementVerificationStatus.Verified
            ? $"A verified temporary copy already exists ({copied}). Final replacement remains disabled."
            : operation.Status switch
            {
                ReplacementOperationStatus.Cancelled =>
                    $"A previous copy was cancelled after {copied}." +
                    (temporaryFileExists ? " Its partial file requires recovery review." : " No partial file remains."),
                ReplacementOperationStatus.Failed =>
                    $"A previous copy failed after {copied}." +
                    (temporaryFileExists ? " Its partial file requires recovery review." : " No partial file remains.") +
                    (string.IsNullOrWhiteSpace(operation.FailureMessage) ? string.Empty : $" {operation.FailureMessage}"),
                _ =>
                    $"An incomplete copy operation was found at stage {operation.Stage} ({copied}). It requires recovery review."
            };
        return new ReplacementRecoveryReview(true, blocksNewCopy, message);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
