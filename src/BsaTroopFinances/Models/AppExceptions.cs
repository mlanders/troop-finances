namespace BsaTroopFinances.Models;

/// <summary>Thrown when a login code is missing, expired, or has been guessed wrong too many times.</summary>
public class InvalidLoginCodeException(string message) : Exception(message);

/// <summary>Thrown when a code was requested again too soon after the last one (anti-spam throttle).</summary>
public class LoginCodeRateLimitedException(TimeSpan retryAfter)
    : Exception($"Please wait {Math.Ceiling(retryAfter.TotalSeconds)} seconds before requesting another code.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
