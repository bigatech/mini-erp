using System.Collections.Concurrent;
using SharedKernel.Application;
using SharedKernel.Domain;
using Xunit;

namespace MiniErp.SharedKernel.Tests;

public sealed class CurrencyTests
{
    [Fact] public void Currency_normalizes_ascii_identifier_and_trims_name()
    {
        var currency = Currency.Create(" try ", " Türk Lirası ");
        Assert.Equal("TRY", currency.Code);
        Assert.Equal("Türk Lirası", currency.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("TR")]
    [InlineData("TRY1")]
    [InlineData("T1Y")]
    [InlineData("T R")]
    [InlineData("ıry")]
    [InlineData("ＴＲＹ")]
    public void Currency_rejects_invalid_or_non_ascii_identifiers(string? code)
    {
        Assert.Equal("code", Assert.Throws<ArgumentException>(() => Currency.Create(code!, "Name")).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("bad\0name")]
    public void Currency_requires_a_name(string? name)
    {
        Assert.Equal("name", Assert.Throws<ArgumentException>(() => Currency.Create("TRY", name!)).ParamName);
    }

    [Fact] public async Task Invalid_currency_is_rejected_before_repository_write()
    {
        var repository = new MemoryRepository();
        await Assert.ThrowsAsync<ArgumentException>(() => new CreateCurrencyHandler(repository).Handle(new("TR1", "Name"), default));
        Assert.Equal(0, repository.CreateCalls);
    }

    [Fact] public async Task Duplicate_normalized_code_does_not_replace_existing_currency()
    {
        var repository = new MemoryRepository();
        var handler = new CreateCurrencyHandler(repository);
        await handler.Handle(new("try", "Türk Lirası"), default);
        var error = await Assert.ThrowsAsync<DuplicateCurrencyCodeException>(() => handler.Handle(new(" TRY ", "Replacement"), default));
        Assert.Equal("TRY", error.Code);
        var currencies = await new ListCurrenciesHandler(repository).Handle(new(), default);
        Assert.Equal(new CurrencyDto("TRY", "Türk Lirası"), Assert.Single(currencies));
    }

    [Fact] public async Task Concurrent_create_uses_atomic_repository_result_without_read_before_write()
    {
        var repository = new MemoryRepository();
        var handler = new CreateCurrencyHandler(repository);
        var attempts = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            try { await handler.Handle(new("EUR", "Euro"), default); return true; }
            catch (DuplicateCurrencyCodeException) { return false; }
        }));
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(created => created));
        Assert.Equal(16, repository.CreateCalls);
        Assert.Equal(0, repository.ListCalls);
        Assert.Single(await repository.ListAsync(default));
    }

    [Fact] public async Task Canceled_commands_and_queries_do_not_reach_repository()
    {
        var repository = new MemoryRepository();
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateCurrencyHandler(repository).Handle(new("TRY", "Türk Lirası"), source.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ListCurrenciesHandler(repository).Handle(new(), source.Token));
        Assert.Equal(0, repository.CreateCalls); Assert.Equal(0, repository.ListCalls);
    }

    // Test double only. The future persistence adapter needs its own integration
    // test for its atomic uniqueness guarantee; this does not prove PostgreSQL behavior.
    private sealed class MemoryRepository : ICurrencyRepository
    {
        private readonly ConcurrentDictionary<string, Currency> currencies = new(StringComparer.Ordinal);
        private int createCalls;
        public int CreateCalls => createCalls;
        public int ListCalls { get; private set; }

        public Task<bool> TryCreateAsync(Currency currency, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref createCalls);
            return Task.FromResult(currencies.TryAdd(currency.Code, currency));
        }

        public Task<IReadOnlyList<Currency>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListCalls++;
            return Task.FromResult<IReadOnlyList<Currency>>(currencies.Values.ToArray());
        }
    }
}
