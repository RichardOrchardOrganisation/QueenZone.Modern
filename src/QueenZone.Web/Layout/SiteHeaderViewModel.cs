using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed record SiteHeaderViewModel(
    string Path,
    bool MastheadDark,
    string? MemberDisplayName,
    Guid? MemberId,
    bool MemberHasAvatar,
    int UnreadMessageCount,
    bool ShowAdminNav,
    string? AdminEmail)
{
    public IReadOnlyList<SiteHeaderNavGroup> NavigationGroups => SiteHeaderNavigation.Groups;

    public string MessagesIconLabel => UnreadMessageCount > 0
        ? $"Messages, {UnreadMessageCount} unread conversations"
        : "Messages";

    public bool IsActive(string route) => route == "/"
        ? Path == "/"
        : Path.StartsWith(route, StringComparison.OrdinalIgnoreCase);
}

public static class SiteHeaderPresentation
{
    public static async Task<SiteHeaderViewModel> CreateAsync(HttpContext context, bool? mastheadDark, bool showAdminNav)
    {
        // The ambient User belongs to the admin scheme; authenticate member cookies separately.
        var memberAuth = await context.AuthenticateMemberAsync();
        var principal = memberAuth.Succeeded ? memberAuth.Principal : null;
        var memberId = Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedId)
            ? parsedId
            : (Guid?)null;
        var chrome = await LoadMemberChromeAsync(context.RequestServices, memberId);
        return new SiteHeaderViewModel(
            context.Request.Path.Value ?? "/",
            mastheadDark ?? true,
            principal?.FindFirstValue(ClaimTypes.Name),
            memberId,
            chrome.HasAvatar,
            chrome.UnreadCount,
            showAdminNav,
            showAdminNav ? AdminIdentity(context.User) : null);
    }

    private static string? AdminIdentity(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("preferred_username")
        ?? principal.Identity?.Name;

    private static async Task<(bool HasAvatar, int UnreadCount)> LoadMemberChromeAsync(IServiceProvider services, Guid? memberId)
    {
        if (memberId is not Guid id)
        {
            return (false, 0);
        }

        // Optional request chrome must not break rendering during a database outage/migration.
        try
        {
            var members = services.GetService<IMemberAccountRepository>();
            var account = members is null ? null : await members.FindByIdAsync(id);
            var hasAvatar = !string.IsNullOrWhiteSpace(account?.AvatarUrl);
            var messages = services.GetService<IPrivateMessageRepository>();
            var unread = messages is null ? 0 : await messages.CountUnreadConversationsAsync(id);
            return (hasAvatar, unread);
        }
        catch
        {
            return (false, 0);
        }
    }
}
