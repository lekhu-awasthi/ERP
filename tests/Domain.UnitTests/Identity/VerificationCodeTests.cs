using ErpApp.Domain.Identity;

namespace ErpApp.Domain.UnitTests.Identity;

public class VerificationCodeTests
{
    [Fact]
    public void IsValid_true_for_matching_unexpired_unconsumed_code()
    {
        var code = VerificationCode.Issue(Guid.NewGuid(), "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));

        Assert.True(code.IsValid("123456", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsValid_false_for_mismatched_code()
    {
        var code = VerificationCode.Issue(Guid.NewGuid(), "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));

        Assert.False(code.IsValid("000000", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsValid_false_once_expired()
    {
        var code = VerificationCode.Issue(Guid.NewGuid(), "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));

        Assert.False(code.IsValid("123456", DateTimeOffset.UtcNow.AddMinutes(16)));
    }

    [Fact]
    public void IsValid_false_once_consumed()
    {
        var code = VerificationCode.Issue(Guid.NewGuid(), "123456", VerificationCodePurpose.EmailVerification, TimeSpan.FromMinutes(15));
        code.Consume();

        Assert.False(code.IsValid("123456", DateTimeOffset.UtcNow));
    }
}
