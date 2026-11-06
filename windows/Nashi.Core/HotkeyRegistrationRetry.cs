namespace Nashi.Core;

// Limits retries when another application temporarily owns the same Windows hotkey.
public sealed class HotkeyRegistrationRetry
{
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;

    public bool ShouldAttempt(bool enabled, bool registered, DateTimeOffset now)
        => enabled && !registered && now >= retryAfter;

    public void RecordFailure(DateTimeOffset now) => retryAfter = now.AddSeconds(2);

    public void Reset() => retryAfter = DateTimeOffset.MinValue;
}
