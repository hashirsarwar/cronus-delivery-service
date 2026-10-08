using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cronus.DeliveryService.Tests.Infrastructure;

namespace Cronus.DeliveryService.Tests.Endpoints;

/// <summary>
/// Exercises the HTTP surface through the real application host, so routing, model binding,
/// serialization and error handling are all covered. Every test uses its own order identifier and
/// scopes its assertions to it, so the tests do not depend on each other's leftovers.
/// </summary>
[TestClass]
public sealed class DeliveryEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task CreateDelivery_ReturnsCreated_OnTheFirstRequest()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        var orderId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/deliveries", NewRequest(orderId), JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DeliveryBody>(JsonOptions);
        Assert.IsNotNull(body);
        Assert.AreEqual(orderId, body.OrderId);
        Assert.AreEqual("Pending", body.Status);
        Assert.AreEqual("Ada Lovelace", body.CustomerName);
        Assert.AreEqual($"/deliveries/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task CreateDelivery_ReturnsOkWithTheSameDelivery_OnARepeatRequest()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();
        var orderId = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/deliveries", NewRequest(orderId), JsonOptions);
        var second = await client.PostAsJsonAsync("/deliveries", NewRequest(orderId), JsonOptions);

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<DeliveryBody>(JsonOptions);
        var secondBody = await second.Content.ReadFromJsonAsync<DeliveryBody>(JsonOptions);

        Assert.IsNotNull(firstBody);
        Assert.IsNotNull(secondBody);
        Assert.AreEqual(firstBody.Id, secondBody.Id);
    }

    [TestMethod]
    public async Task CreateDelivery_ReturnsAValidationProblem_WhenRequiredFieldsAreMissing()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/deliveries", new { }, JsonOptions);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>(JsonOptions);
        Assert.IsNotNull(problem);
        Assert.IsNotNull(problem.Errors);
        CollectionAssert.AreEquivalent(
            new[] { "orderId", "customerName", "addressLine" },
            problem.Errors.Keys.ToArray());
    }

    [TestMethod]
    public async Task CreateDelivery_ReturnsAValidationProblem_WhenValuesAreOutOfRange()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/deliveries",
            NewRequest(Guid.NewGuid()) with { CustomerName = new string('a', 201) },
            JsonOptions);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>(JsonOptions);
        Assert.IsNotNull(problem?.Errors);
        Assert.IsTrue(problem.Errors.ContainsKey("customerName"));
    }

    [TestMethod]
    public async Task GetDelivery_ReturnsTheDelivery()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/deliveries", NewRequest(Guid.NewGuid()), JsonOptions);
        var createdBody = await created.Content.ReadFromJsonAsync<DeliveryBody>(JsonOptions);
        Assert.IsNotNull(createdBody);

        var response = await client.GetAsync($"/deliveries/{createdBody.Id}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DeliveryBody>(JsonOptions);
        Assert.IsNotNull(body);
        Assert.AreEqual(createdBody.Id, body.Id);
        Assert.AreEqual(createdBody.OrderId, body.OrderId);
    }

    [TestMethod]
    public async Task GetDelivery_ReturnsNotFound_ForAnUnknownId()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/deliveries/{Guid.NewGuid()}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Liveness_ReportsHealthy_AndRunsNoChecks()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual("Healthy", body.Status);

        // An empty check list is the point: liveness must not depend on anything downstream.
        Assert.HasCount(0, body.Checks);
    }

    [TestMethod]
    public async Task Readiness_ReportsHealthy_WhenTheDatabaseIsReachable()
    {
        using var factory = await CreateFactoryAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual("Healthy", body.Status);
        Assert.AreEqual("Healthy", body.Checks["postgresql"].Status);
    }

    [TestMethod]
    public async Task Liveness_StaysHealthy_WhenTheDatabaseIsUnreachable()
    {
        // The reason the probes are split: a database outage must not make an otherwise healthy
        // process look dead, because the orchestrator would restart it in a loop to no effect.
        using var factory = DeliveryApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Readiness_ReportsUnhealthy_WhenTheDatabaseIsUnreachable()
    {
        using var factory = DeliveryApiFactory.WithUnreachableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<HealthBody>(JsonOptions);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsNotNull(body);
        Assert.AreEqual("Unhealthy", body.Status);
        Assert.AreEqual("Unhealthy", body.Checks["postgresql"].Status);

        // The detail is returned to callers, so it must not describe where the database lives.
        Assert.DoesNotContain("127.0.0.1", body.Checks["postgresql"].Description ?? string.Empty);
        Assert.DoesNotContain("Port=1", body.Checks["postgresql"].Description ?? string.Empty);
    }

    /// <summary>
    /// The application only migrates in the Development environment, so the suite owns the schema and
    /// must ensure the database exists before the host starts.
    /// </summary>
    private static async Task<DeliveryApiFactory> CreateFactoryAsync()
    {
        await TestDatabase.EnsureCreatedAsync();
        return new DeliveryApiFactory();
    }

    private static DeliveryRequestBody NewRequest(Guid orderId) => new(
        orderId,
        "Ada Lovelace",
        "1 Analytical Way",
        "London",
        "E1 6AN");

    private sealed record DeliveryRequestBody(
        Guid OrderId,
        string CustomerName,
        string AddressLine,
        string? City,
        string? PostalCode);

    private sealed record DeliveryBody(
        Guid Id,
        Guid OrderId,
        string Status,
        string CustomerName,
        string AddressLine,
        string? City,
        string? PostalCode);

    private sealed record ValidationProblemBody(
        string? Title,
        int? Status,
        Dictionary<string, string[]>? Errors);

    private sealed record HealthBody(string Status, Dictionary<string, HealthCheckBody> Checks);

    private sealed record HealthCheckBody(string Status, string? Description);
}
