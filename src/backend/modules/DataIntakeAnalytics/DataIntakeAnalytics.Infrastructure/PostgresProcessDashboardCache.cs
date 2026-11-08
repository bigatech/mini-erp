using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataIntakeAnalytics.Application;
using Npgsql;
using NpgsqlTypes;

namespace DataIntakeAnalytics.Infrastructure;

public static class DashboardSourceVersion
{
    public static string Create(IReadOnlyList<StagedFile> files, DateOnly asOf)
    {
        // Staging rows are immutable. Include publication-sensitive file names,
        // status, import identity and the local day (future report eligibility).
        var manifest = "analytics-v2|" + asOf.ToString("yyyy-MM-dd") + "|" + string.Join('|', files.OrderBy(f => f.Id)
            .Select(f => $"{f.Id:N}:{f.FileName}:{f.Status}:{f.UploadedAt.UtcTicks}:{f.FileSize}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }

    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Istanbul").DateTime);
}

/// <summary>Rebuildable process read model; raw sources and staging are never modified.</summary>
public sealed class PostgresProcessDashboardCache(NpgsqlDataSource dataSource, IStagingRepository repository) : IProcessDashboardCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS data_intake.process_snapshots (
              snapshot_key text PRIMARY KEY, source_version text NOT NULL,
              payload jsonb NOT NULL, generated_at timestamptz NOT NULL);
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProcessDashboard> GetOrCreateAsync(IReadOnlyList<StagedFile> manifest, string process, DateOnly? from, DateOnly? to, string? material, Func<Task<ProcessDashboard>> calculate, CancellationToken ct)
    {
        // Preserve filter semantics exactly: no case folding or date normalization.
        var filter = JsonSerializer.Serialize(new { process, from, to, material }, Json);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(filter)));
        var version = DashboardSourceVersion.Create(manifest, DashboardSourceVersion.Today);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (await ReadAsync(connection, null, key, version, ct) is { } cached) return cached;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(783492012,hashtext(@key))", connection, transaction))
        {
            gate.Parameters.AddWithValue("key", key);
            await gate.ExecuteNonQueryAsync(ct);
        }
        if (await ReadAsync(connection, transaction, key, version, ct) is { } ready)
        {
            await transaction.CommitAsync(ct);
            return ready;
        }
        var result = await calculate();
        if (DashboardSourceVersion.Create(await repository.ListAsync(ct), DashboardSourceVersion.Today) == version)
        {
            await using var save = new NpgsqlCommand("""
                INSERT INTO data_intake.process_snapshots(snapshot_key,source_version,payload,generated_at)
                VALUES (@key,@version,@payload,now())
                ON CONFLICT(snapshot_key) DO UPDATE SET source_version=excluded.source_version,
                  payload=excluded.payload,generated_at=excluded.generated_at;
                DELETE FROM data_intake.process_snapshots WHERE snapshot_key IN (
                  SELECT snapshot_key FROM data_intake.process_snapshots ORDER BY generated_at DESC OFFSET 256);
                """, connection, transaction);
            save.Parameters.AddWithValue("key", key);
            save.Parameters.AddWithValue("version", version);
            save.Parameters.Add("payload", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(result, Json);
            await save.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return result;
    }

    private static async Task<ProcessDashboard?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string key, string version, CancellationToken ct)
    {
        await using var read = new NpgsqlCommand("SELECT payload::text FROM data_intake.process_snapshots WHERE snapshot_key=@key AND source_version=@version", connection, transaction);
        read.Parameters.AddWithValue("key", key);
        read.Parameters.AddWithValue("version", version);
        var json = await read.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<ProcessDashboard>(json, Json);
    }
}
