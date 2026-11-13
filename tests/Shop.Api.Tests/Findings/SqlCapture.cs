using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shop.Api.Tests.Findings;

/// <summary>Records every SQL command EF Core sends, so a test can check what reached the database.</summary>
public sealed class SqlCapture : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
