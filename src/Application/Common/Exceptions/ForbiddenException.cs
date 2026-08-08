namespace ErpApp.Application.Common.Exceptions;

/// <summary>Authenticated, but not allowed to perform this action (e.g. not an org admin). Maps to HTTP 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);
