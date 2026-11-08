using ProductLifecycle.Domain;
namespace ProductLifecycle.Application;

public interface IRevisionLifecycleRepository
{
    Task<RevisionDto> TransitionAsync(Guid revisionId,string action,CancellationToken ct);
    Task<RevisionDto> CloneAsync(Guid revisionId,string newNumber,CancellationToken ct);
}
public sealed class RevisionLifecycleHandler(IRevisionLifecycleRepository repository)
{
    public Task<RevisionDto> Transition(Guid id,string action,CancellationToken ct)=>repository.TransitionAsync(id,action,ct);
    public Task<RevisionDto> Clone(Guid id,string number,CancellationToken ct)=>repository.CloneAsync(id,PlmDraftRules.Required(number,"revisionNumber"),ct);
}
