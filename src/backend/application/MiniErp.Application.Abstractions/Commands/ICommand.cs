using MiniErp.Application.Abstractions.Common;

namespace MiniErp.Application.Abstractions.Commands;

/// <summary>Marker for an application command that does not return a value.</summary>
public interface ICommand : IApplicationRequest
{
}

/// <summary>Marker for an application command that returns a value.</summary>
public interface ICommand<out TResult> : IApplicationRequest
{
}
