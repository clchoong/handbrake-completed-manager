using HandBrakeCompletedManager.Core;

namespace HandBrakeCompletedManager.Core.Tests;

public sealed class ReplacementRecoveryAdvisorTests
{
    [Fact]
    public void Review_BlocksIncompleteOperationWithoutTemporaryFile()
    {
        var review = ReplacementRecoveryAdvisor.Review(
            CreateOperation(ReplacementOperationStatus.InProgress, ReplacementVerificationStatus.NotVerified),
            temporaryFileExists: false);

        Assert.True(review.ShouldDisplay);
        Assert.True(review.BlocksNewCopy);
        Assert.Contains("incomplete", review.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_BlocksAnyExistingTemporaryFile()
    {
        var review = ReplacementRecoveryAdvisor.Review(
            CreateOperation(ReplacementOperationStatus.Cancelled, ReplacementVerificationStatus.NotVerified),
            temporaryFileExists: true);

        Assert.True(review.ShouldDisplay);
        Assert.True(review.BlocksNewCopy);
        Assert.Contains("partial file", review.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_AllowsRetryAfterFailedAttemptWithoutPartialFile()
    {
        var review = ReplacementRecoveryAdvisor.Review(
            CreateOperation(ReplacementOperationStatus.Failed, ReplacementVerificationStatus.Failed),
            temporaryFileExists: false);

        Assert.True(review.ShouldDisplay);
        Assert.False(review.BlocksNewCopy);
        Assert.Contains("No partial file remains", review.Message);
    }

    [Fact]
    public void Review_HidesCompletedOperationWithoutTemporaryFile()
    {
        var review = ReplacementRecoveryAdvisor.Review(
            CreateOperation(ReplacementOperationStatus.Completed, ReplacementVerificationStatus.Verified),
            temporaryFileExists: false);

        Assert.False(review.ShouldDisplay);
        Assert.False(review.BlocksNewCopy);
        Assert.Empty(review.Message);
    }

    private static ReplacementOperation CreateOperation(
        ReplacementOperationStatus status,
        ReplacementVerificationStatus verificationStatus)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReplacementOperation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
            status switch
            {
                ReplacementOperationStatus.Completed => ReplacementOperationStage.Completed,
                ReplacementOperationStatus.Cancelled => ReplacementOperationStage.Cancelled,
                ReplacementOperationStatus.Failed => ReplacementOperationStage.Failed,
                _ => ReplacementOperationStage.Copying
            },
            @"C:\Videos\Source.mkv",
            @"D:\Converted\Output.mp4",
            @"C:\Videos\Source.mp4",
            @"C:\Videos\Source.mp4.hbcm-copying",
            @"C:\Videos\HandBrake Original Backup\Source.mkv",
            2_048,
            1_024,
            512,
            verificationStatus,
            status == ReplacementOperationStatus.Failed ? "Disk unavailable." : null,
            now,
            now);
    }
}
