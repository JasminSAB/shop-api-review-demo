using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Shop.Api.Auth;
using Shop.Api.Data;
using Shop.Api.Domain;
using Shop.Api.Orders;
using Shop.Api.Reports;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShopDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Shop") ?? "Data Source=shop.db"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<RevenueReport>();

builder.Services
    .AddAuthentication(CustomerAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, CustomerAuthenticationHandler>(CustomerAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    db.Database.EnsureCreated();
    SeedData.Seed(db);
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error switch
    {
        ArgumentException or OutOfStockException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
    await context.Response.WriteAsJsonAsync(new { error = error?.ToString() });
}));

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();

api.MapGet("/products", async (ShopDbContext db, CancellationToken ct) =>
    await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct));

api.MapPost("/orders", async (CreateOrderRequest request, OrderService orders, HttpContext http, CancellationToken ct) =>
{
    var order = await orders.PlaceOrderAsync(http.User.CustomerId(), request, ct);
    return Results.Created($"/api/orders/{order.Id}", order);
});

api.MapGet("/orders/{id:int}", async (int id, OrderService orders, CancellationToken ct) =>
    await orders.FindAsync(id, ct) is { } order ? Results.Ok(order) : Results.NotFound());

api.MapPut("/orders/{id:int}", async (int id, Order changes, OrderService orders, CancellationToken ct) =>
    await orders.UpdateAsync(id, changes, ct) is { } order ? Results.Ok(order) : Results.NotFound());

api.MapGet("/orders/search", async (string email, OrderService orders, CancellationToken ct) =>
    await orders.SearchByCustomerEmailAsync(email, ct));

api.MapGet("/reports/revenue", async (DateOnly from, DateOnly to, RevenueReport report, CancellationToken ct) =>
    await report.ForDaysAsync(from, to, ct));

app.Run();

public partial class Program;
