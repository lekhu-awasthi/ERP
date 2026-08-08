namespace ErpApp.Application.Common.Exceptions;

/// <summary>Request conflicts with existing state (e.g. duplicate email). Maps to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
