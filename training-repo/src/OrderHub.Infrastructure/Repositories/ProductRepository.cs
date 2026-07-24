using Microsoft.EntityFrameworkCore;
using OrderHub.Core.Common;
using OrderHub.Core.Domain;
using OrderHub.Core.Interfaces;
using OrderHub.Infrastructure.Data;

namespace OrderHub.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly OrderHubDbContext _db;

    public ProductRepository(OrderHubDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync() =>
        await _db.Products.OrderBy(p => p.Sku).ToListAsync();

    public async Task<IReadOnlyList<Product>> GetActiveAsync() =>
        await _db.Products.Where(p => p.IsActive).OrderBy(p => p.Sku).ToListAsync();

    public Task<Product?> GetByIdAsync(int id) =>
        _db.Products.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<ProductLowStockRow>> GetLowStockAsync(int threshold, DateTime since) =>
        await _db.Products
            .Where(p => p.IsActive && p.StockQuantity < threshold)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new ProductLowStockRow(
                p.Sku,
                p.Name,
                p.StockQuantity,
                _db.OrderItems
                    .Where(oi => oi.ProductId == p.Id
                        && oi.Order!.Status != OrderStatus.Cancelled
                        && oi.Order.CreatedAt >= since)
                    .Sum(oi => (int?)oi.Quantity) ?? 0))
            .ToListAsync();

    public Task SaveChangesAsync() => _db.SaveChangesAsync();
}
