using System.Text.Json;
using System.Security.Cryptography;
using DataIntakeAnalytics.Application;
using Npgsql;
using NpgsqlTypes;

namespace DataIntakeAnalytics.Infrastructure;

public sealed class PostgresStagingRepository(NpgsqlDataSource dataSource) : IStagingRepository
{
    public async Task<byte[]?> GetContentAsync(Guid id, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT file_content FROM data_intake.staging_imports WHERE id=@id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(ct) as byte[];
    }
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            CREATE SCHEMA IF NOT EXISTS data_intake;
            CREATE TABLE IF NOT EXISTS data_intake.staging_imports (
              id uuid PRIMARY KEY, file_name text NOT NULL, media_type text NOT NULL DEFAULT 'application/octet-stream',
              file_size bigint NOT NULL DEFAULT 0, file_content bytea, columns jsonb NOT NULL,
              uploaded_at timestamptz NOT NULL DEFAULT now(), status text NOT NULL DEFAULT 'Completed');
            ALTER TABLE data_intake.staging_imports ADD COLUMN IF NOT EXISTS media_type text NOT NULL DEFAULT 'application/octet-stream';
            ALTER TABLE data_intake.staging_imports ADD COLUMN IF NOT EXISTS file_size bigint NOT NULL DEFAULT 0;
            ALTER TABLE data_intake.staging_imports ADD COLUMN IF NOT EXISTS file_content bytea;
            ALTER TABLE data_intake.staging_imports ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'Completed';
            ALTER TABLE data_intake.staging_imports ADD COLUMN IF NOT EXISTS source_file_hash text;
            UPDATE data_intake.staging_imports SET source_file_hash=encode(sha256(file_content), 'hex') WHERE source_file_hash IS NULL AND file_content IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_staging_hash ON data_intake.staging_imports(source_file_hash);
            CREATE TABLE IF NOT EXISTS data_intake.staging_rows (
              import_id uuid NOT NULL REFERENCES data_intake.staging_imports(id) ON DELETE CASCADE,
              row_number integer NOT NULL, values jsonb NOT NULL, PRIMARY KEY(import_id, row_number));
            """);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid> SaveAsync(string fileName, string mediaType, string status, byte[] fileContent, string[] columns, IReadOnlyList<string[]> rows, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(fileContent));
        await using (var dedup = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@hash)); SELECT id,columns,(SELECT count(*) FROM data_intake.staging_rows r WHERE r.import_id=i.id) FROM data_intake.staging_imports i WHERE source_file_hash=@hash ORDER BY uploaded_at DESC LIMIT 1", connection, tx))
        {
            dedup.Parameters.AddWithValue("hash", hash);
            await using var check = await dedup.ExecuteReaderAsync(ct);
            await check.NextResultAsync(ct);
            if (await check.ReadAsync(ct))
            {
                var previousId = check.GetGuid(0);
                // A parser upgrade can expose previously omitted sheets/rows. Preserve
                // the old staging as Superseded and import the original bytes again.
                bool legacy = check.GetInt64(2) != rows.Count || !columns.SequenceEqual(JsonSerializer.Deserialize<string[]>(check.GetString(1)) ?? []);
                if (!legacy) throw new InvalidDataException("Bu dosya içeriği daha önce staging alanına alındı. Çift import engellendi.");
                await check.CloseAsync();
                await using var supersede = new NpgsqlCommand("UPDATE data_intake.staging_imports SET status='Superseded' WHERE source_file_hash=@hash", connection, tx);
                supersede.Parameters.AddWithValue("hash",hash); await supersede.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var command = new NpgsqlCommand("INSERT INTO data_intake.staging_imports(id,file_name,media_type,file_size,file_content,columns,status,source_file_hash) VALUES (@id,@name,@media,@size,@content,@columns,@status,@hash)", connection, tx))
        {
            command.Parameters.AddWithValue("hash", hash); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", fileName); command.Parameters.AddWithValue("media", mediaType);
            command.Parameters.AddWithValue("size", fileContent.LongLength); command.Parameters.AddWithValue("status", status); command.Parameters.Add("content", NpgsqlDbType.Bytea).Value = fileContent;
            command.Parameters.AddWithValue("columns", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(columns)); await command.ExecuteNonQueryAsync(ct);
        }
        for (var i = 0; i < rows.Count; i++)
        {
            await using var command = new NpgsqlCommand("INSERT INTO data_intake.staging_rows(import_id,row_number,values) VALUES (@id,@row,@values)", connection, tx);
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("row", i + 1); command.Parameters.AddWithValue("values", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(rows[i])); await command.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct); return id;
    }

    public async Task<IReadOnlyList<StagedFile>> ListAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT id,file_name,columns,uploaded_at,status,media_type,file_size FROM data_intake.staging_imports ORDER BY uploaded_at DESC");
        await using var reader = await command.ExecuteReaderAsync(ct); var files = new List<StagedFile>();
        while (await reader.ReadAsync(ct)) files.Add(new(reader.GetGuid(0), reader.GetString(1), JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [], [], reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6)));
        return files;
    }

    public async Task<IReadOnlyList<StagedFile>> LoadCompletedAsync(IReadOnlyList<StagedFile> listed, CancellationToken ct)
    {
        var completed = listed.Where(f => f.Status == "Completed").ToArray();
        if (completed.Length == 0) return [];
        var rows = completed.ToDictionary(f => f.Id, _ => new List<string[]>());
        // One ordered row query replaces two round trips per source file. Original
        // file metadata, source order and every staging row are retained.
        await using var command = dataSource.CreateCommand("SELECT import_id,values FROM data_intake.staging_rows WHERE import_id=ANY(@ids) ORDER BY import_id,row_number");
        command.Parameters.AddWithValue("ids", completed.Select(f => f.Id).ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows[reader.GetGuid(0)].Add(JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? []);
        return completed.Select(f => f with { Rows = rows[f.Id] }).ToArray();
    }

    public async Task<StagedFile?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT id,file_name,columns,uploaded_at,status,media_type,file_size FROM data_intake.staging_imports WHERE id=@id", connection); command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null;
        var fileName = reader.GetString(1); var columns = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? []; var uploadedAt = reader.GetFieldValue<DateTimeOffset>(3); var status = reader.GetString(4); var mediaType = reader.GetString(5); var fileSize = reader.GetInt64(6); await reader.CloseAsync();
        await using var rowsCommand = new NpgsqlCommand("SELECT values FROM data_intake.staging_rows WHERE import_id=@id ORDER BY row_number", connection); rowsCommand.Parameters.AddWithValue("id", id);
        await using var rowsReader = await rowsCommand.ExecuteReaderAsync(ct); var rows = new List<string[]>(); while (await rowsReader.ReadAsync(ct)) rows.Add(JsonSerializer.Deserialize<string[]>(rowsReader.GetString(0)) ?? []);
        return new(id, fileName, columns, rows, uploadedAt, status, mediaType, fileSize);
    }
}
