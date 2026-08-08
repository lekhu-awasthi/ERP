namespace ErpApp.Application.Common.Exceptions;

/// <summary>The supplied verification code is wrong, expired, or already used. Maps to HTTP 400.</summary>
public sealed class InvalidVerificationCodeException(string message) : Exception(message);
