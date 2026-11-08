using DataIntakeAnalytics.Application;
using Xunit;

namespace MiniErp.Analytics.Tests;

public sealed class WorksheetInputTests
{
    private static readonly WorksheetInputs Empty = new(null, null, null, null, null, null, null, null, null);
    [Fact] public void Missing_values_stay_distinct_from_zero()
    {
        Assert.Null(Empty.Validate());
        var zero = Empty with { ActualProduced = 0, FabricUsedKg = 0 };
        Assert.Null(zero.Validate());
        Assert.Null(Empty.ActualProduced);
        Assert.Equal(0, zero.ActualProduced);
    }
    [Fact] public void Counts_require_whole_nonnegative_values_but_measurements_allow_fractions()
    {
        Assert.NotNull((Empty with { ActualProduced = -1 }).Validate());
        Assert.NotNull((Empty with { QualityChecked = 0.5m }).Validate());
        Assert.Null((Empty with { WorkedHours = 1.5m, FabricUsedKg = 0.25m }).Validate());
    }
    [Theory] [InlineData("TRY")] [InlineData("EUR")] [InlineData("")]
    public void Explicit_currency_is_accepted(string currency) => Assert.Null((Empty with { Currency = currency }).Validate());
    [Fact] public void Invalid_currency_and_oversized_notes_are_rejected()
    {
        Assert.NotNull((Empty with { Currency = "try" }).Validate());
        Assert.NotNull((Empty with { Notes = new string('a', 4001) }).Validate());
    }
}
