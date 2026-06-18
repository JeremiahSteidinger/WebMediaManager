using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WebMediaManager.Data;

/// <summary>
/// Applies SQLite PRAGMAs on every connection open. WAL lets the background worker write while
/// the UI reads; busy_timeout rides out brief lock contention instead of throwing SQLITE_BUSY;
/// foreign_keys enforces FK constraints (off by default in SQLite).
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
