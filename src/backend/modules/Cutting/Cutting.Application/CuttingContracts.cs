namespace Cutting.Application;

public sealed record CuttingDocument(Guid Id, Guid OrderId, int OrderVersion, string Number, string Status, int Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<CuttingGroup> Groups, IReadOnlyList<CuttingOrderLine>? OrderSnapshot = null);
public sealed record SaveCuttingDocument(Guid OrderId, int OrderVersion, string Status, IReadOnlyList<CuttingGroup> Groups, bool AcceptOrderChanges = false);
public sealed record CuttingGroup(Guid Id, Guid MaterialId, string Unit, string? WorkOrderNumber, string? ApprovalNumber,
    decimal? FabricKg, decimal? LiningM, IReadOnlyList<CuttingPlan> Plans);
public sealed record CuttingPlan(Guid Id, string Number, decimal? Layers, decimal? LengthM, decimal? WidthM,
    decimal? PlannedConsumption, DateTimeOffset? PlannedStart, DateTimeOffset? PlannedEnd, decimal? PlannedMinutes,
    decimal? PatternAreaM2, decimal? UsableAreaM2, IReadOnlyList<CuttingPlanLine> Lines, IReadOnlyList<CuttingActual> Actuals, decimal? PlannedWeightKg = null);
public sealed record CuttingPlanLine(Guid VariantOptionId, Guid SizeOptionId, Guid? SetPartOptionId, decimal PiecesPerLayer);
public sealed record CuttingActual(Guid Id, DateOnly Date, decimal? Layers, decimal? GrossConsumption,
    decimal? ReturnedConsumption, decimal? ReusableConsumption, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    decimal? DowntimeMinutes, string? DowntimeReason, IReadOnlyList<CuttingActualLine> Lines, decimal? NetWeightKg = null);
public sealed record CuttingActualLine(Guid VariantOptionId, Guid SizeOptionId, Guid? SetPartOptionId,
    decimal CutQuantity, decimal? GoodQuantity, decimal? DefectQuantity);
public sealed record CuttingOrderLine(Guid VariantOptionId, Guid SizeOptionId, Guid? SetPartOptionId, decimal Quantity);
public sealed record CuttingReportFilter(Guid? OrderId = null, Guid? MaterialId = null, string? Status = null,
    DateOnly? From = null, DateOnly? To = null, Guid? VariantOptionId = null, Guid? SizeOptionId = null);
public sealed record CuttingQuantityReport(Guid OrderId, Guid VariantOptionId, Guid SizeOptionId, Guid? SetPartOptionId,
    decimal? OrderQuantity, decimal? PlannedQuantity, decimal CutQuantity, decimal? GoodQuantity, decimal? DefectQuantity,
    decimal? QuantityVariance, decimal? QuantityVariancePercent, decimal? FulfillmentPercent,
    decimal? RemainingQuantity, decimal? ExcessQuantity, decimal? DefectPercent);
public sealed record CuttingMaterialReport(Guid MaterialId, string Unit, decimal? PlannedConsumption,
    decimal? NetConsumption, decimal? ConsumptionVariance, decimal? ConsumptionVariancePercent,
    decimal? PlannedWeightKg = null, decimal? NetWeightKg = null, decimal? PlannedQuantity = null, decimal? CutQuantity = null,
    decimal? PlannedGramsPerPiece = null, decimal? ActualGramsPerCutPiece = null, decimal? GramsVariance = null, decimal? WeightVariancePercent = null);
public sealed record CuttingProcessReport(decimal? PlannedMinutes, decimal? ActualNetMinutes, decimal? DurationVarianceMinutes,
    decimal? GoodPerHour, decimal? TimeEfficiencyPercent, decimal? LayoutEfficiencyPercent);
public sealed record CuttingReport(IReadOnlyList<CuttingQuantityReport> Rows, CuttingQuantityReport Totals,
    IReadOnlyList<CuttingMaterialReport> Materials, CuttingProcessReport Process, IReadOnlyList<string> Warnings);
public sealed class CuttingValidationException(IReadOnlyDictionary<string,string[]> errors) : Exception("Kesim girdilerini kontrol edin.")
{ public IReadOnlyDictionary<string,string[]> Errors { get; } = errors; }
