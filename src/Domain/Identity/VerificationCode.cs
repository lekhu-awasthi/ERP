namespace ErpApp.Domain.Identity;

/// <summary>
/// A one-time code issued to a User for either email verification or password reset
/// (roadmap Phase 1a task 2). Shared between both purposes rather than two near-identical
/// tables — same shape (code, expiry, userId), distinguished by <see cref="Purpose"/>.
/// </summary>
public sealed class VerificationCode
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Code { get; private set; } = null!;
    public VerificationCodePurpose Purpose { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }

    private VerificationCode()
    {
    }

    public static VerificationCode Issue(Guid userId, string code, VerificationCodePurpose purpose, TimeSpan validFor)
    {
        var now = DateTimeOffset.UtcNow;

        return new VerificationCode
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Code = code,
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now.Add(validFor),
        };
    }

    public bool IsValid(string code, DateTimeOffset now) =>
        ConsumedAt is null && now <= ExpiresAt && Code == code;

    public void Consume() => ConsumedAt = DateTimeOffset.UtcNow;
}
