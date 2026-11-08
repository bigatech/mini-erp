using DataIntakeAnalytics.Application;
using DataIntakeAnalytics.Infrastructure;
using Xunit;

namespace MiniErp.Analytics.Tests;

public sealed class DashboardPerformanceTests
{
    [Theory]
    [InlineData("İş Emri / IĞDIR ı ŞÇÖÜ 2026")]
    [InlineData("A\u0301 Å É Straße Ελληνικά 中文 𐐀")]
    [InlineData("")]
    [InlineData("__sheet [2] / 00.012,50 %")]
    public void Allocation_reduced_normalization_preserves_existing_unicode_rules(string value)
    {
        var expected = string.Concat(value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)))
            .ToLowerInvariant().Replace('ı', 'i');
        Assert.Equal(expected, ProcessAnalytics.Normalize(value));
    }

    [Fact]
    public void Reused_source_resolution_keeps_inferred_size_and_explicit_work_order_relationship()
    {
        var file = Source with
        {
            Columns = ["__sheet", "Tarih", "Malzeme No", "Malzeme Adı", "İş Emri No", "Sipariş No", "Sipariş Kalem No", "Kesim Adet"],
            Rows = [
                ["KESİM", "2026-10-01", "A", "Ürün (XL)", "W1", "S1", "1", "10"],
                ["KESİM", "2026-10-02", "A", "Ürün (XL)", "W1", "", "", "20"]]
        };
        var result = ProcessAnalytics.Calculate([file], "kesim");
        Assert.Equal(2, result.RecordCount);
        Assert.All(result.Rows, row => { Assert.Equal("XL", row.Size); Assert.Equal("S1", row.Order); Assert.Equal("1", row.OrderLine); });
        Assert.Equal(20, result.Metrics.Single(m => m.Key == "daily").Value);
    }

    private static readonly StagedFile Source = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "source.csv",
        ["__sheet", "Tarih", "Malzeme No", "Kesim Adet"], [["KESİM", "2026-10-01", "A", "10"]], new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Source_version_is_order_independent_and_invalidates_on_import_status_name_and_local_day()
    {
        var second = Source with { Id = Guid.NewGuid() };
        var day = new DateOnly(2026, 11, 2);
        var version = DashboardSourceVersion.Create([Source, second], day);
        Assert.Equal(version, DashboardSourceVersion.Create([second, Source], day));
        Assert.NotEqual(version, DashboardSourceVersion.Create([Source], day));
        Assert.NotEqual(version, DashboardSourceVersion.Create([Source, second with { Status = "Superseded" }], day));
        Assert.NotEqual(version, DashboardSourceVersion.Create([Source, second with { FileName = "Üretim Raporu 01.11.2026.xlsx" }], day));
        Assert.NotEqual(version, DashboardSourceVersion.Create([Source, second], day.AddDays(1)));
    }

    [Fact]
    public async Task Warm_process_request_does_not_load_staging_rows_and_filtered_result_uses_its_own_cache_key()
    {
        var repository = new BatchRepository();
        var handler = new GetProcessDashboardHandler(repository, new Cache());
        var first = await handler.HandleAsync("kesim", null, new(2026, 11, 1), null, default);
        var warm = await handler.HandleAsync("kesim", null, new(2026, 11, 1), null, default);
        Assert.Equal(1, repository.Batches);
        Assert.Equal(10, first.Metrics.Single(m => m.Key == "daily").Value);
        Assert.Same(first, warm);
        var filtered = await handler.HandleAsync("kesim", null, new(2026, 11, 1), "missing", default);
        Assert.Equal(2, repository.Batches);
        Assert.Null(filtered.Metrics.Single(m => m.Key == "daily").Value);
        Assert.Equal(0, filtered.RecordCount);
        repository.Source = Source with { Status = "Superseded" };
        var invalidated = await handler.HandleAsync("kesim", null, new(2026, 11, 1), null, default);
        Assert.Equal(3, repository.Batches);
        Assert.Equal(0, invalidated.RecordCount);
    }

    private sealed class BatchRepository : IStagingRepository
    {
        public StagedFile Source { get; set; } = DashboardPerformanceTests.Source;
        public int Batches { get; private set; }
        public Task<IReadOnlyList<StagedFile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<StagedFile>>([Source with { Rows = [] }]);
        public Task<IReadOnlyList<StagedFile>> LoadCompletedAsync(IReadOnlyList<StagedFile> listed, CancellationToken ct)
        {
            Batches++;
            return Task.FromResult<IReadOnlyList<StagedFile>>(listed.Where(f => f.Status == "Completed").Select(f => f with { Rows = Source.Rows }).ToArray());
        }
        public Task<StagedFile?> GetAsync(Guid id, CancellationToken ct) => throw new InvalidOperationException("Batch-capable repository must not load one file at a time.");
        public Task<byte[]?> GetContentAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid> SaveAsync(string n, string m, string s, byte[] b, string[] c, IReadOnlyList<string[]> r, CancellationToken ct) => throw new NotSupportedException();
    }

    // This verifies handler cache/batch wiring; PostgreSQL single-flight and SQL
    // persistence require the separate live endpoint smoke check.
    private sealed class Cache : IProcessDashboardCache
    {
        private readonly Dictionary<string, ProcessDashboard> snapshots = [];
        public async Task<ProcessDashboard> GetOrCreateAsync(IReadOnlyList<StagedFile> manifest, string process, DateOnly? from, DateOnly? to, string? material, Func<Task<ProcessDashboard>> calculate, CancellationToken ct)
        {
            var key = DashboardSourceVersion.Create(manifest, new(2026, 11, 2)) + $"|{process}|{from}|{to}|{material}";
            if (!snapshots.TryGetValue(key, out var result)) snapshots[key] = result = await calculate();
            return result;
        }
    }
}
