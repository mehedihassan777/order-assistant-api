using Bogus;
using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Entities;

namespace OrderAssistant.Api.Data;

public static class Seeder
{
    public static async Task SeedAsync(OmsDb db)
    {
        if (await db.Customers.AnyAsync()) return; // already seeded

        Randomizer.Seed = new Random(42); // same data every run
        var f = new Faker();

        var customers = new Faker<Customer>()
            .RuleFor(c => c.Name, x => x.Name.FullName())
            .RuleFor(c => c.Email, x => $"customer{x.IndexFaker + 1}@example.test")
            .Generate(200);

        var products = new Faker<Product>()
            .RuleFor(p => p.Sku, x => $"SKU-{1001 + x.IndexFaker}")
            .RuleFor(p => p.Name, x => x.Commerce.ProductName())
            .RuleFor(p => p.UnitPrice, x => Math.Round(x.Random.Decimal(2, 500), 2))
            .Generate(100);

        var warehouses = new List<Warehouse>
        {
            new() { Code = "WH-N", Name = "North" },
            new() { Code = "WH-S", Name = "South" },
            new() { Code = "WH-E", Name = "East" },
        };

        var inventory = (from p in products
                         from w in warehouses
                         select new Inventory { Product = p, Warehouse = w, QuantityOnHand = f.Random.Int(0, 500) })
                        .ToList();

        var orders = new Faker<Order>()
            .RuleFor(o => o.OrderNumber, x => $"ORD-{100001 + x.IndexFaker}")
            .RuleFor(o => o.Customer, x => x.PickRandom(customers))
            .RuleFor(o => o.Status, x => x.PickRandom<OrderStatus>())
            .RuleFor(o => o.CreatedAtUtc, x => x.Date.Past(1).ToUniversalTime())
            .RuleFor(o => o.Lines, x => x.PickRandom(products, x.Random.Int(1, 5))
                .Select(p => new OrderLine { Product = p, Quantity = x.Random.Int(1, 10), UnitPrice = p.UnitPrice })
                .ToList())
            .Generate(1000);

        db.AddRange(customers);
        db.AddRange(products);
        db.AddRange(warehouses);
        db.AddRange(inventory);
        db.AddRange(orders);
        await db.SaveChangesAsync();
    }
}