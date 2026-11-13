using Microsoft.EntityFrameworkCore;
using Shop.Api.Data;
using Shop.Api.Domain;

namespace Shop.Api.Orders;

public record OrderLineRequest(int ProductId, int Quantity);

public record CreateOrderRequest(List<OrderLineRequest> Lines, string? Note);

public class OutOfStockException(string productName) : Exception($"'{productName}' is out of stock.");

/// <summary>
/// Places orders: validates the lines, reserves stock and calculates the total including VAT.
/// </summary>
public class OrderService(ShopDbContext db, TimeProvider clock)
{
    /// <summary>VAT rate applied to every order (17%).</summary>
    public const double VatRate = 0.17;

    public async Task<Order> PlaceOrderAsync(int customerId, CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(request));
        }

        var order = new Order
        {
            CustomerId = customerId,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Note = request.Note,
        };

        foreach (var line in request.Lines)
        {
            var product = await db.Products.FindAsync([line.ProductId], cancellationToken)
                ?? throw new ArgumentException($"Product {line.ProductId} does not exist.", nameof(request));

            if (product.Stock < line.Quantity)
            {
                throw new OutOfStockException(product.Name);
            }

            product.Stock -= line.Quantity;
            order.Lines.Add(new OrderLine { ProductId = product.Id, Quantity = line.Quantity, UnitPrice = product.Price });
        }

        order.Total = CalculateTotal(order.Lines);

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>Sum of the lines plus VAT, rounded to cents.</summary>
    public static double CalculateTotal(IEnumerable<OrderLine> lines)
    {
        var net = lines.Sum(l => l.UnitPrice * l.Quantity);
        return Math.Round(net * (1 + VatRate), 2, MidpointRounding.AwayFromZero);
    }

    public Task<Order?> FindAsync(int id, CancellationToken cancellationToken = default) =>
        db.Orders.Include(o => o.Lines).AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<List<Order>> SearchByCustomerEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var sql = "SELECT o.* FROM Orders o JOIN Customers c ON c.Id = o.CustomerId " +
                  $"WHERE c.Email = '{email.Trim().ToLowerInvariant()}'";

        return db.Orders.FromSqlRaw(sql).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<Order?> UpdateAsync(int id, Order changes, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.FindAsync([id], cancellationToken);
        if (order is null) return null;

        db.Entry(order).CurrentValues.SetValues(changes);
        order.Id = id;
        await db.SaveChangesAsync(cancellationToken);
        return order;
    }
}
