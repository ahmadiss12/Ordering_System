using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OrderingSystem.Application.Abstractions;
using OrderingSystem.Infrastructure.Persistence;

namespace OrderingSystem.Infrastructure.Orders;

/// <summary>
/// Allocation as one statement, so two simultaneous checkouts cannot receive the same number.
/// </summary>
internal sealed class SqlOrderNumberAllocator(AppDbContext db) : IOrderNumberAllocator
{
    public async Task<int> NextAsync(
        Guid restaurantId, DateOnly businessDate, CancellationToken ct = default)
    {
        // UPDATE ... OUTPUT reads and writes under one lock on one row. Reading the counter and
        // writing it back as two statements is the classic way to hand two customers the same
        // order number on a busy Friday.
        //
        // The insert runs first and does nothing when the row exists, so a day's first order
        // creates the sequence without a separate round trip deciding whether to.
        const string sql = """
            MERGE OrderNumberSequences WITH (HOLDLOCK) AS target
            USING (SELECT @restaurantId AS RestaurantId, @businessDate AS BusinessDate) AS source
                ON target.RestaurantId = source.RestaurantId
               AND target.BusinessDate = source.BusinessDate
            WHEN MATCHED THEN
                UPDATE SET NextValue = target.NextValue + 1
            WHEN NOT MATCHED THEN
                INSERT (RestaurantId, BusinessDate, NextValue) VALUES (source.RestaurantId, source.BusinessDate, 2)
            OUTPUT COALESCE(deleted.NextValue, 1) AS Allocated;
            """;

        // HOLDLOCK is what makes the MERGE safe: without it two connections can both fail to
        // match and both try to insert, and one of them dies on the primary key.
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        // Types spelled out rather than left to the driver. Inference happens to be right for
        // both of these today; naming them means the statement keeps working if that ever stops
        // being true, which is cheap insurance on the one query in the system that hands out a
        // number two customers must never share.
        command.Parameters.Add(new SqlParameter("@restaurantId", SqlDbType.UniqueIdentifier)
        {
            Value = restaurantId,
        });
        command.Parameters.Add(new SqlParameter("@businessDate", SqlDbType.Date)
        {
            Value = businessDate.ToDateTime(TimeOnly.MinValue),
        });

        var transaction = db.Database.CurrentTransaction;
        if (transaction is not null)
        {
            command.Transaction = transaction.GetDbTransaction();
        }

        await db.Database.OpenConnectionAsync(ct);

        try
        {
            var allocated = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(allocated, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            // Only closes when this call opened it; EF reference-counts while a transaction is open.
            await db.Database.CloseConnectionAsync();
        }
    }
}
