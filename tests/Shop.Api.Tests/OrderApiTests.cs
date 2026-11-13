using System.Net;
using System.Net.Http.Json;
using Shop.Api.Domain;
using Shop.Api.Orders;
using Shop.Api.Reports;

namespace Shop.Api.Tests;

public sealed class OrderApiTests : IClassFixture<ShopApiFactory>
{
    private readonly ShopApiFactory _factory;

    public OrderApiTests(ShopApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var response = await _factory.CreateClient().GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Products_are_listed()
    {
        var products = await _factory.ClientFor(1).GetFromJsonAsync<List<Product>>("/api/products");

        Assert.Equal(3, products!.Count);
    }

    [Fact]
    public async Task Placing_an_order_reserves_stock_and_adds_vat()
    {
        var client = _factory.ClientFor(1);
        var stockBefore = _factory.WithDb(db => db.Products.Find(2)!.Stock);

        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(2, 2)], "Gift wrap"));
        var order = await response.Content.ReadFromJsonAsync<Order>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(58.38, order!.Total); // 2 x 24.95 = 49.90, plus 17% VAT
        Assert.Equal(stockBefore - 2, _factory.WithDb(db => db.Products.Find(2)!.Stock));
    }

    [Fact]
    public async Task An_order_larger_than_the_stock_is_rejected()
    {
        var response = await _factory.ClientFor(1).PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(3, 500)], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_can_read_their_order()
    {
        var client = _factory.ClientFor(1);
        var created = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(1, 1)], null)))
            .Content.ReadFromJsonAsync<Order>();

        var order = await client.GetFromJsonAsync<Order>($"/api/orders/{created!.Id}");

        Assert.Equal(created.Id, order!.Id);
        Assert.Single(order.Lines);
    }

    [Fact]
    public async Task Orders_can_be_found_by_customer_email()
    {
        var client = _factory.ClientFor(2);
        await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(2, 1)], null));

        var orders = await client.GetFromJsonAsync<List<Order>>("/api/orders/search?email=dino@example.com");

        Assert.NotEmpty(orders!);
        Assert.All(orders!, o => Assert.Equal(2, o.CustomerId));
    }

    [Fact]
    public async Task The_note_of_an_order_can_be_changed()
    {
        var client = _factory.ClientFor(1);
        var created = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest([new(1, 1)], "Leave at the door")))
            .Content.ReadFromJsonAsync<Order>();

        created!.Note = "Call before delivery";
        var updated = await (await client.PutAsJsonAsync($"/api/orders/{created.Id}", created)).Content.ReadFromJsonAsync<Order>();

        Assert.Equal("Call before delivery", updated!.Note);
    }

    [Fact]
    public async Task Revenue_report_counts_paid_orders_of_the_day()
    {
        var day = new DateOnly(2026, 3, 14);
        _factory.WithDb(db =>
        {
            db.Orders.Add(new Order { CustomerId = 1, CreatedAt = day.ToDateTime(new TimeOnly(10, 30)), Status = OrderStatus.Paid, Total = 100 });
            db.Orders.Add(new Order { CustomerId = 1, CreatedAt = day.ToDateTime(new TimeOnly(11, 0)), Status = OrderStatus.Pending, Total = 50 });
            return db.SaveChanges();
        });

        var report = await _factory.ClientFor(1).GetFromJsonAsync<RevenueSummary>("/api/reports/revenue?from=2026-03-14&to=2026-03-14");

        Assert.Equal(1, report!.Orders);
        Assert.Equal(100, report.Revenue);
    }
}
