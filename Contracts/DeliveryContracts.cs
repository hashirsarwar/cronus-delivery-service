using System.ComponentModel.DataAnnotations;
using Cronus.DeliveryService.Errors;
using Cronus.DeliveryService.Models;

namespace Cronus.DeliveryService.Contracts;

public sealed record CreateDeliveryRequest(
    [property: NotEmpty(ErrorMessage = "orderId is required.")]
    Guid OrderId,
    [property: NotEmpty(ErrorMessage = "customerName is required.")]
    [property: MaxLength(200, ErrorMessage = "customerName cannot exceed 200 characters.")]
    string? CustomerName,
    [property: NotEmpty(ErrorMessage = "addressLine is required.")]
    [property: MaxLength(300, ErrorMessage = "addressLine cannot exceed 300 characters.")]
    string? AddressLine,
    [property: MaxLength(100, ErrorMessage = "city cannot exceed 100 characters.")]
    string? City,
    [property: MaxLength(20, ErrorMessage = "postalCode cannot exceed 20 characters.")]
    string? PostalCode);

public sealed record DeliveryResponse(
    Guid Id,
    Guid OrderId,
    DeliveryStatus Status,
    string CustomerName,
    string AddressLine,
    string? City,
    string? PostalCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DeliveryResponse FromEntity(Delivery delivery) => new(
        delivery.Id,
        delivery.OrderId,
        delivery.Status,
        delivery.CustomerName,
        delivery.AddressLine,
        delivery.City,
        delivery.PostalCode,
        delivery.CreatedAt,
        delivery.UpdatedAt);
}
