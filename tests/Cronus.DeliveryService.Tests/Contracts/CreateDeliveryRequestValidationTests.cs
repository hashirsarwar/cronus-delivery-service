using Cronus.DeliveryService.Contracts;
using Cronus.DeliveryService.Errors;

namespace Cronus.DeliveryService.Tests.Contracts;

/// <summary>
/// These are contract-level rules, so they need no database.
/// </summary>
[TestClass]
public sealed class CreateDeliveryRequestValidationTests
{
    [TestMethod]
    public void EnsureValid_AcceptsACompleteRequest() => RequestValidator.EnsureValid(Complete());

    [TestMethod]
    public void EnsureValid_ReportsMissingOrderId_UnderTheCamelCaseFieldName()
    {
        var exception = Assert.ThrowsExactly<RequestValidationException>(() =>
            RequestValidator.EnsureValid(Complete() with { OrderId = Guid.Empty }));

        Assert.IsTrue(
            exception.Errors.ContainsKey("orderId"),
            $"Expected an 'orderId' error but found: {string.Join(", ", exception.Errors.Keys)}");
    }

    [TestMethod]
    public void EnsureValid_ReportsEveryFailure_NotOnlyTheFirst()
    {
        var exception = Assert.ThrowsExactly<RequestValidationException>(() =>
            RequestValidator.EnsureValid(new CreateDeliveryRequest(Guid.Empty, null, "   ", null, null)));

        CollectionAssert.AreEquivalent(
            new[] { "orderId", "customerName", "addressLine" },
            exception.Errors.Keys.ToArray());
    }

    [TestMethod]
    public void EnsureValid_EnforcesMaximumLengths()
    {
        var exception = Assert.ThrowsExactly<RequestValidationException>(() =>
            RequestValidator.EnsureValid(Complete() with { CustomerName = new string('a', 201) }));

        Assert.IsTrue(exception.Errors.ContainsKey("customerName"));
    }

    [TestMethod]
    public void EnsureValid_TreatsWhitespaceAsMissing()
    {
        var exception = Assert.ThrowsExactly<RequestValidationException>(() =>
            RequestValidator.EnsureValid(Complete() with { AddressLine = "   " }));

        Assert.IsTrue(exception.Errors.ContainsKey("addressLine"));
    }

    private static CreateDeliveryRequest Complete() => new(
        Guid.NewGuid(),
        "Ada Lovelace",
        "1 Analytical Way",
        "London",
        "E1 6AN");
}
