namespace SSMConnect.Core;

internal static class TunnelRetryPolicy
{
    public static int DelaySeconds(int failures) => Math.Min(60, 2 << Math.Clamp(failures - 1, 0, 5));

    public static bool IsTransient(string reason)
    {
        var text = reason.ToLowerInvariant();
        // Authentication and configuration errors still require an explicit user correction.
        if (new[] { "expired", "sign-in", "sign in", "accessdenied", "unauthorized", "not allowed", "not installed", "not valid", "already used", "address already in use" }.Any(text.Contains)) return false;
        return new[] { "network", "timed out", "timeout", "did not connect within", "rate-limiting", "cannot reach aws", "connection reset", "connection closed", "connection dropped", "tunnel port closed", "wsarecv", "socket", "targetnotconnected", "server is offline", "i/o", "eof" }.Any(text.Contains);
    }
}
