using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Data;
using OrderAssistant.Api.Entities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<OmsDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Oms")));
builder.Services.AddHealthChecks().AddDbContextCheck<OmsDb>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmsDb>();
    await db.Database.MigrateAsync(); // applies migrations on startup
    await Seeder.SeedAsync(db);
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health");

var orders = app.MapGroup("/api/orders").WithTags("Orders");

// GET /api/orders?q=jane&status=Shipped&page=1&pageSize=20
orders.MapGet("/", async (OmsDb db, string? q, OrderStatus? status, int page = 1, int pageSize = 20) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100);
    var query = db.Orders.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(o => o.OrderNumber.Contains(q) || o.Customer.Name.Contains(q));
    if (status is not null)
        query = query.Where(o => o.Status == status);

    var items = await query
        .OrderByDescending(o => o.CreatedAtUtc)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .Select(o => new OrderSummary(o.Id, o.OrderNumber, o.Customer.Name, o.Status.ToString(),
            o.CreatedAtUtc, o.Lines.Sum(l => l.Quantity * l.UnitPrice)))
        .ToListAsync();

    return TypedResults.Ok(items);
});

// GET /api/orders/42
orders.MapGet("/{id:int}", async Task<Results<Ok<OrderDetail>, NotFound>> (int id, OmsDb db) =>
{
    var order = await db.Orders.AsNoTracking()
        .Where(o => o.Id == id)
        .Select(o => new OrderDetail(o.Id, o.OrderNumber, o.Customer.Name, o.Status.ToString(), o.CreatedAtUtc,
            o.Lines.Select(l => new OrderLineDto(l.Product.Sku, l.Product.Name, l.Quantity, l.UnitPrice)).ToList()))
        .FirstOrDefaultAsync();

    return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
});

// GET /api/inventory/SKU-1001
app.MapGet("/api/inventory/{sku}", async Task<Results<Ok<List<StockDto>>, NotFound>> (string sku, OmsDb db) =>
{
    var stock = await db.Inventory.AsNoTracking()
        .Where(i => i.Product.Sku == sku)
        .Select(i => new StockDto(i.Warehouse.Code, i.Warehouse.Name, i.QuantityOnHand))
        .ToListAsync();

    return stock.Count == 0 ? TypedResults.NotFound() : TypedResults.Ok(stock);
}).WithTags("Inventory");

app.Run();

record OrderSummary(int Id, string OrderNumber, string Customer, string Status, DateTime CreatedAtUtc, decimal Total);
record OrderLineDto(string Sku, string Product, int Quantity, decimal UnitPrice);
record OrderDetail(int Id, string OrderNumber, string Customer, string Status, DateTime CreatedAtUtc, List<OrderLineDto> Lines);
record StockDto(string WarehouseCode, string Warehouse, int QuantityOnHand);