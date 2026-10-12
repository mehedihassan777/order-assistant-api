using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Contracts;
using OrderAssistant.Api.Data;
using OrderAssistant.Api.Entities;

namespace OrderAssistant.Api.Endpoints;

public static class OrderEndpoints
{
    public const int MaxPageSize = 100;

    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapGet("/", ListOrders).WithName("ListOrders");
        orders.MapGet("/{id:int}", GetOrder).WithName("GetOrder");

        return orders;
    }

    // GET /api/orders?q=jane&status=Shipped&page=1&pageSize=20
    static async Task<Results<Ok<PagedResult<OrderSummary>>, ValidationProblem>> ListOrders(
        OmsDb db, string? q, string? status, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var errors = new Dictionary<string, string[]>();
        // Parsed here rather than bound as an enum: binding failures surface as 500s behind
        // the exception handler, and this also accepts any casing ("shipped").
        OrderStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed) && !int.TryParse(status, out _))
                statusFilter = parsed;
            else
                errors["status"] = [$"Must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}."];
        }
        if (page < 1) errors["page"] = ["Must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var query = db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Case-insensitive "contains"; escape LIKE wildcards so % and _ match literally.
            var pattern = $"%{EscapeLike(q.Trim())}%";
            query = query.Where(o => EF.Functions.ILike(o.OrderNumber, pattern)
                                  || EF.Functions.ILike(o.Customer.Name, pattern));
        }
        if (statusFilter is not null)
            query = query.Where(o => o.Status == statusFilter);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id) // stable order so pages never overlap
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderSummary(o.Id, o.OrderNumber, o.Customer.Name, o.Status,
                o.CreatedAtUtc, o.Lines.Sum(l => l.Quantity * l.UnitPrice)))
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<OrderSummary>(items, page, pageSize, total));
    }

    // GET /api/orders/42
    static async Task<Results<Ok<OrderDetail>, NotFound>> GetOrder(int id, OmsDb db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OrderDetail(o.Id, o.OrderNumber, o.Customer.Name, o.Status, o.CreatedAtUtc,
                o.Lines.OrderBy(l => l.Id)
                    .Select(l => new OrderLineDto(l.Product.Sku, l.Product.Name, l.Quantity, l.UnitPrice))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    // PostgreSQL's default LIKE escape character is backslash.
    static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
