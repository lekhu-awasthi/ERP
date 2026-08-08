namespace ErpApp.Application.Common.Exceptions;

/// <summary>Requested entity does not exist. Maps to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);
