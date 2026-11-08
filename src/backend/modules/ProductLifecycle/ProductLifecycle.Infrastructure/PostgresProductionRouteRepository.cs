using Npgsql;
using ProductLifecycle.Application;
namespace ProductLifecycle.Infrastructure;

public sealed class PostgresProductionRouteRepository(NpgsqlDataSource source) : IProductionRouteRepository
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS product_lifecycle.operasyon_tanimi(
              operasyon_id uuid PRIMARY KEY,operasyon_kodu text NOT NULL UNIQUE,operasyon_adi text NOT NULL,
              standart_sure numeric NOT NULL CHECK(standart_sure>=0),sure_birimi text NOT NULL CHECK(sure_birimi IN ('minute','second')));
            CREATE TABLE IF NOT EXISTS product_lifecycle.operasyon_rotasi(
              rota_id uuid PRIMARY KEY,model_revizyon_id uuid NOT NULL REFERENCES product_lifecycle.model_revizyon(model_revizyon_id),
              operasyon_id uuid NOT NULL REFERENCES product_lifecycle.operasyon_tanimi(operasyon_id),sira_no integer NOT NULL CHECK(sira_no>0),
              UNIQUE(model_revizyon_id,sira_no),UNIQUE(model_revizyon_id,operasyon_id));
            """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<OperationDto>> OperationsAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand("SELECT operasyon_id,operasyon_kodu,operasyon_adi,standart_sure,sure_birimi FROM product_lifecycle.operasyon_tanimi ORDER BY operasyon_kodu");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<OperationDto>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),reader.GetString(4)));
        return rows;
    }
    public async Task<OperationDto> CreateOperationAsync(string code,string name,decimal duration,string unit,CancellationToken ct)
    {
        var id=Guid.NewGuid();
        await using var command=source.CreateCommand("INSERT INTO product_lifecycle.operasyon_tanimi(operasyon_id,operasyon_kodu,operasyon_adi,standart_sure,sure_birimi) VALUES(@id,@code,@name,@duration,@unit)");
        command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("code",code);command.Parameters.AddWithValue("name",name);command.Parameters.AddWithValue("duration",duration);command.Parameters.AddWithValue("unit",unit);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation) { throw new DuplicateRouteEntryException(); }
        return new(id,code,name,duration,unit);
    }
    public async Task<IReadOnlyList<ProductionRouteDto>> RouteAsync(Guid revisionId,CancellationToken ct)
    {
        await using(var check=source.CreateCommand("SELECT EXISTS(SELECT 1 FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@id)"))
        {
            check.Parameters.AddWithValue("id",revisionId);
            if(!(bool)(await check.ExecuteScalarAsync(ct))!)throw new RouteReferenceNotFoundException();
        }
        await using var command=source.CreateCommand("SELECT r.rota_id,r.model_revizyon_id,o.operasyon_id,o.operasyon_kodu,o.operasyon_adi,r.sira_no,o.standart_sure,o.sure_birimi FROM product_lifecycle.operasyon_rotasi r JOIN product_lifecycle.operasyon_tanimi o ON o.operasyon_id=r.operasyon_id WHERE r.model_revizyon_id=@id ORDER BY r.sira_no");
        command.Parameters.AddWithValue("id",revisionId);
        await using var reader=await command.ExecuteReaderAsync(ct);var rows=new List<ProductionRouteDto>();
        while(await reader.ReadAsync(ct))rows.Add(ReadStep(reader));return rows;
    }
    public async Task<ProductionRouteDto> AddStepAsync(Guid revisionId,Guid operationId,int sequence,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var transaction=await connection.BeginTransactionAsync(ct);
        await using(var check=new NpgsqlCommand("SELECT onay_durumu FROM product_lifecycle.model_revizyon WHERE model_revizyon_id=@id FOR UPDATE",connection,transaction))
        {
            check.Parameters.AddWithValue("id",revisionId);var state=await check.ExecuteScalarAsync(ct) as string;
            if(state is null)throw new RouteReferenceNotFoundException();
            if(state!="Draft")throw new RouteRevisionLockedException();
        }
        var id=Guid.NewGuid();
        await using var command=new NpgsqlCommand("""
            WITH added AS(INSERT INTO product_lifecycle.operasyon_rotasi(rota_id,model_revizyon_id,operasyon_id,sira_no)
              VALUES(@id,@revision,@operation,@sequence) RETURNING *)
            SELECT r.rota_id,r.model_revizyon_id,o.operasyon_id,o.operasyon_kodu,o.operasyon_adi,r.sira_no,o.standart_sure,o.sure_birimi
            FROM added r JOIN product_lifecycle.operasyon_tanimi o ON o.operasyon_id=r.operasyon_id
            """,connection,transaction);
        command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("revision",revisionId);command.Parameters.AddWithValue("operation",operationId);command.Parameters.AddWithValue("sequence",sequence);
        try
        {
            await using var reader=await command.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))throw new RouteReferenceNotFoundException();var result=ReadStep(reader);
            await reader.CloseAsync();await transaction.CommitAsync(ct);return result;
        }
        catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.UniqueViolation){throw new DuplicateRouteEntryException();}
        catch(PostgresException ex)when(ex.SqlState==PostgresErrorCodes.ForeignKeyViolation){throw new RouteReferenceNotFoundException();}
    }
    private static ProductionRouteDto ReadStep(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.GetDecimal(6),r.GetString(7));
}
