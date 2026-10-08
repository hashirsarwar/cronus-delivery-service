using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Cronus.DeliveryService.Errors;

/// <summary>
/// Validates a request contract from its DataAnnotations attributes and throws a single
/// <see cref="RequestValidationException"/> describing every failure, so endpoints contain no
/// hand-written validation branching.
/// </summary>
public static class RequestValidator
{
    public static void EnsureValid(object request)
    {
        var failures = new List<ValidationResult>();

        if (Validator.TryValidateObject(request, new ValidationContext(request), failures, validateAllProperties: true))
        {
            return;
        }

        var errors = failures
            .SelectMany(
                failure => failure.MemberNames.DefaultIfEmpty(string.Empty),
                (failure, member) => (Member: member, Message: failure.ErrorMessage ?? "The value is invalid."))
            .GroupBy(x => x.Member, StringComparer.Ordinal)
            .ToDictionary(
                group => ToJsonPropertyName(group.Key),
                group => group.Select(x => x.Message).ToArray());

        throw new RequestValidationException(errors);
    }

    /// <summary>Aligns field keys with the camelCase property names clients see.</summary>
    private static string ToJsonPropertyName(string memberName) =>
        string.IsNullOrEmpty(memberName) ? string.Empty : JsonNamingPolicy.CamelCase.ConvertName(memberName);
}
