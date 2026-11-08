using System.Globalization;
using System.Text;

namespace DataIntakeAnalytics.Application;

public sealed record StagedFile(Guid Id, string FileName, string[] Columns, IReadOnlyList<string[]> Rows, DateTimeOffset UploadedAt, string Status = "Completed", string MediaType = "application/octet-stream", long FileSize = 0);
public sealed record DailyKpi(string Date, decimal Revenue, decimal Expense, decimal Profit);
public sealed record CategoryKpi(string Name, decimal Quantity);
public sealed record DashboardKpi(int TotalFiles, int TotalRecords, decimal? ProductionQuantity, decimal? Revenue, decimal? Expense, decimal? Profit, IReadOnlyList<CategoryKpi> ProductProduction, IReadOnlyList<CategoryKpi> OperationDistribution, IReadOnlyList<DailyKpi> Daily, IReadOnlyList<StagedFile> RecentFiles);

public interface ISpreadsheetReader
{
    Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, string extension, CancellationToken cancellationToken);
}
public interface IPdfReader
{
    Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, CancellationToken cancellationToken);
}
public interface IStagingRepository
{
    Task<byte[]?> GetContentAsync(Guid id, CancellationToken cancellationToken);
    Task<Guid> SaveAsync(string fileName, string mediaType, string status, byte[] fileContent, string[] columns, IReadOnlyList<string[]> rows, CancellationToken cancellationToken);
    Task<IReadOnlyList<StagedFile>> ListAsync(CancellationToken cancellationToken);
    Task<StagedFile?> GetAsync(Guid id, CancellationToken cancellationToken);
    async Task<IReadOnlyList<StagedFile>> LoadCompletedAsync(IReadOnlyList<StagedFile> listed, CancellationToken ct)
    {
        var files = new List<StagedFile>();
        foreach (var file in listed.Where(f => f.Status == "Completed"))
        {
            ct.ThrowIfCancellationRequested();
            if (await GetAsync(file.Id, ct) is { } full) files.Add(full);
        }
        return files;
    }
}

public sealed class ImportFileHandler(ISpreadsheetReader spreadsheetReader, IPdfReader pdfReader, IStagingRepository repository)
{
    public async Task<StagedFile> HandleAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx" or ".xls" or ".ods" or ".pdf")) throw new InvalidDataException("Yalnızca CSV, XLSX, XLS, ODS ve PDF dosyaları desteklenir.");
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        await using var parseStream = new MemoryStream(bytes, writable: false);
        var (columns, rows) = extension == ".pdf"
            ? await pdfReader.ReadAsync(parseStream, cancellationToken)
            : await spreadsheetReader.ReadAsync(parseStream, extension, cancellationToken);
        if (columns.Length == 0) throw new InvalidDataException("Dosyada okunabilir veri bulunamadı.");
        if (columns.Length > 500 || rows.Count > 100_000) throw new InvalidDataException("Dosya 500 birleşik sütun ve 100.000 veri satırı sınırını aşıyor.");
        var safeName = Path.GetFileName(fileName);
        var mediaType = extension switch { ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xls" => "application/vnd.ms-excel", ".ods" => "application/vnd.oasis.opendocument.spreadsheet", ".pdf" => "application/pdf", _ => "text/csv" };
        var status = extension == ".pdf" && rows.Any(row => row.Length > 1 && row[1].Contains("OCR incelemesi gerekli", StringComparison.OrdinalIgnoreCase)) ? "NeedsReview" : "Completed";
        var id = await repository.SaveAsync(safeName, mediaType, status, bytes, columns, rows, cancellationToken);
        return new(id, safeName, columns, rows, DateTimeOffset.UtcNow, status, mediaType, bytes.Length);
    }
}

public sealed class ListStagedFilesHandler(IStagingRepository repository)
{ public Task<IReadOnlyList<StagedFile>> HandleAsync(CancellationToken ct) => repository.ListAsync(ct); }
public sealed class GetStagedFileHandler(IStagingRepository repository)
{ public Task<StagedFile?> HandleAsync(Guid id, CancellationToken ct) => repository.GetAsync(id, ct); }

public sealed class GetDashboardHandler(IStagingRepository repository)
{
    public async Task<DashboardKpi> HandleAsync(CancellationToken ct)
    {
        var listed = await repository.ListAsync(ct);
        var files = await repository.LoadCompletedAsync(listed, ct);
        var summaries = new List<ProcessDashboard>();
        foreach (var process in ProcessAnalytics.Processes)
        {
            ct.ThrowIfCancellationRequested();
            summaries.Add(ProcessAnalytics.Calculate(files,process));
        }
        var cost = summaries.Single(p => p.Process == "maliyet");
        decimal? Value(string key) => cost.Metrics.Single(m => m.Key == key).Value;
        var operations = summaries.Where(p => p.Process is "kesim" or "dikim" or "paket")
            .Select(p => new { p.Process, Quantity = p.Metrics.Single(m => m.Key == "daily").Value })
            .Where(p => p.Quantity.HasValue).Select(p => new CategoryKpi(p.Process,p.Quantity!.Value)).ToArray();
        return new(listed.Count,files.Where(f => f.Status == "Completed").Sum(f => f.Rows.Count),null,Value("revenue"),Value("expense"),Value("profit"),[],operations,[],listed.Take(8).ToArray());
    }
}
