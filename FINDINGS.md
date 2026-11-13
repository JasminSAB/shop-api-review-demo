# Answer key

This API compiles without warnings and its original tests pass. It still hides eight problems that a
reviewer should catch. This file and the tests in `tests/Shop.Api.Tests/Findings` were added **after**
the automated review in [pull request #3](https://github.com/JasminSAB/shop-api-review-demo/pull/3),
so the review saw only the code, never the answers.

Each test below **passes while the problem exists**: it proves the problem is real.

```
dotnet test --filter "FullyQualifiedName~Findings"
```

| # | Problem | Where | Proof | Found by the review |
|---|---|---|---|---|
| 1 | SQL injection: the email is concatenated into `FromSqlRaw` | `OrderService.SearchByCustomerEmailAsync` | `x' or '1'='1` returns other customers' orders | Yes, critical |
| 2 | IDOR: any customer can read any order | `GET /api/orders/{id}` | customer 2 reads customer 1's order | Yes, high |
| 3 | Over-posting: the PUT body is the `Order` entity | `PUT /api/orders/{id}` | a customer sets `Status = Paid` and `Total = 0.01` | Yes, critical |
| 4 | Money in `double` | `OrderService.CalculateTotal` | 7.50 plus 17% VAT is 8.77 instead of 8.78 | Yes, high |
| 5 | Lost update on stock: check, then write, no concurrency token | `OrderService.PlaceOrderAsync` | 20 parallel orders sell more than the 5 monitors in stock | Yes, high |
| 6 | Exception details returned to the client | `Program.cs` exception handler | a failing request returns the stack trace | Yes, high |
| 7 | The revenue report loads every order and filters in memory | `RevenueReport.ForDaysAsync` | the SQL sent to the database has no `WHERE` | Yes, medium |
| 8 | Inclusive end bound `<= 23:59:59` | `RevenueReport.ForDaysAsync` | an order paid at 23:59:59.5 is missing from the day | Yes, medium |

The review also reported nine findings that were not planted, for example that order quantities can
be zero or negative. See the pull request for all 17.

## How the fixes look

1. Use LINQ (`Where(o => o.Customer!.Email == email)`) or `FromSql($"...")`, which turns interpolation into parameters.
2. Filter by the caller: `o.CustomerId == http.User.CustomerId()`, and return 404 for other customers' orders.
3. Bind a request DTO with only the fields a customer may change (`Note`), never the entity.
4. Keep money in `decimal` (`Price`, `UnitPrice`, `Total`, the VAT rate).
5. Add a concurrency token (`rowversion`) to `Product` and retry, or decrement atomically with `ExecuteUpdate(... Where Stock >= quantity)`.
6. Return `ProblemDetails` without exception details outside Development.
7. Filter and aggregate in SQL: `Where(...)` and `SumAsync(...)` before materializing.
8. Use a half-open range: `CreatedAt >= start && CreatedAt < to.AddDays(1)`.
