using System.Security.Cryptography;

namespace DataIntakeAnalytics.Application;

public sealed record ValidationFile(Guid Id, string FileName, string Status, string? Sha256, int StagingRows, int? SourceRows, bool CellsMatch, string? Error);
public sealed record ValidationMetric(string Process, string Key, string Kpi, string Unit, decimal? Source, decimal? Dashboard, decimal? Difference, string Status, string Formula, IReadOnlyList<string> Sources, IReadOnlyList<string> Limitations);
public sealed record DashboardValidation(DateOnly AsOf, int TotalFiles, int StagingRecords, int SourceRecords, int MatchedKpis, int ComparableKpis, int TotalKpis, decimal? AccuracyPercent, decimal CoveragePercent, IReadOnlyList<ValidationFile> Files, IReadOnlyList<ValidationMetric> Metrics, string Method);

// Reparse original archived bytes, never use staging cells as the source value.
// This checks source -> staging -> dashboard consistency; formula correctness is tested separately.
public sealed class GetDashboardValidationHandler(IStagingRepository repository, ISpreadsheetReader spreadsheets, IPdfReader pdfs)
{
    public async Task<DashboardValidation> HandleAsync(CancellationToken ct)
    {
        var staged = new List<StagedFile>(); var sources = new List<StagedFile>(); var manifest = new List<ValidationFile>();
        foreach (var item in await repository.ListAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (item.Status == "Superseded") continue;
            var file = await repository.GetAsync(item.Id, ct);
            if (file is null || file.Status == "Superseded") continue;
            staged.Add(file);
            string? hash = null;
            try
            {
                var bytes = await repository.GetContentAsync(file.Id, ct) ?? throw new InvalidDataException("Özgün dosya arşivde bulunamadı.");
                hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                await using var stream = new MemoryStream(bytes, writable: false);
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                ct.ThrowIfCancellationRequested();
                var parsed = extension == ".pdf" ? await pdfs.ReadAsync(stream, ct) : await spreadsheets.ReadAsync(stream, extension, ct);
                ct.ThrowIfCancellationRequested();
                var status = extension == ".pdf" && parsed.Rows.Any(r => r.Length > 1 && r[1].Contains("OCR incelemesi gerekli", StringComparison.OrdinalIgnoreCase)) ? "NeedsReview" : "Completed";
                sources.Add(file with { Columns = parsed.Columns, Rows = parsed.Rows, Status = status });
                bool match = file.Status == status && file.Columns.SequenceEqual(parsed.Columns) && file.Rows.Count == parsed.Rows.Count && file.Rows.Zip(parsed.Rows).All(pair => pair.First.SequenceEqual(pair.Second));
                manifest.Add(new(file.Id, file.FileName, file.Status, hash, file.Rows.Count, parsed.Rows.Count, match, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                manifest.Add(new(file.Id, file.FileName, file.Status, hash, file.Rows.Count, null, false, ex.Message));
            }
        }
        var asOf = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Istanbul").DateTime);
        var metrics = new List<ValidationMetric>();
        ct.ThrowIfCancellationRequested();
        var references = SourceKpiReference.Calculate(sources, asOf);
        foreach (var process in ProcessAnalytics.Processes)
        {
            ct.ThrowIfCancellationRequested();
            var dashboard = ProcessAnalytics.Calculate(staged, process, to: asOf);
            foreach (var metric in dashboard.Metrics)
            {
                references.TryGetValue(process+"/"+metric.Key, out var reference);
                var expected = reference?.Value;
                var difference = metric.Value - expected;
                var status = manifest.Any(f => !f.CellsMatch) ? "SourceMismatch" : !metric.Value.HasValue || !expected.HasValue ? "MissingData" : difference == 0 ? "Matched" : "Mismatch";
                metrics.Add(new(process, metric.Key, metric.Label, metric.Unit, expected, metric.Value, difference, status, metric.Formula,
                    reference?.Sources ?? [], (reference is null ? new[] { "Bağımsız kaynak referansı veya gerekli veri alanları eksik." } : new[] { reference.Scope }).Concat(dashboard.MissingFields).Concat(dashboard.Warnings).Distinct().ToArray()));
            }
        }
        ct.ThrowIfCancellationRequested();
        return Summarize(asOf, manifest, metrics);
    }

    public static DashboardValidation Summarize(DateOnly asOf, IReadOnlyList<ValidationFile> files, IReadOnlyList<ValidationMetric> metrics)
    {
        int comparable = metrics.Count(m => m.Source.HasValue && m.Dashboard.HasValue);
        int matched = metrics.Count(m => m.Status == "Matched");
        return new(asOf, files.Count, files.Sum(f => f.StagingRows), files.Sum(f => f.SourceRows ?? 0), matched, comparable, metrics.Count,
            comparable == 0 ? null : decimal.Round(100m * matched / comparable, 2), metrics.Count == 0 ? 0 : decimal.Round(100m * comparable / metrics.Count, 2), files, metrics,
            "Özgün dosya yeniden okunur; sütun/satır/hücreler staging ile karşılaştırılır. KPI referansı dashboard motorundan bağımsız doğrudan aritmetik ile son tarihli üretim raporu ve XLS kalite dökümünden hesaplanır. Eski raporlarla birleşim farkları uzlaştırma gerektirir. Boş/boş eşleşme sayılmaz. Yüzde yalnız sayısal karşılaştırmaların doğruluğudur; tüm işletme verisinin tamlığını kanıtlamaz. Superseded kayıtlar hariçtir.");
    }
}
