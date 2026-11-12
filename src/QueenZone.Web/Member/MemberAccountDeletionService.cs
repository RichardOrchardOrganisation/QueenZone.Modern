using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public sealed class MemberAccountDeletionService(
    IMemberAccountRepository memberAccountRepository,
    IBlobUploadService blobUploadService,
    TimeProvider timeProvider,
    AppleAccountTokenService? appleTokens,
    ILogger<MemberAccountDeletionService> logger,
    IEmailSender? emailSender)
{
    private readonly TimeProvider clock = timeProvider;

    internal static readonly TimeSpan DefaultBlobDeleteTimeout = TimeSpan.FromSeconds(20);

    internal TimeSpan BlobDeleteTimeout { get; init; } = DefaultBlobDeleteTimeout;

    public const string ImmediatePurgeIncompleteError =
        "Account deletion could not finish removing personal data. Try again shortly.";

    public async Task<MemberAccountResult> RequestDeletionAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var requested = await memberAccountRepository.RequestDeletionAsync(
            memberId,
            DateTime.UtcNow,
            cancellationToken);
        if (requested is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(requested.Account);
    }

    public async Task<MemberAccountResult> CancelDeletionAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.CancelDeletionAsync(
            memberId,
            DateTime.UtcNow,
            cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.PersonalDataPurgedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.DeletionNotCancellable, "Account deletion can no longer be cancelled.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.DeletionNotCancellable, "The 30-day cooling-off period has ended.");
        }

        return MemberAccountResult.Success(account);
    }

    public async Task<MemberAccountResult> DeleteImmediatelyAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var requested = await memberAccountRepository.RequestDeletionAsync(
            memberId,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken,
            immediate: true);
        if (requested is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        var recipientEmail = requested.Account.Email;

        try
        {
            await PurgeDueDeletionsAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Immediate account deletion purge failed for {MemberId}.", memberId);
        }

        var purged = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (purged?.PersonalDataPurgedAt is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.PurgeIncomplete, ImmediatePurgeIncompleteError);
        }

        if (emailSender is not null)
        {
            try
            {
                await emailSender.SendAsync(
                    new OutboundEmail(
                        recipientEmail,
                        "Your Queenzone account deletion request",
                        "Your Queenzone account has been deleted. Remaining external cleanup may still be processing. If you did not request this, contact support@queenzone.org."),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Could not email account deletion confirmation for {MemberId}: {ErrorType}.", memberId, ex.GetType().Name);
            }
        }

        return MemberAccountResult.Success(purged);
    }

    public async Task<int> PurgeDueDeletionsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var purgeBefore = utcNow.AddDays(-MemberAccountDeletionPolicy.RetentionDays);
        var result = await memberAccountRepository.PurgeDeletedAccountsAsync(
            purgeBefore,
            utcNow,
            cancellationToken);
        var pendingBlobs = await memberAccountRepository.ListPendingDeletionBlobsAsync(100, cancellationToken);
        foreach (var blob in pendingBlobs)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BlobDeleteTimeout);
            try
            {
                await blobUploadService.DeleteAsync(blob.Container, blob.Path, timeout.Token)
                    .WaitAsync(BlobDeleteTimeout, cancellationToken);
                await memberAccountRepository.CompleteDeletionBlobAsync(blob.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Account deletion blob cleanup will retry for {BlobId}.", blob.Id);
            }
        }

        if (appleTokens is not null)
        {
            await appleTokens.RevokePendingAsync(cancellationToken);
        }

        return result.PurgedCount;
    }

}
