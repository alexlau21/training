using OrderHub.Core.Domain;

namespace OrderHub.Tests;

public class ProductServiceLowStockTests
{
    [Fact]
    public async Task GetLowStock_FiltersByThresholdAndSortsAscending()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-B001", stock: 3);
        TestSetup.AddProduct(db, sku: "SKU-B002", stock: 8);
        TestSetup.AddProduct(db, sku: "SKU-B003", stock: 10);
        TestSetup.AddProduct(db, sku: "SKU-B004", stock: 20);

        var rows = await service.GetLowStockAsync(10);

        Assert.Equal(2, rows.Count);
        Assert.Equal("SKU-B001", rows[0].Sku);
        Assert.Equal("SKU-B002", rows[1].Sku);
    }

    [Fact]
    public async Task GetLowStock_ExcludesInactiveProducts()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        TestSetup.AddProduct(db, sku: "SKU-C001", stock: 2, isActive: false);
        TestSetup.AddProduct(db, sku: "SKU-C002", stock: 2, isActive: true);

        var rows = await service.GetLowStockAsync(10);

        Assert.Single(rows);
        Assert.Equal("SKU-C002", rows[0].Sku);
    }

    [Fact]
    public async Task GetLowStock_SoldLast30Days_ExcludesCancelledAndOldOrders()
    {
        using var db = TestSetup.CreateContext();
        var service = TestSetup.CreateProductService(db);
        var customer = TestSetup.AddCustomer(db);
        var product = TestSetup.AddProduct(db, sku: "SKU-D001", stock: 5);

        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Confirmed,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 4, UnitPriceSnapshot = 100m } }
        });
        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Cancelled,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 100, UnitPriceSnapshot = 100m } }
        });
        db.Orders.Add(new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Shipped,
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            Items = { new OrderItem { ProductId = product.Id, Quantity = 100, UnitPriceSnapshot = 100m } }
        });
        db.SaveChanges();

        var rows = await service.GetLowStockAsync(10);

        Assert.Single(rows);
        Assert.Equal(4, rows[0].SoldLast30Days);
    }
}
