namespace ErpApp.Application.Common.Security;

public interface IJwtTokenGenerator
{
    JwtToken GenerateToken(Guid userId, string email);
}

public readonly record struct JwtToken(string Value, DateTimeOffset ExpiresAt);
