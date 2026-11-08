using MiniErp.Application.Abstractions.Common;

namespace MiniErp.Application.Abstractions.Queries;

/// <summary>Marker for an application query that returns a value.</summary>
public interface IQuery<out TResult> : IApplicationRequest
{
}
