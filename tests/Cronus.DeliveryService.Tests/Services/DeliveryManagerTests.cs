using Cronus.DeliveryService.Contracts;
using Cronus.DeliveryService.Data;
using Cronus.DeliveryService.Errors;
using Cronus.DeliveryService.Models;
using Cronus.DeliveryService.Services;
using Cronus.DeliveryService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cronus.DeliveryService.Tests.Services;

[TestClass]
public sealed class DeliveryManagerTests
{
    [TestMethod]
    public async Task GetOrCreateAsync_CreatesADelivery_WhenTheOrderHasNone()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();
        var orderId = Guid.NewGuid();

        var result = await CreateManager(db).GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);

        Assert.IsTrue(result.WasCreated);
        Assert.AreEqual(orderId, result.Delivery.OrderId);
        Assert.AreEqual(DeliveryStatus.Pending, result.Delivery.Status);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_PersistsTheDelivery()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();
        var orderId = Guid.NewGuid();

        await CreateManager(db).GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);

        await using var verification = TestDatabase.CreateContext();
        var stored = await verification.Deliveries.SingleAsync(delivery => delivery.OrderId == orderId);
        Assert.AreEqual("Ada Lovelace", stored.CustomerName);
        Assert.AreEqual("1 Analytical Way", stored.AddressLine);
        Assert.AreEqual("London", stored.City);
    }

    [TestMethod]
    public async Task GetOrCreateAsync_IsIdempotent_ForTheSameOrder()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();
        var orderId = Guid.NewGuid();
        var manager = CreateManager(db);

        var first = await manager.GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);
        var second = await manager.GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);

        Assert.IsTrue(first.WasCreated, "the first request should create the delivery");
        Assert.IsFalse(second.WasCreated, "a repeated request should not create a second delivery");
        Assert.AreEqual(first.Delivery.Id, second.Delivery.Id);

        await using var verification = TestDatabase.CreateContext();
        Assert.AreEqual(
            1,
            await verification.Deliveries.CountAsync(delivery => delivery.OrderId == orderId));
    }

    [TestMethod]
    public async Task GetOrCreateAsync_CreatesExactlyOneDelivery_UnderConcurrentRequests()
    {
        await TestDatabase.ResetAsync();
        var orderId = Guid.NewGuid();

        // Each attempt opens its own context, so the requests hold separate connections and genuinely
        // contend on the unique index over OrderId instead of being serialised by one change tracker.
        var attempts = Enumerable
            .Range(0, 8)
            .Select(_ => CreateDeliveryInOwnContextAsync(orderId))
            .ToArray();

        var results = await Task.WhenAll(attempts);

        Assert.AreEqual(
            1,
            results.Count(result => result.WasCreated),
            "exactly one concurrent caller should create the delivery");
        Assert.AreEqual(
            1,
            results.Select(result => result.Delivery.Id).Distinct().Count(),
            "every caller should be handed the same delivery");

        await using var verification = TestDatabase.CreateContext();
        Assert.AreEqual(
            1,
            await verification.Deliveries.CountAsync(delivery => delivery.OrderId == orderId));
    }

    [TestMethod]
    public async Task GetOrCreateAsync_TrimsValues_AndTreatsBlankOptionalsAsAbsent()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();

        var result = await CreateManager(db).GetOrCreateAsync(
            new CreateDeliveryRequest(Guid.NewGuid(), "  Ada Lovelace  ", " 1 Analytical Way ", "   ", ""),
            CancellationToken.None);

        Assert.AreEqual("Ada Lovelace", result.Delivery.CustomerName);
        Assert.AreEqual("1 Analytical Way", result.Delivery.AddressLine);
        Assert.IsNull(result.Delivery.City);
        Assert.IsNull(result.Delivery.PostalCode);
    }

    [TestMethod]
    public async Task GetAsync_ReturnsTheStoredDelivery()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();
        var created = await CreateManager(db).GetOrCreateAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);

        var found = await CreateManager(db).GetAsync(created.Delivery.Id, CancellationToken.None);

        Assert.AreEqual(created.Delivery.Id, found.Id);
        Assert.AreEqual(created.Delivery.OrderId, found.OrderId);
    }

    [TestMethod]
    public async Task GetAsync_ThrowsNotFound_ForAnUnknownId()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();

        await Assert.ThrowsExactlyAsync<NotFoundException>(() =>
            CreateManager(db).GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetOrCreateAsync_LogsTheCreatedDeliveryAsStructuredProperties()
    {
        await using var db = await TestDatabase.CreateCleanContextAsync();
        var orderId = Guid.NewGuid();

        var logger = new RecordingLogger<DeliveryManager>();
        var manager = new DeliveryManager(db, logger);

        var result = await manager.GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);

        // Selected by property name rather than by position, so adding another log line does not
        // silently change what this asserts.
        var created = logger.Entries.Single(entry => entry.Properties.ContainsKey("DeliveryId"));

        Assert.AreEqual(result.Delivery.Id, (Guid)created.Properties["DeliveryId"]!);
        Assert.AreEqual(orderId, (Guid)created.Properties["OrderId"]!);
        Assert.AreEqual(result.Delivery.Status, created.Properties["Status"]);
    }

    private static DeliveryManager CreateManager(DeliveryDbContext db) =>
        new(db, NullLogger<DeliveryManager>.Instance);

    private static CreateDeliveryRequest NewRequest(Guid orderId) =>
        new(orderId, "Ada Lovelace", "1 Analytical Way", "London", "E1 6AN");

    private static async Task<DeliveryCreationResult> CreateDeliveryInOwnContextAsync(Guid orderId)
    {
        await using var db = TestDatabase.CreateContext();
        return await CreateManager(db).GetOrCreateAsync(NewRequest(orderId), CancellationToken.None);
    }
}
