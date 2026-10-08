using Cronus.DeliveryService.Contracts;
using Cronus.DeliveryService.Data;
using Cronus.DeliveryService.Errors;
using Cronus.DeliveryService.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cronus.DeliveryService.Services;

/// <summary>Owns delivery creation and lookup, idempotent per order.</summary>
public sealed class DeliveryManager(DeliveryDbContext db, ILogger<DeliveryManager> logger)
{
    /// <summary>
    /// Returns the delivery for an order, creating it on first request. Idempotent per order, so a
    /// caller that retries after a timeout cannot produce a duplicate.
    /// </summary>
    public async Task<DeliveryCreationResult> GetOrCreateAsync(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindByOrderAsync(request.OrderId, cancellationToken);
        if (existing is not null)
        {
            // Retries do not change business state, so log existing deliveries at debug level.
            logger.LogDebug(
                "Delivery {DeliveryId} already exists for order {OrderId}; returning it unchanged.",
                existing.Id,
                request.OrderId);

            return new DeliveryCreationResult(existing, WasCreated: false);
        }

        var delivery = new Delivery
        {
            OrderId = request.OrderId,
            CustomerName = request.CustomerName!.Trim(),
            AddressLine = request.AddressLine!.Trim(),
            City = Normalise(request.City),
            PostalCode = Normalise(request.PostalCode),
        };

        db.Deliveries.Add(delivery);

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Created delivery {DeliveryId} for order {OrderId} with status {Status}.",
                delivery.Id,
                delivery.OrderId,
                delivery.Status);

            return new DeliveryCreationResult(delivery, WasCreated: true);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another request created the delivery between our read and our write. The unique index on
            // OrderId is the source of truth, so read the winner back rather than failing the request.
            db.Entry(delivery).State = EntityState.Detached;

            var winner = await FindByOrderAsync(request.OrderId, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"A delivery for order '{request.OrderId}' exists but could not be read back.");

            logger.LogInformation(
                "A delivery for order {OrderId} was created concurrently; returning delivery {DeliveryId}.",
                request.OrderId,
                winner.Id);

            return new DeliveryCreationResult(winner, WasCreated: false);
        }
    }

    public async Task<Delivery> GetAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        await db.Deliveries
            .AsNoTracking()
            .SingleOrDefaultAsync(delivery => delivery.Id == deliveryId, cancellationToken)
        ?? throw new NotFoundException($"Delivery '{deliveryId}' was not found.");

    private Task<Delivery?> FindByOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Deliveries
            .AsNoTracking()
            .SingleOrDefaultAsync(delivery => delivery.OrderId == orderId, cancellationToken);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Whether the delivery already existed, so the endpoint can choose between 201 and 200.</summary>
public sealed record DeliveryCreationResult(Delivery Delivery, bool WasCreated);
