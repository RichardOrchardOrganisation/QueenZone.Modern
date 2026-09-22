using System.Collections.Concurrent;

namespace QueenZone.Web;

/// <summary>
/// Process-local pending PKCE sessions for the few minutes between /authorize and the
/// provider callback. Single-instance hosting makes a distributed store unnecessary.
/// </summary>
public sealed class MobileAuthAuthorizationSessionStore(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, MobileAuthAuthorizationSession> sessions = new(StringComparer.Ordinal);

    public MobileAuthAuthorizationSession Create(
        string clientId,
        string redirectUri,
        string codeChallenge,
        string state,
        string provider,
        TimeSpan lifetime)
    {
        var session = new MobileAuthAuthorizationSession(
            RequestId: MobileAuthPkce.CreateOpaqueToken(),
            ClientId: clientId,
            RedirectUri: redirectUri,
            CodeChallenge: codeChallenge,
            State: state,
            Provider: provider,
            ExpiresAt: timeProvider.GetUtcNow().Add(lifetime));

        sessions[session.RequestId] = session;
        return session;
    }

    public MobileAuthAuthorizationSession? Peek(string requestId)
    {
        if (!sessions.TryGetValue(requestId, out var session))
        {
            return null;
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            sessions.TryRemove(requestId, out _);
            return null;
        }

        return session;
    }

    public MobileAuthAuthorizationSession? Take(string requestId)
    {
        if (!sessions.TryRemove(requestId, out var session))
        {
            return null;
        }

        if (session.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return null;
        }

        return session;
    }
}
