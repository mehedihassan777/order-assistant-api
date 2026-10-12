using OrderAssistant.Api.Entities;

namespace OrderAssistant.Api.Contracts;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record OrderSummary(int Id, string OrderNumber, string Customer, OrderStatus Status, DateTime CreatedAtUtc, decimal Total);

public record OrderLineDto(string Sku, string Product, int Quantity, decimal UnitPrice);

public record OrderDetail(int Id, string OrderNumber, string Customer, OrderStatus Status, DateTime CreatedAtUtc, List<OrderLineDto> Lines);

public record StockDto(string WarehouseCode, string Warehouse, int QuantityOnHand);
