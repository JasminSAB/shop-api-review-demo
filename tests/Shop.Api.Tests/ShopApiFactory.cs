using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Api.Auth;
using Shop.Api.Data;

namespace Shop.Api.Tests;

/// <summary>
/// The API on a private SQLite database, seeded like production: in memory by default, or a database
/// file where every request opens its own connection, as it does in production.
/// </summary>
public sealed class ShopApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string? _databaseFile;

    /// <summary>Every SQL command sent to the database by the API.</summary>
    public Findings.SqlCapture Sql { get; } = new();

    public ShopApiFactory() => _connection.Open();

    private ShopApiFactory(string databaseFile) => _databaseFile = databaseFile;

    /// <summary>An API on a database file, where every request opens its own connection.</summary>
    public static ShopApiFactory WithDatabaseFile(string databaseFile) => new(databaseFile);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ShopDbContext>>();
            services.AddDbContext<ShopDbContext>(options => (_databaseFile is null ? options.UseSqlite(_connection) : options.UseSqlite($"Data Source={_databaseFile};Default Timeout=30")).AddInterceptors(Sql));
        });
    }

    public HttpClient ClientFor(int customerId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(CustomerAuthenticationHandler.HeaderName, customerId.ToString());
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public T WithDb<T>(Func<ShopDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(descriptor);
    }
}
