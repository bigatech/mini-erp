using Npgsql;
using ProductLifecycle.Application;
namespace ProductLifecycle.Infrastructure;
public sealed class PostgresRevisionBomOptionsReader(NpgsqlDataSource source):IRevisionBomOptionsReader
{
    public async Task<IReadOnlyList<ModelOptionDto>> ReadAsync(Guid revisionId,CancellationToken ct)
    {
        await using var c=source.CreateCommand("SELECT o.option_id,o.model_id,o.kind,o.code,o.name,o.quantity FROM product_lifecycle.model_configuration_option o JOIN product_lifecycle.model_revizyon r ON r.model_id=o.model_id WHERE r.model_revizyon_id=@revision");c.Parameters.AddWithValue("revision",revisionId);await using var reader=await c.ExecuteReaderAsync(ct);var rows=new List<ModelOptionDto>();while(await reader.ReadAsync(ct))rows.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetDecimal(5)));return rows;
    }
}
