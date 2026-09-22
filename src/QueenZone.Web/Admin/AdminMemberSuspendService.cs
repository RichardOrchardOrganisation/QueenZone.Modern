using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Admin member suspend: hide forum content, then suspend the account, then revoke
/// refresh tokens. SQL-backed hosts wrap hide + suspend in one DbContext transaction
/// (the three repos share the scoped context). In-memory Testing has no DbContext, so
/// the same hide-first sequence runs without a transaction.
/// </summary>
public sealed class AdminMemberSuspendService(
    IMemberAccountRepository memberAccountRepository,
    IForumWriteRepository forumWriteRepository,
    IMobileAuthGrantRepository mobileAuthGrantRepository,
    ILogger<AdminMemberSuspendService> logger,
    IServiceProvider? serviceProvider = null)
{
    public const string SuccessMessage =
        "Member suspended and their forum topics and posts hidden. Their session will end on their next request.";

    public const string HideTimeoutMessage =
        "Could not hide forum content for this member because the database timed out. The account is still active. Retry suspend.";

    public const string RevokeFailedMessage =
        "Member is suspended and their forum content is hidden, but their sessions could not be revoked. Retry suspend to revoke sessions.";

    public async Task<AdminMemberSuspendResult> SuspendAsync(
        Guid memberId,
        string reason,
        string suspendedByAdminEmail,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var member = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (member is null)
        {
            return AdminMemberSuspendResult.NotFound();
        }

        try
        {
            var hiddenAndSuspended = await QueenZoneDbTransactions.ExecuteAsync(
                serviceProvider,
                async ct =>
                {
                    await forumWriteRepository.HideAuthorForumContentAsync(
                        member.Id, member.DisplayName, ct);

                    var updated = await memberAccountRepository.SuspendAsync(
                        member.Id, reason, suspendedByAdminEmail, utcNow, ct);
                    return updated is not null;
                },
                cancellationToken);

            if (!hiddenAndSuspended)
            {
                return AdminMemberSuspendResult.NotFound();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && SiteSearchSqlTimeout.IsCommandTimeout(ex))
        {
            logger.LogWarning(
                ex,
                "Admin member suspend hide timed out for {MemberId}. Account left active.",
                memberId);
            return AdminMemberSuspendResult.HideTimedOut();
        }

        try
        {
            await mobileAuthGrantRepository.RevokeAllRefreshTokensForMemberAsync(
                memberId, utcNow, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Admin member suspend revoke failed for {MemberId} after hide and suspend.",
                memberId);
            return AdminMemberSuspendResult.RevokeFailed();
        }

        return AdminMemberSuspendResult.Succeeded();
    }
}

public enum AdminMemberSuspendStatus
{
    NotFound = 0,
    Succeeded = 1,
    HideTimedOut = 2,
    RevokeFailed = 3,
}

public readonly record struct AdminMemberSuspendResult(AdminMemberSuspendStatus Status)
{
    public static AdminMemberSuspendResult NotFound() => new(AdminMemberSuspendStatus.NotFound);

    public static AdminMemberSuspendResult Succeeded() => new(AdminMemberSuspendStatus.Succeeded);

    public static AdminMemberSuspendResult HideTimedOut() => new(AdminMemberSuspendStatus.HideTimedOut);

    public static AdminMemberSuspendResult RevokeFailed() => new(AdminMemberSuspendStatus.RevokeFailed);
}
