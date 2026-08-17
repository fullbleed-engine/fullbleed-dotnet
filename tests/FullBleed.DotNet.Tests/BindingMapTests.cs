using System.Globalization;

namespace FullBleed.DotNet.Tests;

public sealed class BindingMapTests
{
    [Fact]
    public void MaterializeBuildsInvariantColumnarBindingsInInputOrder()
    {
        var records = new[]
        {
            new Invoice("INV-002", "Ada", 12.5m),
            new Invoice("INV-001", "Grace", 3m),
        };
        var map = new BindingMap<Invoice>()
            .Bind("invoice_id", invoice => invoice.Id)
            .Bind("customer", invoice => invoice.Customer)
            .Bind("amount", invoice => invoice.Amount, "0.00", CultureInfo.InvariantCulture);

        var bindings = map.Materialize(records.OrderBy(invoice => invoice.Id));

        Assert.Equal(["INV-001", "INV-002"], bindings["invoice_id"]);
        Assert.Equal(["Grace", "Ada"], bindings["customer"]);
        Assert.Equal(["3.00", "12.50"], bindings["amount"]);
    }

    [Fact]
    public void MaterializeRejectsDuplicateSlots()
    {
        var map = new BindingMap<int>()
            .Bind("value", value => value)
            .Bind("value", value => value + 1);

        var error = Assert.Throws<InvalidOperationException>(() => map.Materialize([1]));

        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MaterializeRejectsEmptySequences()
    {
        var map = new BindingMap<int>().Bind("value", value => value);

        Assert.Throws<ArgumentException>(() => map.Materialize([]));
    }

    private sealed record Invoice(string Id, string Customer, decimal Amount);
}
