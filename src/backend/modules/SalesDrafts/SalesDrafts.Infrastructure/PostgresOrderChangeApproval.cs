using Npgsql;
using NpgsqlTypes;
using SalesDrafts.Application;
using System.Text.Json;
namespace SalesDrafts.Infrastructure;
public sealed partial class PostgresOrderDraftRepository
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private const string ApprovalSelect="SELECT id,type,base_version,proposed_lines,requested_at,status,decided_by,decided_at FROM sales_drafts.order_change_approval";
    private static OrderChangeApproval ReadApproval(NpgsqlDataReader r)=>new(r.GetGuid(0),r.GetString(1),r.GetInt32(2),JsonSerializer.Deserialize<OrderDraftCell[]>(r.GetString(3),JsonOptions)!,r.GetFieldValue<DateTimeOffset>(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetFieldValue<DateTimeOffset>(7));
    public async Task<IReadOnlyList<OrderChangeApproval>> ApprovalsAsync(Guid id,CancellationToken ct)
    {
        if(await GetAsync(id,ct) is null)throw new OrderDraftNotFoundException();
        await using var command=source.CreateCommand(ApprovalSelect+" WHERE order_id=@id ORDER BY requested_at,id");command.Parameters.AddWithValue("id",id);
        await using var r=await command.ExecuteReaderAsync(ct);var result=new List<OrderChangeApproval>();while(await r.ReadAsync(ct))result.Add(ReadApproval(r));return result;
    }
    public async Task LinkAsync(Guid orderId,Guid materialRequestId,CancellationToken ct)
    {
        if(materialRequestId==Guid.Empty)throw new ArgumentException("Malzeme talebi kimliği gereklidir.");
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var current=await ReadCard(connection,tx,orderId,true,ct)??throw new OrderDraftNotFoundException();
        OrderDraftCardRules.Writable(current,current.Version);
        await using var command=new NpgsqlCommand("INSERT INTO sales_drafts.order_material_request(order_id,material_request_id) VALUES(@id,@request) ON CONFLICT(order_id,material_request_id) DO NOTHING",connection,tx);
        command.Parameters.AddWithValue("id",orderId);command.Parameters.AddWithValue("request",materialRequestId);
        if(await command.ExecuteNonQueryAsync(ct)>0)await SaveVersionAndEvent(connection,tx,current,current.Status,current.Version+1,"MaterialRequestLinked",ct);
        await tx.CommitAsync(ct);
    }
    public async Task<OrderDraftDto> DecideAsync(Guid id,Guid approvalId,int expectedVersion,bool approve,string actor,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(actor))throw new ArgumentException("Onaylayan kullanıcı kimliği gereklidir.",nameof(actor));
        await using var connection=await source.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);
        var current=await ReadCard(connection,tx,id,true,ct)??throw new OrderDraftNotFoundException();
        OrderChangeApproval approval;
        await using(var command=new NpgsqlCommand(ApprovalSelect+" WHERE order_id=@id AND id=@approval FOR UPDATE",connection,tx))
        {command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("approval",approvalId);await using var r=await command.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new OrderApprovalNotFoundException();approval=ReadApproval(r);}
        OrderApprovalRules.Decidable(current,approval,expectedVersion);
        var lines=current.Lines;var status=current.Status;
        if(approve&&approval.Type=="Update")
        {
            lines=approval.ProposedLines;
            await using(var delete=new NpgsqlCommand("DELETE FROM sales_drafts.order_draft_cell WHERE draft_id=@id",connection,tx)){delete.Parameters.AddWithValue("id",id);await delete.ExecuteNonQueryAsync(ct);}
            foreach(var line in lines)
            {
                await using var insert=new NpgsqlCommand("INSERT INTO sales_drafts.order_draft_cell(draft_id,variant_option_id,size_option_id,quantity,set_part_option_id) VALUES(@id,@variant,@size,@quantity,@part)",connection,tx);
                insert.Parameters.AddWithValue("id",id);insert.Parameters.AddWithValue("variant",line.VariantOptionId);insert.Parameters.AddWithValue("size",line.SizeOptionId);insert.Parameters.AddWithValue("quantity",line.Quantity);insert.Parameters.Add("part",NpgsqlDbType.Uuid).Value=(object?)line.SetPartOptionId??DBNull.Value;await insert.ExecuteNonQueryAsync(ct);
            }
        }
        if(approve&&approval.Type=="Cancel")status="Cancelled";
        await using(var decision=new NpgsqlCommand("UPDATE sales_drafts.order_change_approval SET status=@status,decided_by=@actor,decided_at=now() WHERE id=@id",connection,tx))
        {decision.Parameters.AddWithValue("id",approvalId);decision.Parameters.AddWithValue("status",approve?"Approved":"Rejected");decision.Parameters.AddWithValue("actor",actor);await decision.ExecuteNonQueryAsync(ct);}
        await SaveVersionAndEvent(connection,tx,current,status,current.Version+1,approve?"ChangeApproved":"ChangeRejected",ct);
        await tx.CommitAsync(ct);return current with{Lines=lines,Status=status,Version=current.Version+1,PendingApproval=null};
    }
    private static async Task SaveVersionAndEvent(NpgsqlConnection connection,NpgsqlTransaction tx,OrderDraftDto current,string status,int version,string type,CancellationToken ct)
    {
        await using(var command=new NpgsqlCommand("UPDATE sales_drafts.order_draft SET status=@status,version=@version WHERE id=@id",connection,tx))
        {command.Parameters.AddWithValue("id",current.Id);command.Parameters.AddWithValue("status",status);command.Parameters.AddWithValue("version",version);await command.ExecuteNonQueryAsync(ct);}
        await using var audit=new NpgsqlCommand("INSERT INTO sales_drafts.order_draft_event(event_id,draft_id,type,from_status,to_status,version,previous_lines) VALUES(@event,@id,@type,@from,@to,@version,@previous)",connection,tx);
        audit.Parameters.AddWithValue("event",Guid.NewGuid());audit.Parameters.AddWithValue("id",current.Id);audit.Parameters.AddWithValue("type",type);audit.Parameters.AddWithValue("from",current.Status);audit.Parameters.AddWithValue("to",status);audit.Parameters.AddWithValue("version",version);audit.Parameters.Add("previous",NpgsqlDbType.Jsonb).Value=JsonSerializer.Serialize(current.Lines,JsonOptions);await audit.ExecuteNonQueryAsync(ct);
    }
}
