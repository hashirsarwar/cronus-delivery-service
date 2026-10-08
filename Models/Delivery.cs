using System.ComponentModel.DataAnnotations;

namespace Cronus.DeliveryService.Models;

public class Delivery
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    [MaxLength(200)]
    public required string CustomerName { get; set; }

    [MaxLength(300)]
    public required string AddressLine { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
