using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Validates pending fan-performance audio, consumes the shared member daily
/// quota, then writes to <see cref="BlobUploadContainers.FanPerformances"/>.
/// Does not persist a submission entity — that is #1293.
/// </summary>
public sealed class FanPerformanceAudioUploadService(
    IBlobUploadService blobUploadService,
    MemberUploadQuotaService uploadQuota,
    IOptions<BlobUploadOptions> options)
{
    public sealed record UploadResult(
        bool Succeeded,
        string? Error,
        BlobUploadResult? Blob,
        int? DurationSeconds);

    public async Task<UploadResult> UploadPendingAsync(
        Guid memberAccountId,
        Stream content,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        if (memberAccountId == Guid.Empty)
        {
            return Fail("Sign in is required to upload a fan performance.");
        }

        if (content is null)
        {
            return Fail("Upload content is empty.");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return Fail("Original file name is required.");
        }

        var validator = new BlobUploadValidator(options.Value);
        var container = BlobUploadContainers.FanPerformances;
        validator.EnsureKnownContainer(container);

        Stream buffer;
        try
        {
            buffer = await AzureBlobUploadService.BufferForUploadAsync(
                content,
                validator.GetMaxBytes(container),
                cancellationToken);
        }
        catch (BlobUploadException ex)
        {
            return Fail(ex.Message);
        }

        await using (buffer)
        {
            try
            {
                validator.ValidateSize(buffer.Length, container);

                buffer.Position = 0;
                var headerLength = (int)Math.Min(64, buffer.Length);
                var header = new byte[headerLength];
                var headerRead = await buffer.ReadAsync(header.AsMemory(0, headerLength), cancellationToken);
                var contentType = validator.ResolveAndValidateContentType(
                    originalFileName,
                    header.AsSpan(0, headerRead),
                    container);

                buffer.Position = 0;
                var prefixLength = (int)Math.Min(Mp3Duration.PrefixBytes, buffer.Length);
                var prefix = new byte[prefixLength];
                var prefixRead = await buffer.ReadAsync(prefix.AsMemory(0, prefixLength), cancellationToken);
                var duration = AudioDuration.TryGetSeconds(
                    contentType,
                    prefix.AsSpan(0, prefixRead),
                    buffer.Length);

                var principalKey = MemberUploadQuotaService.PrincipalKeyFromMemberId(memberAccountId);
                if (!uploadQuota.TryConsume(principalKey, buffer.Length, out var quotaError))
                {
                    return Fail(quotaError ?? "Daily upload limit reached.");
                }

                buffer.Position = 0;
                var uploaded = await blobUploadService.UploadAsync(
                    buffer,
                    originalFileName,
                    container,
                    new BlobUploadContext { MemberAccountId = memberAccountId },
                    cancellationToken);

                return new UploadResult(true, null, uploaded, duration);
            }
            catch (BlobUploadException ex)
            {
                return Fail(ex.Message);
            }
        }
    }

    private static UploadResult Fail(string error) => new(false, error, null, null);
}
