using Npgsql;
using SalesDrafts.Application;
namespace SalesDrafts.Infrastructure;

public sealed partial class PostgresOrderDraftRepository(NpgsqlDataSource source) : IOrderDraftRepository, IOrderChangeApprovalRepository, IOrderMaterialRequestLink
{
    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
        CREATE SCHEMA IF NOT EXISTS sales_drafts;
        CREATE SEQUENCE IF NOT EXISTS sales_drafts.order_number_seq AS bigint START WITH 1;
        CREATE TABLE IF NOT EXISTS sales_drafts.order_draft (
          id uuid PRIMARY KEY, number text NOT NULL UNIQUE, revision_id uuid NOT NULL,
          status text NOT NULL DEFAULT 'Draft' CHECK(status='Draft'), created_at timestamptz NOT NULL);
        ALTER TABLE sales_drafts.order_draft ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1;
        ALTER TABLE sales_drafts.order_draft DROP CONSTRAINT IF EXISTS order_draft_status_check;
        ALTER TABLE sales_drafts.order_draft ADD CONSTRAINT order_draft_status_check CHECK(status IN ('Draft','Cancelled'));
        CREATE TABLE IF NOT EXISTS sales_drafts.order_draft_event(
          event_id uuid PRIMARY KEY,draft_id uuid NOT NULL REFERENCES sales_drafts.order_draft(id),
          type text NOT NULL,occurred_at timestamptz NOT NULL DEFAULT now(),from_status text NOT NULL,to_status text NOT NULL,
          version integer NOT NULL,previous_lines jsonb NOT NULL);
        CREATE TABLE IF NOT EXISTS sales_drafts.order_material_request (
          order_id uuid NOT NULL REFERENCES sales_drafts.order_draft(id), material_request_id uuid NOT NULL UNIQUE,
          linked_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(order_id,material_request_id));
        CREATE TABLE IF NOT EXISTS sales_drafts.order_change_approval (
          id uuid PRIMARY KEY,order_id uuid NOT NULL REFERENCES sales_drafts.order_draft(id),
          type text NOT NULL CHECK(type IN ('Update','Cancel')),base_version integer NOT NULL,
          proposed_lines jsonb NOT NULL,requested_at timestamptz NOT NULL DEFAULT now(),
          status text NOT NULL DEFAULT 'Pending' CHECK(status IN ('Pending','Approved','Rejected')),
          decided_by text,decided_at timestamptz);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_order_pending_approval ON sales_drafts.order_change_approval(order_id) WHERE status='Pending';
        CREATE TABLE IF NOT EXISTS sales_drafts.order_draft_cell (
          cell_id uuid PRIMARY KEY DEFAULT gen_random_uuid(), draft_id uuid NOT NULL REFERENCES sales_drafts.order_draft(id), variant_option_id uuid NOT NULL,
          size_option_id uuid NOT NULL, quantity numeric NOT NULL CHECK(quantity>0), set_part_option_id uuid);
        ALTER TABLE sales_drafts.order_draft_cell ADD COLUMN IF NOT EXISTS set_part_option_id uuid;
        ALTER TABLE sales_drafts.order_draft_cell ADD COLUMN IF NOT EXISTS cell_id uuid NOT NULL DEFAULT gen_random_uuid();
        DO $migration$
        BEGIN
          IF EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=ANY(c.conkey)
                     WHERE c.conrelid='sales_drafts.order_draft_cell'::regclass AND c.contype='p' AND a.attname='draft_id') THEN
            ALTER TABLE sales_drafts.order_draft_cell DROP CONSTRAINT order_draft_cell_pkey;
            ALTER TABLE sales_drafts.order_draft_cell ADD PRIMARY KEY(cell_id);
          END IF;
        END $migration$;
        CREATE UNIQUE INDEX IF NOT EXISTS ix_order_draft_cell_part ON sales_drafts.order_draft_cell(draft_id,variant_option_id,size_option_id,set_part_option_id) NULLS NOT DISTINCT;
        """);
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<OrderDraftDto> CreateAsync(string number,Guid revisionId,IReadOnlyList<OrderDraftCell> lines,CancellationToken ct)
    {
        var id=Guid.NewGuid();var createdAt=DateTimeOffset.UtcNow;
        await using var connection=await source.OpenConnectionAsync(ct);
        await using var transaction=await connection.BeginTransactionAsync(ct);
        try
        {
            string? generatedNumber=null;
            // ON CONFLICT does not poison the transaction. Legacy manual SIP numbers
            // are skipped while nextval remains atomic across concurrent requests.
            for(var attempt=0;attempt<10&&generatedNumber is null;attempt++)
            {
                await using var command=new NpgsqlCommand("""
                WITH allocated AS (SELECT nextval('sales_drafts.order_number_seq') AS n)
                INSERT INTO sales_drafts.order_draft(id,number,revision_id,created_at)
                SELECT @id,'SIP-'||lpad(n::text,GREATEST(8,length(n::text)),'0'),@revision,@created FROM allocated
                ON CONFLICT(number) DO NOTHING RETURNING number
                """,connection,transaction);
                command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("revision",revisionId);command.Parameters.AddWithValue("created",createdAt);
                generatedNumber=await command.ExecuteScalarAsync(ct) as string;
            }
            if(generatedNumber is null)throw new DuplicateOrderDraftException();
            foreach(var cell in lines)
            {
                await using var command=new NpgsqlCommand("INSERT INTO sales_drafts.order_draft_cell(draft_id,variant_option_id,size_option_id,quantity,set_part_option_id) VALUES(@draft,@variant,@size,@quantity,@part)",connection,transaction);
                command.Parameters.AddWithValue("draft",id);command.Parameters.AddWithValue("variant",cell.VariantOptionId);command.Parameters.AddWithValue("size",cell.SizeOptionId);command.Parameters.AddWithValue("quantity",cell.Quantity);command.Parameters.Add("part",NpgsqlTypes.NpgsqlDbType.Uuid).Value=(object?)cell.SetPartOptionId??DBNull.Value;
                await command.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return new(id,generatedNumber,revisionId,"Draft",lines,createdAt);
        }
        catch(PostgresException ex) when(ex.SqlState==PostgresErrorCodes.UniqueViolation){throw new DuplicateOrderDraftException();}
    }
    public async Task<IReadOnlyList<OrderDraftDto>> ListAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
        SELECT d.id,d.number,d.revision_id,d.created_at,c.variant_option_id,c.size_option_id,c.quantity,c.set_part_option_id,d.status,d.version,
        EXISTS(SELECT 1 FROM sales_drafts.order_material_request m WHERE m.order_id=d.id),
        (SELECT jsonb_build_object('id',a.id,'type',a.type,'baseVersion',a.base_version,'proposedLines',a.proposed_lines,'requestedAt',a.requested_at,'status',a.status)
         FROM sales_drafts.order_change_approval a WHERE a.order_id=d.id AND a.status='Pending')
        FROM sales_drafts.order_draft d JOIN sales_drafts.order_draft_cell c ON c.draft_id=d.id
        ORDER BY d.created_at DESC,d.id,c.variant_option_id,c.size_option_id
        """);
        await using var reader=await command.ExecuteReaderAsync(ct);
        var drafts=new List<OrderDraftDto>();var cells=new Dictionary<Guid,List<OrderDraftCell>>();
        while(await reader.ReadAsync(ct))
        {
            var id=reader.GetGuid(0);
            if(!cells.TryGetValue(id,out var lines))
            {
                lines=[];cells.Add(id,lines);
                drafts.Add(new(id,reader.GetString(1),reader.GetGuid(2),reader.GetString(8),lines,reader.GetFieldValue<DateTimeOffset>(3),reader.GetInt32(9),reader.IsDBNull(11)?null:System.Text.Json.JsonSerializer.Deserialize<OrderChangeApproval>(reader.GetString(11),JsonOptions),reader.GetBoolean(10)));
            }
            lines.Add(new(reader.GetGuid(4),reader.GetGuid(5),reader.GetDecimal(6),reader.IsDBNull(7)?null:reader.GetGuid(7)));
        }
        return drafts;
    }
}
