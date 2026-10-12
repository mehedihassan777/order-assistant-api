using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderAssistant.Api.Contracts;
using OrderAssistant.Api.Data;
using OrderAssistant.Api.Entities;

namespace OrderAssistant.Api.Tests;

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<PagedResult<OrderSummary>> GetOrders(string query = "")
    {
        var res = await _client.GetAsync($"/api/orders{query}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PagedResult<OrderSummary>>(Json))!;
    }

    [Fact]
    public void Uses_the_test_container_not_the_dev_database()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmsDb>();
        Assert.Equal(factory.ConnectionString, db.Database.GetConnectionString());
    }

    [Fact]
    public async Task Health_is_healthy()
    {
        Assert.Equal("Healthy", await _client.GetStringAsync("/health"));
    }

    [Fact]
    public async Task Orders_list_is_paged_with_total_count_newest_first()
    {
        var result = await GetOrders();

        Assert.Equal(1000, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(result.Items.OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id), result.Items);
    }

    [Fact]
    public async Task Pages_do_not_overlap_and_out_of_range_pages_are_empty()
    {
        var page1 = await GetOrders("?pageSize=100&page=1");
        var page2 = await GetOrders("?pageSize=100&page=2");
        Assert.Empty(page1.Items.Select(o => o.Id).Intersect(page2.Items.Select(o => o.Id)));

        var beyond = await GetOrders("?pageSize=100&page=11");
        Assert.Empty(beyond.Items);
        Assert.Equal(1000, beyond.TotalCount);
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?status=Bogus", "status")]
    [InlineData("?status=2", "status")]
    public async Task Invalid_list_parameters_return_400_problem_details(string query, string field)
    {
        var res = await _client.GetAsync($"/api/orders{query}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Search_is_case_insensitive_on_customer_and_order_number()
    {
        var sample = (await GetOrders("?pageSize=1")).Items[0];
        var lastName = sample.Customer.Split(' ')[^1];

        var byName = await GetOrders($"?pageSize=100&q={Uri.EscapeDataString(lastName.ToLowerInvariant())}");
        Assert.NotEmpty(byName.Items);
        Assert.All(byName.Items, o => Assert.Contains(lastName, o.Customer, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(byName.Items, o => o.Id == sample.Id);

        var byNumber = await GetOrders($"?q={sample.OrderNumber.ToLowerInvariant()}");
        Assert.Contains(byNumber.Items, o => o.Id == sample.Id);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task Search_treats_like_wildcards_literally(string term)
    {
        var result = await GetOrders($"?q={Uri.EscapeDataString(term)}");
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("shipped")]
    public async Task Status_filter_matches_any_casing(string status)
    {
        var result = await GetOrders($"?status={status}&pageSize=100");

        Assert.InRange(result.TotalCount, 1, 999);
        Assert.All(result.Items, o => Assert.Equal(OrderStatus.Shipped, o.Status));
    }

    [Fact]
    public async Task Order_detail_lines_add_up_to_the_list_total()
    {
        var summary = (await GetOrders("?pageSize=1")).Items[0];

        var detail = await _client.GetFromJsonAsync<OrderDetail>($"/api/orders/{summary.Id}", Json);

        Assert.NotNull(detail);
        Assert.Equal(summary.OrderNumber, detail.OrderNumber);
        Assert.NotEmpty(detail.Lines);
        Assert.Equal(summary.Total, detail.Lines.Sum(l => l.Quantity * l.UnitPrice));
    }

    [Fact]
    public async Task Unknown_order_returns_404_problem_details()
    {
        var res = await _client.GetAsync("/api/orders/999999");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Inventory_returns_one_row_per_warehouse()
    {
        var stock = await _client.GetFromJsonAsync<List<StockDto>>("/api/inventory/SKU-1001", Json);

        Assert.NotNull(stock);
        Assert.Equal(["WH-E", "WH-N", "WH-S"], stock.Select(s => s.WarehouseCode));
    }

    [Fact]
    public async Task Unknown_sku_returns_404()
    {
        var res = await _client.GetAsync("/api/inventory/NOPE-1");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Database_blocks_deleting_a_customer_who_has_orders()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmsDb>();
        var customerId = await db.Orders.Select(o => o.CustomerId).FirstAsync();

        // Raw SQL, so the foreign key itself is what is being tested.
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM \"Customers\" WHERE \"Id\" = {customerId}"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        Assert.True(await db.Customers.AnyAsync(c => c.Id == customerId));
    }
}
