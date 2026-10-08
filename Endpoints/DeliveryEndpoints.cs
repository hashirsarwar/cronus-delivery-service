using Cronus.DeliveryService.Contracts;
using Cronus.DeliveryService.Errors;
using Cronus.DeliveryService.Services;

namespace Cronus.DeliveryService.Endpoints;

public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var deliveries = app.MapGroup("/deliveries").WithTags("Deliveries");

        deliveries.MapPost("/", async (
            CreateDeliveryRequest request,
            DeliveryManager deliveryManager,
            CancellationToken cancellationToken) =>
        {
            RequestValidator.EnsureValid(request);

            var result = await deliveryManager.GetOrCreateAsync(request, cancellationToken);
            var body = DeliveryResponse.FromEntity(result.Delivery);

            return result.WasCreated
                ? Results.Created($"/deliveries/{result.Delivery.Id}", body)
                : Results.Ok(body);
        })
        .WithName("CreateDelivery")
        .WithSummary("Creates the delivery for an order. Repeat calls for the same order return the existing delivery.");

        deliveries.MapGet("/{deliveryId:guid}", async (
            Guid deliveryId,
            DeliveryManager deliveryManager,
            CancellationToken cancellationToken) =>
        {
            var delivery = await deliveryManager.GetAsync(deliveryId, cancellationToken);
            return Results.Ok(DeliveryResponse.FromEntity(delivery));
        })
        .WithName("GetDelivery")
        .WithSummary("Returns one delivery.");

        return app;
    }
}
