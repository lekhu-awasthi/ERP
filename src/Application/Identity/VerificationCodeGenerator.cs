using System.Globalization;
using System.Security.Cryptography;

namespace ErpApp.Application.Identity;

/// <summary>Generates the 6-digit numeric codes used for both email verification and password reset.</summary>
internal static class VerificationCodeGenerator
{
    public static string GenerateSixDigitCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}
