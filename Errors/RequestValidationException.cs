namespace Cronus.DeliveryService.Errors;

/// <summary>
/// One or more request fields are invalid. Carries the field errors so the API can return an
/// RFC 9457 validation problem. Reported as HTTP 400.
/// </summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.Ordinal);
    }

    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
