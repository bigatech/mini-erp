using DataIntakeAnalytics.Application;
using Xunit;

namespace MiniErp.Analytics.Tests;

public sealed class HandlerLoadingTests
{
    private static readonly StagedFile Completed = new(Guid.NewGuid(), "source.csv",
        ["__sheet", "Tarih", "Malzeme No", "Kesim Adet"], [["KESİM", "2026-10-01", "A", "10"]], DateTimeOffset.UtcNow);
    private static readonly StagedFile Review = new(Guid.NewGuid(), "scan.pdf",
        ["__page", "text"], [["1", "OCR incelemesi gerekli"]], DateTimeOffset.UtcNow, "NeedsReview");
    private static readonly StagedFile Superseded = Completed with { Id = Guid.NewGuid(), Status = "Superseded" };

    private sealed class Repository(bool allowNeedsReview = false) : IStagingRepository
    {
        private readonly StagedFile[] files = [Completed, Review, Superseded];
        public List<Guid> Loaded { get; } = [];
        public Task<IReadOnlyList<StagedFile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<StagedFile>>(files);
        public Task<StagedFile?> GetAsync(Guid id, CancellationToken ct)
        {
            var file = files.Single(f => f.Id == id);
            if (file.Status == "Superseded" || file.Status == "NeedsReview" && !allowNeedsReview)
                throw new InvalidOperationException("Inactive rows must not be loaded by this handler.");
            Loaded.Add(id);
            return Task.FromResult<StagedFile?>(file);
        }
        public Task<byte[]?> GetContentAsync(Guid id, CancellationToken ct) => Task.FromResult<byte[]?>([1]);
        public Task<Guid> SaveAsync(string name, string media, string status, byte[] bytes, string[] columns, IReadOnlyList<string[]> rows, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Readers : ISpreadsheetReader, IPdfReader
    {
        public Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, string extension, CancellationToken ct) =>
            Task.FromResult((Completed.Columns, Completed.Rows));
        public Task<(string[] Columns, IReadOnlyList<string[]> Rows)> ReadAsync(Stream content, CancellationToken ct) =>
            Task.FromResult((Review.Columns, Review.Rows));
    }

    [Fact] public async Task Process_dashboard_does_not_load_superseded_or_review_rows()
    {
        var repository = new Repository();
        var result = await new GetProcessDashboardHandler(repository).HandleAsync("kesim", null, new(2026,11,1), null, default);
        Assert.Equal(new[] { Completed.Id }, repository.Loaded);
        Assert.Equal(10, result.Metrics.Single(m => m.Key == "daily").Value);
    }

    [Fact] public async Task Dashboard_keeps_historical_file_count_and_recent_metadata_without_loading_inactive_rows()
    {
        var repository = new Repository();
        var result = await new GetDashboardHandler(repository).HandleAsync(default);
        Assert.Equal(new[] { Completed.Id }, repository.Loaded);
        Assert.Equal(3, result.TotalFiles); Assert.Equal(1, result.TotalRecords);
        Assert.Equal(3, result.RecentFiles.Count);
        Assert.Contains(result.RecentFiles, f => f.Status == "Superseded");
        Assert.Contains(result.RecentFiles, f => f.Status == "NeedsReview");
    }

    [Fact] public async Task Validation_keeps_review_file_in_manifest_but_never_loads_superseded_rows()
    {
        var repository = new Repository(allowNeedsReview:true); var readers = new Readers();
        var result = await new GetDashboardValidationHandler(repository, readers, readers).HandleAsync(default);
        Assert.Equal(new[] { Completed.Id, Review.Id }, repository.Loaded);
        Assert.Equal(2, result.TotalFiles); Assert.Equal(2, result.StagingRecords);
        var review = Assert.Single(result.Files, f => f.Status == "NeedsReview");
        Assert.True(review.CellsMatch); Assert.Equal(1, review.SourceRows);
        Assert.DoesNotContain(result.Files, f => f.Status == "Superseded");
    }
}
