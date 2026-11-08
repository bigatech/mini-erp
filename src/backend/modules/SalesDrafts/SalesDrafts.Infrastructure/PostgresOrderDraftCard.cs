using Npgsql;
using NpgsqlTypes;
using SalesDrafts.Application;
using System.Text.Json;
namespace SalesDrafts.Infrastructure;
public sealed partial class PostgresOrderDraftRepository
{
    public async Task<OrderDraftDto?> GetAsync(Guid id,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        var result=await ReadCard(connection,tx,id,false,ct);await tx.CommitAsync(ct);return result;
    }
    private static async Task<OrderDraftDto?> ReadCard(NpgsqlConnection connection,NpgsqlTransaction? tx,Guid id,bool locked,CancellationToken ct)
    {
        OrderDraftDto? draft;
        await using(var command=new NpgsqlCommand("SELECT number,revision_id,status,created_at,version FROM sales_drafts.order_draft WHERE id=@id"+(locked?" FOR UPDATE":""),connection,tx))
        {
            command.Parameters.AddWithValue("id",id);await using var r=await command.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct))return null;
            draft=new(id,r.GetString(0),r.GetGuid(1),r.GetString(2),[],r.GetFieldValue<DateTimeOffset>(3),r.GetInt32(4));
        }
        var lines=new List<OrderDraftCell>();
        await using(var command=new NpgsqlCommand("SELECT variant_option_id,size_option_id,quantity,set_part_option_id FROM sales_drafts.order_draft_cell WHERE draft_id=@id ORDER BY variant_option_id,size_option_id,set_part_option_id",connection,tx))
        {
            command.Parameters.AddWithValue("id",id);await using var r=await command.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))lines.Add(new(r.GetGuid(0),r.GetGuid(1),r.GetDecimal(2),r.IsDBNull(3)?null:r.GetGuid(3)));
        }
        OrderChangeApproval? pending=null;bool linked;
        await using(var meta=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM sales_drafts.order_material_request WHERE order_id=@id)",connection,tx))
        {meta.Parameters.AddWithValue("id",id);linked=(bool)(await meta.ExecuteScalarAsync(ct))!;}
        await using(var approvals=new NpgsqlCommand(ApprovalSelect+" WHERE order_id=@id AND status='Pending'",connection,tx))
        {approvals.Parameters.AddWithValue("id",id);await using var reader=await approvals.ExecuteReaderAsync(ct);if(await reader.ReadAsync(ct))pending=ReadApproval(reader);}
        return draft with{Lines=lines,HasMaterialRequests=linked,PendingApproval=pending};
    }
    public Task<OrderDraftDto> ReplaceAsync(Guid id,int expectedVersion,IReadOnlyList<OrderDraftCell> lines,CancellationToken ct)=>Mutate(id,expectedVersion,lines,false,ct);
    public Task<OrderDraftDto> CancelAsync(Guid id,int expectedVersion,CancellationToken ct)=>Mutate(id,expectedVersion,null,true,ct);
    private async Task<OrderDraftDto> Mutate(Guid id,int expectedVersion,IReadOnlyList<OrderDraftCell>? lines,bool cancel,CancellationToken ct)
    {
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var current=await ReadCard(connection,tx,id,true,ct)??throw new OrderDraftNotFoundException();
        OrderDraftCardRules.Writable(current,expectedVersion);
        if(current.HasMaterialRequests)
        {
            var pending=new OrderChangeApproval(Guid.NewGuid(),cancel?"Cancel":"Update",current.Version,cancel?current.Lines:lines!,DateTimeOffset.UtcNow);
            await using(var request=new NpgsqlCommand("INSERT INTO sales_drafts.order_change_approval(id,order_id,type,base_version,proposed_lines,requested_at) VALUES(@approval,@id,@type,@version,@lines,@at)",connection,tx))
            {
                request.Parameters.AddWithValue("approval",pending.Id);request.Parameters.AddWithValue("id",id);request.Parameters.AddWithValue("type",pending.Type);request.Parameters.AddWithValue("version",current.Version);request.Parameters.Add("lines",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(pending.ProposedLines,JsonOptions);request.Parameters.AddWithValue("at",pending.RequestedAt);await request.ExecuteNonQueryAsync(ct);
            }
            await SaveVersionAndEvent(connection,tx,current,"Draft",current.Version+1,"ApprovalRequested",ct);
            await tx.CommitAsync(ct);return current with{Version=current.Version+1,PendingApproval=pending};
        }
        if(!cancel)
        {
            await using(var delete=new NpgsqlCommand("DELETE FROM sales_drafts.order_draft_cell WHERE draft_id=@id",connection,tx)){delete.Parameters.AddWithValue("id",id);await delete.ExecuteNonQueryAsync(ct);}
            foreach(var line in lines!)
            {
                await using var insert=new NpgsqlCommand("INSERT INTO sales_drafts.order_draft_cell(draft_id,variant_option_id,size_option_id,quantity,set_part_option_id) VALUES(@id,@variant,@size,@quantity,@part)",connection,tx);
                insert.Parameters.AddWithValue("id",id);insert.Parameters.AddWithValue("variant",line.VariantOptionId);insert.Parameters.AddWithValue("size",line.SizeOptionId);insert.Parameters.AddWithValue("quantity",line.Quantity);insert.Parameters.Add("part",NpgsqlDbType.Uuid).Value=(object?)line.SetPartOptionId??DBNull.Value;
                await insert.ExecuteNonQueryAsync(ct);
            }
        }
        var status=cancel?"Cancelled":"Draft";var version=current.Version+1;
        await using(var update=new NpgsqlCommand("UPDATE sales_drafts.order_draft SET status=@status,version=@version WHERE id=@id",connection,tx)){update.Parameters.AddWithValue("id",id);update.Parameters.AddWithValue("status",status);update.Parameters.AddWithValue("version",version);await update.ExecuteNonQueryAsync(ct);}
        await using(var audit=new NpgsqlCommand("INSERT INTO sales_drafts.order_draft_event(event_id,draft_id,type,from_status,to_status,version,previous_lines) VALUES(@event,@id,@type,@from,@to,@version,@previous)",connection,tx))
        {
            audit.Parameters.AddWithValue("event",Guid.NewGuid());audit.Parameters.AddWithValue("id",id);audit.Parameters.AddWithValue("type",cancel?"Cancelled":"Updated");audit.Parameters.AddWithValue("from",current.Status);audit.Parameters.AddWithValue("to",status);audit.Parameters.AddWithValue("version",version);audit.Parameters.Add("previous",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(current.Lines);await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);return current with{Status=status,Version=version,Lines=cancel?current.Lines:lines!};
    }
    public async Task<IReadOnlyList<OrderDraftEvent>> EventsAsync(Guid id,CancellationToken ct)
    {
        if(await GetAsync(id,ct) is null)throw new OrderDraftNotFoundException();
        await using var command=source.CreateCommand("SELECT event_id,type,occurred_at,from_status,to_status,version FROM sales_drafts.order_draft_event WHERE draft_id=@id ORDER BY version");command.Parameters.AddWithValue("id",id);
        await using var r=await command.ExecuteReaderAsync(ct);var result=new List<OrderDraftEvent>();while(await r.ReadAsync(ct))result.Add(new(r.GetGuid(0),r.GetString(1),r.GetFieldValue<DateTimeOffset>(2),r.GetString(3),r.GetString(4),r.GetInt32(5)));return result;
    }
}
