using OrderHub.Core.Domain;
using OrderHub.Core.Services;

namespace OrderHub.Tests;

public class OrderServicePricingTests
{
    [Theory]
    [InlineData(CustomerTier.Standard, 0)]
    [InlineData(CustomerTier.Silver, 0.05)]
    [InlineData(CustomerTier.Gold, 0.10)]
    public void GetDiscountRate_ReturnsExpectedRate(CustomerTier tier, decimal expected)
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);

        Assert.Equal(expected, service.GetDiscountRate(tier));
    }

    [Fact]
    public void CalculateSubtotal_SumsQuantityTimesSnapshotPrice()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);

        var order = new Order
        {
            Items =
            {
                new OrderItem { Quantity = 2, UnitPriceSnapshot = 150m },
                new OrderItem { Quantity = 3, UnitPriceSnapshot = 40m }
            }
        };

        Assert.Equal(420m, service.CalculateSubtotal(order));
    }

    [Theory]
    [InlineData(CustomerTier.Standard, 1000, 1000)]
    [InlineData(CustomerTier.Silver, 1000, 950)]
    [InlineData(CustomerTier.Gold, 1000, 900)]
    public void CalculateTotal_AppliesTierDiscountOnSubtotal(CustomerTier tier, decimal unitPrice, decimal expectedTotal)
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);

        var order = new Order
        {
            Customer = new Customer { Tier = tier },
            Items = { new OrderItem { Quantity = 1, UnitPriceSnapshot = unitPrice } }
        };

        Assert.Equal(expectedTotal, service.CalculateTotal(order));
    }

    [Fact]
    public void CalculateTotal_WithoutCustomer_UsesStandardRate()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);

        var order = new Order
        {
            Items = { new OrderItem { Quantity = 2, UnitPriceSnapshot = 250m } }
        };

        Assert.Equal(500m, service.CalculateTotal(order));
    }

    [Fact]
    public async Task CreateOrder_GoldCustomer_TotalDiscountedOnlyOnce()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db, tier: CustomerTier.Gold);
        var product = TestSetup.AddProduct(db, unitPrice: 1000m);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 1) });
        var order = result.Value!;
        order.Customer ??= customer;

        Assert.Equal(1000m, order.Items.Single().UnitPriceSnapshot);
        Assert.Equal(900m, service.CalculateTotal(order));
    }

    [Fact]
    public async Task CreateOrder_SilverCustomer_TotalDiscountedOnlyOnce()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateOrderService(db);
        var customer = TestSetup.AddCustomer(db, tier: CustomerTier.Silver);
        var product = TestSetup.AddProduct(db, unitPrice: 1000m);

        var result = await service.CreateOrderAsync(customer.Id, new[] { new NewOrderLine(product.Id, 1) });
        var order = result.Value!;
        order.Customer ??= customer;

        Assert.Equal(1000m, order.Items.Single().UnitPriceSnapshot);
        Assert.Equal(950m, service.CalculateTotal(order));
    }
}
