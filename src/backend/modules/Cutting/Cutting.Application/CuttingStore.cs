namespace Cutting.Application;
public sealed class CuttingStoreException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed record CuttingDetail(CuttingDocument Document, CuttingReport Report, int CurrentOrderVersion, bool OrderChanged, string OrderStatus,
    IReadOnlyList<CuttingPlanReport> PlanReports);
public sealed record CuttingPlanReport(Guid GroupId, Guid PlanId, CuttingReport Report, CuttingWeightReport Weight);
public sealed record CuttingWeightReport(decimal? PlannedWeightKg, decimal? NetWeightKg, decimal? PlannedGramsPerPiece,
    decimal? ActualGramsPerCutPiece, decimal? GramsVariance, decimal? VariancePercent);
public sealed record CuttingEvent(int Version, DateTimeOffset At, string? Actor, string Type);
public interface ICuttingStore
{
    Task<object> OptionsAsync(CancellationToken ct);
    Task<IReadOnlyList<CuttingDocument>> ListAsync(CancellationToken ct);
    Task<CuttingDetail?> GetAsync(Guid id,CancellationToken ct);
    Task<IReadOnlyList<CuttingEvent>> EventsAsync(Guid id,CancellationToken ct);
    Task<CuttingReport> ReportAsync(CuttingReportFilter filter,Guid? recordId,CancellationToken ct);
    Task<CuttingDetail> SaveAsync(Guid id,SaveCuttingDocument request,int? expectedVersion,bool create,string? actor,CancellationToken ct);
}
