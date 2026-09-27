using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>Issues an opaque receipt that remains usable after member sign-out.</summary>
public sealed class MemberDeletionReceiptService(
    IDataProtectionProvider protectionProvider,
    IMemberAccountRepository accounts)
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("QueenZone.MemberDeletionReceipt.v1");

    public string Issue(Guid memberId) => protector.Protect(memberId.ToString("N"));

    public async Task<MemberDeletionProgress?> GetProgressAsync(
        string? receipt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(receipt) || receipt.Length > 1024)
        {
            return null;
        }

        try
        {
            var value = protector.Unprotect(receipt);
            return Guid.TryParseExact(value, "N", out var memberId)
                ? await accounts.GetDeletionProgressAsync(memberId, cancellationToken)
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
