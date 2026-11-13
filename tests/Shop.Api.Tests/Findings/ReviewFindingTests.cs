using System.Net;
using System.Net.Http.Json;
using Shop.Api.Domain;
using Shop.Api.Orders;
using Shop.Api.Reports;

namespace Shop.Api.Tests.Findings;

/// <summary>
/// The answer key of the code review demo: one test per problem hidden in the API. Each test PASSES
/// while the problem exists, so it proves the problem is real, not a matter of opinion.
/// See FINDINGS.md for the explanation and the fix of each one.
/// </summary>
public sealed class ReviewFindingTests : IClassFixture<ShopApiFactory>
{
    private readonly ShopApiFactory _factory;

    public ReviewFindingTests(ShopApiFactory factory) => _factory = factory;

    private async Task<Order> PlaceOrderAsync(int customerId, int productId = 1, int quantity = 1)
    {
        var response = await _factory.ClientFor(customerId).PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(productId, quantity)], null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Order>())!;
    }

    [Fact(DisplayName = "1. SQL injection: the email search returns other customers' orders")]
    public async Task Email_search_is_open_to_sql_injection()
    {
        await PlaceOrderAsync(customerId: 1);
        await PlaceOrderAsync(customerId: 2);

        var orders = await _factory.ClientFor(2)
            .GetFromJsonAsync<List<Order>>("/api/orders/search?email=" + Uri.EscapeDataString("x' or '1'='1"));

        Assert.Contains(orders!, o => o.CustomerId == 1);
    }

    [Fact(DisplayName = "2. IDOR: a customer can read another customer's order")]
    public async Task A_customer_can_read_someone_elses_order()
    {
        var amrasOrder = await PlaceOrderAsync(customerId: 1);

        var response = await _factory.ClientFor(2).GetAsync($"/api/orders/{amrasOrder.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "3. Over-posting: a customer can mark an order as paid and change its total")]
    public async Task A_customer_can_mark_an_order_paid_and_change_the_total()
    {
        var order = await PlaceOrderAsync(customerId: 1);

        order.Status = OrderStatus.Paid;
        order.Total = 0.01;
        await _factory.ClientFor(1).PutAsJsonAsync($"/api/orders/{order.Id}", order);

        var stored = _factory.WithDb(db => db.Orders.Find(order.Id)!);
        Assert.Equal(OrderStatus.Paid, stored.Status);
        Assert.Equal(0.01, stored.Total);
    }

    [Fact(DisplayName = "4. Money in double: 7.50 plus 17% VAT is charged as 8.77, not 8.78")]
    public void Vat_on_a_double_price_loses_a_cent()
    {
        var total = OrderService.CalculateTotal([new OrderLine { UnitPrice = 7.50, Quantity = 1 }]);

        var correct = Math.Round(7.50m * 1.17m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(8.78m, correct);
        Assert.Equal(8.77, total);
    }

    [Fact(DisplayName = "5. Lost update: concurrent orders sell more monitors than are in stock")]
    public async Task Concurrent_orders_oversell_the_stock()
    {
        // A database file: each request gets its own connection, so the requests really run in parallel.
        var file = Path.Combine(Path.GetTempPath(), $"shop-race-{Guid.NewGuid():N}.db");
        await using var factory = ShopApiFactory.WithDatabaseFile(file);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            factory.WithDb(db => { db.Products.Find(3)!.Stock = 5; return db.SaveChanges(); });
            var soldBefore = factory.WithDb(db => db.OrderLines.Count(l => l.ProductId == 3));

            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
                factory.ClientFor(1).PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(3, 1)], null))));

            var sold = factory.WithDb(db => db.OrderLines.Count(l => l.ProductId == 3)) - soldBefore;
            if (sold > 5)
            {
                Assert.True(sold > 5, $"{sold} monitors sold from a stock of 5");
                return;
            }
        }

        Assert.Fail("The race did not show up in 5 attempts; run the test again.");
    }

    [Fact(DisplayName = "6. Error details: a failing request returns the stack trace to the client")]
    public async Task A_server_error_returns_the_stack_trace()
    {
        var response = await _factory.ClientFor(1).PostAsync("/api/orders", JsonContent.Create(new { lines = (object?)null }));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("at Shop.Api.Orders.OrderService", body);
    }

    [Fact(DisplayName = "7. Client-side filtering: the revenue report loads every order of all time")]
    public async Task Revenue_report_reads_the_whole_orders_table()
    {
        while (_factory.Sql.Commands.TryDequeue(out _)) { }

        await _factory.ClientFor(1).GetFromJsonAsync<RevenueSummary>("/api/reports/revenue?from=2026-03-01&to=2026-03-31");

        var ordersQuery = _factory.Sql.Commands.Single(sql => sql.Contains("FROM \"Orders\""));
        Assert.DoesNotContain("WHERE", ordersQuery);
    }

    [Fact(DisplayName = "8. Date range: an order paid at 23:59:59.5 is missing from that day's revenue")]
    public async Task Revenue_report_drops_orders_in_the_last_second_of_the_day()
    {
        var day = new DateOnly(2026, 4, 30);
        _factory.WithDb(db =>
        {
            db.Orders.Add(new Order { CustomerId = 1, Status = OrderStatus.Paid, Total = 40, CreatedAt = day.ToDateTime(new TimeOnly(23, 59, 59, 500)) });
            return db.SaveChanges();
        });

        var report = await _factory.ClientFor(1).GetFromJsonAsync<RevenueSummary>("/api/reports/revenue?from=2026-04-30&to=2026-04-30");

        Assert.Equal(0, report!.Orders);
    }
}
