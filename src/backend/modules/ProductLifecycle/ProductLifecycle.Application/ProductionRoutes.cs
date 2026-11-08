using ProductLifecycle.Domain;
namespace ProductLifecycle.Application;

public sealed record OperationDto(Guid Id, string Code, string Name, decimal StandardDuration, string DurationUnit);
public sealed record ProductionRouteDto(Guid Id, Guid RevisionId, Guid OperationId, string OperationCode, string OperationName, int Sequence, decimal StandardDuration, string DurationUnit);
public sealed class DuplicateRouteEntryException : Exception { }
public sealed class RouteReferenceNotFoundException : Exception { }
public sealed class RouteRevisionLockedException : Exception { }
public interface IProductionRouteRepository
{
    Task<IReadOnlyList<OperationDto>> OperationsAsync(CancellationToken ct);
    Task<OperationDto> CreateOperationAsync(string code, string name, decimal duration, string unit, CancellationToken ct);
    Task<IReadOnlyList<ProductionRouteDto>> RouteAsync(Guid revisionId, CancellationToken ct);
    Task<ProductionRouteDto> AddStepAsync(Guid revisionId, Guid operationId, int sequence, CancellationToken ct);
}
public sealed class ProductionRouteHandler(IProductionRouteRepository repository)
{
    public Task<OperationDto> CreateOperation(string code, string name, decimal duration, string unit, CancellationToken ct)
    {
        var validatedUnit = ProductionRouteRules.Duration(duration, unit);
        return repository.CreateOperationAsync(PlmDraftRules.Required(code, "code"), PlmDraftRules.Required(name, "name"), duration, validatedUnit, ct);
    }
    public Task<ProductionRouteDto> AddStep(Guid revisionId, Guid operationId, int sequence, CancellationToken ct)
    {
        ProductionRouteRules.Sequence(sequence);
        if (operationId == Guid.Empty) throw new ArgumentException("Operasyon seçin.", "operationId");
        return repository.AddStepAsync(revisionId, operationId, sequence, ct);
    }
}
