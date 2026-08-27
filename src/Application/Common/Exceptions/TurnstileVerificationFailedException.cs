namespace ErpApp.Application.Common.Exceptions;

public sealed class TurnstileVerificationFailedException(string message) : Exception(message);
