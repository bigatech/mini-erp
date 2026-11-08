using System.Text;
using DataIntakeAnalytics.Application;
using DataIntakeAnalytics.Infrastructure;
using Xunit;

namespace MiniErp.Analytics.Tests;

public sealed class DashboardValidationTests
{
    private sealed class Repository(StagedFile file, byte[]? bytes) : IStagingRepository
    {
        public Task<byte[]?> GetContentAsync(Guid id, CancellationToken ct) => Task.FromResult(bytes);
        public Task<IReadOnlyList<StagedFile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<StagedFile>>([file]);
        public Task<StagedFile?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<StagedFile?>(file);
        public Task<Guid> SaveAsync(string name, string media, string status, byte[] content, string[] columns, IReadOnlyList<string[]> rows, CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task<DashboardValidation> Validate(string quantity, bool missing = false)
    {
        var bytes = Encoding.UTF8.GetBytes("__sheet;Tarih;Malzeme No;Yapılan İş;Kesim Adet\nKESİM;2026-10-01;A;Kesim;10\n");
        var file = new StagedFile(Guid.NewGuid(), "ÜRETİM RAPORU 01.10.2026.csv", ["__sheet","Tarih","Malzeme No","Yapılan İş","Kesim Adet"], [["KESİM","2026-10-01","A","Kesim",quantity]], DateTimeOffset.UtcNow);
        return await new GetDashboardValidationHandler(new Repository(file, missing ? null : bytes), new SpreadsheetReader(), new PdfReader()).HandleAsync(default);
    }

    [Fact] public async Task Original_bytes_are_reparsed_and_corrupted_staging_is_detected()
    {
        var result = await Validate("999");
        var daily = result.Metrics.Single(m => m.Process == "kesim" && m.Key == "daily");
        Assert.Equal(10, daily.Source); Assert.Equal(999, daily.Dashboard); Assert.Equal(989, daily.Difference);
        Assert.Equal("SourceMismatch", daily.Status); Assert.False(result.Files.Single().CellsMatch);
        Assert.Equal(0, result.MatchedKpis);
    }

    [Fact] public async Task Null_values_are_not_counted_as_correct_and_coverage_is_separate()
    {
        var result = await Validate("10");
        Assert.Equal(1, result.MatchedKpis); Assert.Equal(1, result.ComparableKpis);
        Assert.Equal(24, result.TotalKpis); Assert.Equal(100, result.AccuracyPercent);
        Assert.Equal(4.17m, result.CoveragePercent);
        Assert.All(result.Metrics.Where(m => m.Source is null), m => Assert.Equal("MissingData", m.Status));
    }

    [Fact] public async Task Missing_original_file_cannot_produce_a_successful_validation()
    {
        var result = await Validate("10", missing: true);
        Assert.Equal(0, result.MatchedKpis); Assert.Null(result.AccuracyPercent);
        Assert.NotNull(result.Files.Single().Error);
    }

    [Fact] public void Explicit_zero_is_comparable_and_an_empty_dataset_has_no_accuracy()
    {
        var zero = new ValidationMetric("kesim", "daily", "Günlük", "adet", 0, 0, 0, "Matched", "", [], []);
        Assert.Equal(100, GetDashboardValidationHandler.Summarize(new(2026,11,1), [], [zero]).AccuracyPercent);
        Assert.Null(GetDashboardValidationHandler.Summarize(new(2026,11,1), [], []).AccuracyPercent);
    }

    [Fact] public void Independent_reference_uses_latest_report_not_a_union_of_revised_snapshots()
    {
        var old = new StagedFile(Guid.NewGuid(), "ÜRETİM RAPORU 19.10.2026.ods", ["__sheet","Tarih","Malzeme No","Fason Giden Adet","Fason Gelen Adet"],
            [["FASON_ADET","2026-10-19","A","100","20"]], DateTimeOffset.UtcNow);
        var revised = old with { Id = Guid.NewGuid(), FileName = "ÜRETİM RAPORU 20.10.2026.ods", Rows = [["FASON_ADET","2026-10-19","A","100","80"]] };
        var result = SourceKpiReference.Calculate([old,revised],new(2026,11,1));
        Assert.Equal(100,result["fason/sent"].Value); Assert.Equal(80,result["fason/received"].Value); Assert.Equal(20,result["fason/net"].Value);
        var dashboard = ProcessAnalytics.Calculate([old,revised],"fason",to:new(2026,11,1));
        Assert.Equal(100,dashboard.Metrics.Single(m => m.Key == "sent").Value);
        Assert.Equal(80,dashboard.Metrics.Single(m => m.Key == "received").Value);
        Assert.Equal(20,dashboard.Metrics.Single(m => m.Key == "net").Value);
    }

    [Fact] public void Independent_daily_reference_does_not_sum_report_total_or_previous_days()
    {
        var file = new StagedFile(Guid.NewGuid(), "ÜRETİM RAPORU 20.10.2026.ods", ["__sheet","Tarih","Malzeme No","Kesim Adet","Toplam"],
            [["KESİM","2026-10-18","A","500","500"],["KESİM","2026-10-19","A","10","30"],["KESİM","2026-10-19","B","20",""]],DateTimeOffset.UtcNow);
        Assert.Equal(30,SourceKpiReference.Calculate([file],new(2026,11,1))["kesim/daily"].Value);
    }
}
