using Microsoft.EntityFrameworkCore;
using Shop.Api.Data;
using Shop.Api.Domain;

namespace Shop.Api.Reports;

public record RevenueSummary(DateOnly From, DateOnly To, int Orders, double Revenue);

/// <summary>Revenue of paid and shipped orders for a range of days, both days included.</summary>
public class RevenueReport(ShopDbContext db)
{
    public async Task<RevenueSummary> ForDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(new TimeOnly(23, 59, 59));

        var orders = await db.Orders.AsNoTracking().ToListAsync(cancellationToken);

        var inRange = orders
            .Where(o => o.Status is OrderStatus.Paid or OrderStatus.Shipped)
            .Where(o => o.CreatedAt >= start && o.CreatedAt <= end)
            .ToList();

        return new RevenueSummary(from, to, inRange.Count, inRange.Sum(o => o.Total));
    }
}
