namespace ErpApp.Application.Common.Exceptions;

/// <summary>Login credentials were invalid. Maps to HTTP 401. Kept deliberately generic
/// (never distinguishes "unknown email" from "wrong password") to avoid user enumeration.</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);
