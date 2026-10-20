using System.Data;
using Microsoft.EntityFrameworkCore;

namespace SessionDesk.Api.Data;

public sealed record StatusCount(string Status, int Count);

public sealed record DayActionCount(DateTime Day, string Action, int Count);

public static class BillSummaryQuery
{
    public static async Task<(List<StatusCount> Statuses, List<DayActionCount> Days)> LoadAsync(
        AppDbContext db,
        Guid committeeId,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var statuses = new List<StatusCount>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT status, COUNT(*)::int AS count
                FROM bills
                WHERE committee_id = @committeeId
                GROUP BY status
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "committeeId";
            parameter.Value = committeeId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(new StatusCount(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        var days = new List<DayActionCount>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT date_trunc('day', ba.created_at AT TIME ZONE 'UTC') AS day,
                       ba.action,
                       COUNT(*)::int AS count
                FROM bill_actions ba
                JOIN bills b ON b.id = ba.bill_id
                WHERE b.committee_id = @committeeId
                  AND ba.created_at >= now() - interval '7 days'
                GROUP BY 1, 2
                ORDER BY 1
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "committeeId";
            parameter.Value = committeeId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                days.Add(new DayActionCount(reader.GetDateTime(0), reader.GetString(1), reader.GetInt32(2)));
            }
        }

        return (statuses, days);
    }
}
