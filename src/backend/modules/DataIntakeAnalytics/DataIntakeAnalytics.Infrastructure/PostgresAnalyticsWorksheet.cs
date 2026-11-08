using System.Text.Json;
using DataIntakeAnalytics.Application;
using Npgsql;
using NpgsqlTypes;

namespace DataIntakeAnalytics.Infrastructure;

// A document-specific analytical worksheet; never writes ERP orders or staging rows.
public sealed class PostgresAnalyticsWorksheet(NpgsqlDataSource dataSource) : IAnalyticsWorksheetStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE TABLE IF NOT EXISTS data_intake.analytics_worksheet (
              id text PRIMARY KEY, source jsonb NOT NULL, source_sha256 text NOT NULL,
              source_pdf bytea NOT NULL, source_xlsx bytea NOT NULL,
              version bigint NOT NULL DEFAULT 1, inputs jsonb NOT NULL DEFAULT '{}', updated_at timestamptz);
            CREATE TABLE IF NOT EXISTS data_intake.analytics_worksheet_history (
              worksheet_id text NOT NULL REFERENCES data_intake.analytics_worksheet(id),
              version bigint NOT NULL, inputs jsonb NOT NULL, saved_at timestamptz NOT NULL,
              PRIMARY KEY(worksheet_id, version));
            """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<AnalyticsWorksheet?> GetAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT version,source::text,inputs::text,updated_at FROM data_intake.analytics_worksheet WHERE id='1532_261030162231_001'");
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<AnalyticsWorksheet?> SaveAsync(long version, WorksheetInputs inputs, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            WITH changed AS (
              UPDATE data_intake.analytics_worksheet SET inputs=@inputs, version=version+1, updated_at=now()
              WHERE id='1532_261030162231_001' AND version=@version
              RETURNING id,version,source,inputs,updated_at
            ), history AS (
              INSERT INTO data_intake.analytics_worksheet_history(worksheet_id,version,inputs,saved_at)
              SELECT id,version,inputs,updated_at FROM changed
            ) SELECT version,source::text,inputs::text,updated_at FROM changed;
            """);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.Add("inputs", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(inputs, Json);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<WorksheetAsset?> GetAssetAsync(bool excel, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(excel
            ? "SELECT source_xlsx FROM data_intake.analytics_worksheet WHERE id='1532_261030162231_001'"
            : "SELECT source_pdf FROM data_intake.analytics_worksheet WHERE id='1532_261030162231_001'");
        return await command.ExecuteScalarAsync(ct) is byte[] bytes
            ? new(bytes, excel ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "application/pdf", "1532_261030162231_001." + (excel ? "xlsx" : "pdf")) : null;
    }
    private static AnalyticsWorksheet Read(NpgsqlDataReader reader) => new(reader.GetInt64(0),
        JsonSerializer.Deserialize<JsonElement>(reader.GetString(1), Json),
        JsonSerializer.Deserialize<WorksheetInputs>(reader.GetString(2), Json)!,
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
}
