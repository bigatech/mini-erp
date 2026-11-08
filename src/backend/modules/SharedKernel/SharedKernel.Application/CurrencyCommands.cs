using MiniErp.Application.Abstractions.Commands;
using MiniErp.Application.Abstractions.Queries;
using SharedKernel.Domain;

namespace SharedKernel.Application;

public sealed record CurrencyDto(string Code, string Name);
public sealed record CreateCurrencyCommand(string Code, string Name) : ICommand<CurrencyDto>;
public sealed record ListCurrenciesQuery : IQuery<IReadOnlyList<CurrencyDto>>;

public sealed class DuplicateCurrencyCodeException(string code)
    : Exception($"Currency code '{code}' already exists.")
{
    public string Code { get; } = code;
}

public interface ICurrencyRepository
{
    /// <summary>
    /// Atomically inserts a currency only when its normalized code is absent.
    /// Returns false only for an existing code; other failures must throw.
    /// An adapter must enforce code uniqueness at its storage boundary, rather
    /// than implementing a separate existence read followed by an insert.
    /// </summary>
    Task<bool> TryCreateAsync(Currency currency, CancellationToken cancellationToken);
    Task<IReadOnlyList<Currency>> ListAsync(CancellationToken cancellationToken);
}

public sealed class CreateCurrencyHandler(ICurrencyRepository repository)
{
    public async Task<CurrencyDto> Handle(CreateCurrencyCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currency = Currency.Create(command.Code, command.Name);
        if (!await repository.TryCreateAsync(currency, cancellationToken))
            throw new DuplicateCurrencyCodeException(currency.Code);
        return new(currency.Code, currency.Name);
    }
}

public sealed class ListCurrenciesHandler(ICurrencyRepository repository)
{
    public async Task<IReadOnlyList<CurrencyDto>> Handle(ListCurrenciesQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await repository.ListAsync(cancellationToken))
            .Select(currency => new CurrencyDto(currency.Code, currency.Name)).ToArray();
    }
}
