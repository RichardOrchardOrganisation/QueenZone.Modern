using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Member avatar upload and removal (#2437). Takes a plain stream and file name so callers
/// outside ASP.NET Core (for example the mobile API host) can reuse it.
/// </summary>
public sealed class MemberAvatarService(
    IMemberAccountRepository memberAccountRepository,
    IBlobUploadService blobUploadService,
    MemberUploadQuotaService uploadQuota)
{
    /// <summary>
    /// Bound for each avatar blob delete. Azure.Storage.Blobs often ignores
    /// <see cref="CancellationToken"/> during a dead TCP or retry loop, so deletes use
    /// both <c>CancelAfter</c> and <c>WaitAsync</c>.
    /// </summary>
    private const string AccountNotFoundError = "Account not found.";

    internal static readonly TimeSpan DefaultBlobDeleteTimeout = TimeSpan.FromSeconds(20);

    internal TimeSpan BlobDeleteTimeout { get; init; } = DefaultBlobDeleteTimeout;

    /// <summary>
    /// Validates and processes an avatar upload, stores full + thumbnail WebP blobs, and
    /// updates <see cref="Data.Entities.MemberAccount.AvatarUrl"/>. On DB failure the new blobs
    /// are deleted and the previous avatar is left in place.
    /// </summary>
    public async Task<MemberAccountResult> UpdateAvatarAsync(
        Guid memberId,
        Stream image,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, AccountNotFoundError);
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(
                MemberAccountOutcome.PendingDeletion,
                MemberAccountService.PendingDeletionEditError);
        }

        MemberAvatarImageProcessor.ProcessedAvatar processed;
        try
        {
            processed = await MemberAvatarImageProcessor.ProcessAsync(image, originalFileName, cancellationToken);
        }
        catch (MemberAvatarRejectedException ex)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, ex.Message);
        }

        var principalKey = MemberUploadQuotaService.PrincipalKeyFromMemberId(memberId);
        var quotaBytes = Math.Max(processed.FullImage.Length, 1);
        if (!uploadQuota.TryConsume(principalKey, quotaBytes, out var quotaError))
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.QuotaExceeded, quotaError ?? "Daily upload limit reached.");
        }

        await using (processed.FullImage)
        await using (processed.Thumbnail)
        {
            var avatarBlobName = MemberAvatarPaths.CreateAvatarBlobName(memberId);
            var thumbBlobName = MemberAvatarPaths.ToThumbBlobName(avatarBlobName);
            var uploadContext = new BlobUploadContext
            {
                MemberAccountId = memberId,
                PreferredBlobName = avatarBlobName,
            };
            var thumbContext = new BlobUploadContext
            {
                MemberAccountId = memberId,
                PreferredBlobName = thumbBlobName,
            };

            string? uploadedAvatar = null;
            string? uploadedThumb = null;
            try
            {
                processed.FullImage.Position = 0;
                var fullResult = await blobUploadService.UploadAsync(
                    processed.FullImage,
                    "avatar.webp",
                    MemberAvatarPaths.Container,
                    uploadContext,
                    cancellationToken);
                uploadedAvatar = fullResult.BlobName;

                processed.Thumbnail.Position = 0;
                var thumbResult = await blobUploadService.UploadAsync(
                    processed.Thumbnail,
                    "avatar-thumb.webp",
                    MemberAvatarPaths.Container,
                    thumbContext,
                    cancellationToken);
                uploadedThumb = thumbResult.BlobName;

                var previousAvatar = account.AvatarUrl;
                var updated = await memberAccountRepository.UpdateAvatarUrlAsync(
                    memberId,
                    uploadedAvatar,
                    cancellationToken);
                if (updated is null)
                {
                    await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                    return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, AccountNotFoundError);
                }

                // Delete previous blobs only after the DB write succeeds.
                if (!string.IsNullOrWhiteSpace(previousAvatar)
                    && !string.Equals(previousAvatar, uploadedAvatar, StringComparison.Ordinal))
                {
                    await SafeDeleteAsync(
                        previousAvatar,
                        MemberAvatarPaths.ToThumbBlobName(previousAvatar),
                        cancellationToken);
                }

                return MemberAccountResult.Success(updated);
            }
            catch (NotSupportedException ex)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                return MemberAccountResult.Failure(MemberAccountOutcome.UploadFailed, ex.Message);
            }
            catch (BlobUploadException ex)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                return MemberAccountResult.Failure(MemberAccountOutcome.UploadFailed, ex.Message);
            }
            catch (Exception)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                throw;
            }
        }
    }

    public async Task<MemberAccountResult> RemoveAvatarAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, AccountNotFoundError);
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(
                MemberAccountOutcome.PendingDeletion,
                MemberAccountService.PendingDeletionEditError);
        }

        if (string.IsNullOrWhiteSpace(account.AvatarUrl))
        {
            return MemberAccountResult.Success(account);
        }

        var previousAvatar = account.AvatarUrl;
        var updated = await memberAccountRepository.UpdateAvatarUrlAsync(memberId, null, cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, AccountNotFoundError);
        }

        await SafeDeleteAsync(
            previousAvatar,
            MemberAvatarPaths.ToThumbBlobName(previousAvatar),
            cancellationToken);

        return MemberAccountResult.Success(updated);
    }

    private async Task SafeDeleteAsync(
        string? avatarBlobName,
        string? thumbBlobName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(avatarBlobName))
        {
            await DeleteBlobBestEffortAsync(avatarBlobName, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(thumbBlobName))
        {
            await DeleteBlobBestEffortAsync(thumbBlobName, cancellationToken);
        }
    }

    private async Task DeleteBlobBestEffortAsync(string blobName, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BlobDeleteTimeout);

        try
        {
            // CancelAfter asks the SDK to stop; WaitAsync bounds this await even when
            // the SDK ignores the token (cold first outbound call, dead TCP, retries).
            await blobUploadService
                .DeleteAsync(MemberAvatarPaths.Container, blobName, timeout.Token)
                .WaitAsync(BlobDeleteTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Timeout, SDK cancel, or storage failure: best-effort only.
        }
    }
}
