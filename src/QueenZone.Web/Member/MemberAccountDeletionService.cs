using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public sealed class MemberAccountDeletionService(
    IMemberAccountRepository memberAccountRepository,
    IBlobUploadService blobUploadService,
    TimeProvider? timeProvider = null,
    AppleAccountTokenService? appleTokens = null,
    ILogger<MemberAccountService>? logger = null,
    IEmailSender? emailSender = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<MemberAccountResult> DeleteImmediatelyAsync(
        Guid memberId,
        TimeSpan blobDeleteTimeout,
        CancellationToken cancellationToken = default)
    {
        var requested = await memberAccountRepository.RequestDeletionAsync(
            memberId,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken,
            immediate: true);
        if (requested is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        var recipientEmail = requested.Account.Email;

        try
        {
            await PurgeDueDeletionsAsync(clock.GetUtcNow().UtcDateTime, blobDeleteTimeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Immediate account deletion purge failed for {MemberId}.", memberId);
        }

        var purged = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (purged?.PersonalDataPurgedAt is null)
        {
            return MemberAccountResult.Failure(MemberAccountService.ImmediatePurgeIncompleteError);
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
                logger?.LogError("Could not email account deletion confirmation for {MemberId}: {ErrorType}.", memberId, ex.GetType().Name);
            }
        }

        return MemberAccountResult.Success(purged);
    }

    public async Task<int> PurgeDueDeletionsAsync(
        DateTime utcNow,
        TimeSpan blobDeleteTimeout,
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
            timeout.CancelAfter(blobDeleteTimeout);
            try
            {
                await blobUploadService.DeleteAsync(blob.Container, blob.Path, timeout.Token)
                    .WaitAsync(blobDeleteTimeout, cancellationToken);
                await memberAccountRepository.CompleteDeletionBlobAsync(blob.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "Account deletion blob cleanup will retry for {BlobId}.", blob.Id);
            }
        }

        if (appleTokens is not null)
        {
            await appleTokens.RevokePendingAsync(cancellationToken);
        }

        return result.PurgedCount;
    }

}
