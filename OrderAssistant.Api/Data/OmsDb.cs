using Microsoft.EntityFrameworkCore;
using OrderAssistant.Api.Entities;

namespace OrderAssistant.Api.Data;

public class OmsDb(DbContextOptions<OmsDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Inventory> Inventory => Set<Inventory>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>().HasIndex(c => c.Email).IsUnique();

        b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        b.Entity<Product>().Property(p => p.UnitPrice).HasPrecision(12, 2);

        b.Entity<Warehouse>().HasIndex(w => w.Code).IsUnique();

        // one stock row per product per warehouse
        b.Entity<Inventory>().HasIndex(i => new { i.ProductId, i.WarehouseId }).IsUnique();

        b.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        b.Entity<Order>().HasIndex(o => o.CreatedAtUtc);
        b.Entity<Order>().Property(o => o.Status).HasConversion<string>();

        b.Entity<OrderLine>().Property(l => l.UnitPrice).HasPrecision(12, 2);
    }
}