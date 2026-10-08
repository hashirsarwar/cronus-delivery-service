namespace Cronus.DeliveryService.Errors;

/// <summary>A requested resource does not exist. Reported as HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);
