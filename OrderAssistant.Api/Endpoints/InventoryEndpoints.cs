using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Contracts;
using OrderAssistant.Api.Data;

namespace OrderAssistant.Api.Endpoints;

public static class InventoryEndpoints
{
    public static RouteGroupBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var inventory = app.MapGroup("/api/inventory").WithTags("Inventory");

        inventory.MapGet("/{sku}", GetStockBySku).WithName("GetStockBySku");

        return inventory;
    }

    // GET /api/inventory/SKU-1001
    static async Task<Results<Ok<List<StockDto>>, NotFound>> GetStockBySku(string sku, OmsDb db, CancellationToken ct)
    {
        // 404 only when the SKU is unknown; a known SKU with no stock rows returns [].
        if (!await db.Products.AnyAsync(p => p.Sku == sku, ct))
            return TypedResults.NotFound();

        var stock = await db.Inventory.AsNoTracking()
            .Where(i => i.Product.Sku == sku)
            .OrderBy(i => i.Warehouse.Code)
            .Select(i => new StockDto(i.Warehouse.Code, i.Warehouse.Name, i.QuantityOnHand))
            .ToListAsync(ct);

        return TypedResults.Ok(stock);
    }
}
