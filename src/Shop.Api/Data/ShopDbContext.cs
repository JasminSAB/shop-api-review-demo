using Microsoft.EntityFrameworkCore;
using Shop.Api.Domain;

namespace Shop.Api.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasIndex(c => c.Email).IsUnique();
        modelBuilder.Entity<Order>().HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        modelBuilder.Entity<Order>().HasIndex(o => o.CreatedAt);
    }
}

public static class SeedData
{
    public static void Seed(ShopDbContext db)
    {
        if (db.Products.Any()) return;

        db.Customers.AddRange(
            new Customer { Id = 1, Name = "Amra Hodzic", Email = "amra@example.com" },
            new Customer { Id = 2, Name = "Dino Kovac", Email = "dino@example.com" });

        db.Products.AddRange(
            new Product { Id = 1, Name = "Mechanical keyboard", Price = 89.90, Stock = 25 },
            new Product { Id = 2, Name = "Wireless mouse", Price = 24.95, Stock = 40 },
            new Product { Id = 3, Name = "27\" monitor", Price = 249.00, Stock = 5 });

        db.SaveChanges();
    }
}
