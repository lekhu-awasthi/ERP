namespace ErpApp.Application.Common.Exceptions;

/// <summary>Login was attempted before the account finished email verification. Maps to HTTP 403.</summary>
public sealed class EmailNotVerifiedException(string message) : Exception(message);
