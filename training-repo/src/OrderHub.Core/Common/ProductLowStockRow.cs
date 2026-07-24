namespace OrderHub.Core.Common;

public record ProductLowStockRow(string Sku, string Name, int StockQuantity, int SoldLast30Days);
