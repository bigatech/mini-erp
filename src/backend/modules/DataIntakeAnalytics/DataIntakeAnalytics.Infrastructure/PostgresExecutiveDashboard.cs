using System.Text.Json;
using DataIntakeAnalytics.Application;
using Npgsql;
using NpgsqlTypes;

namespace DataIntakeAnalytics.Infrastructure;

public sealed record ExecutiveSnapshot(DateTimeOffset GeneratedAt, string SourceVersion, IReadOnlyList<ProcessDashboard> Data);

/// <summary>Derived, rebuildable read model. Never writes canonical business or staging records.</summary>
public sealed class PostgresExecutiveDashboard(NpgsqlDataSource dataSource, IStagingRepository repository, IProcessDashboardCache processCache)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS data_intake.executive_snapshots (
              snapshot_key text PRIMARY KEY, source_version text NOT NULL,
              payload jsonb NOT NULL, generated_at timestamptz NOT NULL);
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ExecutiveSnapshot> GetAsync(CancellationToken ct)
    {
        var listed = await repository.ListAsync(ct);
        var version = Version(listed);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (await ReadAsync(connection, null, version, ct) is { } cached) return cached;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Cross-host single flight, released automatically on cancellation/rollback.
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(783492011)", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        listed = await repository.ListAsync(ct);
        version = Version(listed);
        if (await ReadAsync(connection, transaction, version, ct) is { } ready)
        {
            await transaction.CommitAsync(ct);
            return ready;
        }
        IReadOnlyList<StagedFile>? files = null;
        var dashboards = new List<ProcessDashboard>();
        foreach (var process in ProcessAnalytics.Processes)
        {
            ct.ThrowIfCancellationRequested();
            // Share the full process snapshot so opening a process after the
            // overview never repeats the same calculation for this source/day.
            // Reuse one source read for all cache misses in this overview request.
            var dashboard = await processCache.GetOrCreateAsync(listed, process, null, null, null, async () =>
            {
                files ??= await repository.LoadCompletedAsync(listed, ct);
                ct.ThrowIfCancellationRequested();
                return ProcessAnalytics.Calculate(files, process);
            }, ct);
            dashboards.Add(dashboard with { Rows = [], Breakdown = [], Daily = [] });
        }
        if (Version(await repository.ListAsync(ct)) != version)
            throw new ExecutiveSourcesChangedException();
        var snapshot = new ExecutiveSnapshot(DateTimeOffset.UtcNow, version, dashboards);
        await using (var save = new NpgsqlCommand("""
            INSERT INTO data_intake.executive_snapshots(snapshot_key,source_version,payload,generated_at)
            VALUES ('overview',@version,@payload,@generated)
            ON CONFLICT(snapshot_key) DO UPDATE SET source_version=excluded.source_version,
              payload=excluded.payload,generated_at=excluded.generated_at;
            """, connection, transaction))
        {
            save.Parameters.AddWithValue("version", version);
            save.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(snapshot, Json);
            save.Parameters.AddWithValue("generated", snapshot.GeneratedAt);
            await save.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return snapshot;
    }

    private static async Task<ExecutiveSnapshot?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string version, CancellationToken ct)
    {
        await using var read = new NpgsqlCommand("SELECT payload::text FROM data_intake.executive_snapshots WHERE snapshot_key='overview' AND source_version=@version", connection, transaction);
        read.Parameters.AddWithValue("version", version);
        var json = await read.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<ExecutiveSnapshot>(json, Json);
    }

    private static string Version(IReadOnlyList<StagedFile> files)
    {
        // Staging rows are immutable. Status changes and new parser imports change the manifest.
        // Istanbul day invalidates future-dated report eligibility even without a new import.
        return DashboardSourceVersion.Create(files, DashboardSourceVersion.Today);
    }
}

public sealed class ExecutiveSourcesChangedException : Exception { }
