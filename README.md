# Shop API

A small online shop backend in ASP.NET Core 10 and EF Core (SQLite): products, orders with VAT,
order search and a revenue report.

## Run

```
dotnet run --project src/Shop.Api
```

The database (`shop.db`) is created and seeded on first start. Requests are authenticated by the API
gateway in front of the service, which forwards the customer id in the `X-Customer-Id` header:

```
curl -H "X-Customer-Id: 1" http://localhost:5000/api/products
```

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/products` | List products |
| POST | `/api/orders` | Place an order, reserve stock, add 17% VAT |
| GET | `/api/orders/{id}` | Read an order |
| PUT | `/api/orders/{id}` | Update an order |
| GET | `/api/orders/search?email=` | Orders of a customer |
| GET | `/api/reports/revenue?from=&to=` | Revenue of paid and shipped orders, both days included |

## Test

```
dotnet test
```
