using System.ComponentModel.DataAnnotations;

namespace Cronus.DeliveryService.Errors;

/// <summary>
/// Fails when a value is null, an empty/whitespace string, or <see cref="Guid.Empty"/>.
/// <see cref="RequiredAttribute"/> cannot express the last case because a struct is never null.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value switch
    {
        null => false,
        Guid guid => guid != Guid.Empty,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true,
    };
}
