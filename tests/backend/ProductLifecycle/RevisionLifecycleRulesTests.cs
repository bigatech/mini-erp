using ProductLifecycle.Domain;
using Xunit;
namespace MiniErp.ProductLifecycle.Tests;

public sealed class RevisionLifecycleRulesTests
{
    [Theory][InlineData("Draft","submit","PendingApproval")][InlineData("PendingApproval","approve","Approved")][InlineData("PendingApproval","reject","Draft")]
    public void Explicit_allowed_transition_keeps_contract(string from,string action,string to)=>Assert.Equal(to,RevisionLifecycleRules.Next(from,action));
    [Theory][InlineData("Draft","approve")][InlineData("Approved","submit")][InlineData("Approved","reject")][InlineData("PendingApproval","submit")]
    public void Unsupported_transition_is_rejected(string from,string action)=>Assert.Throws<RevisionStateConflictException>(()=>RevisionLifecycleRules.Next(from,action));
    [Theory][InlineData("Draft")][InlineData("PendingApproval")]
    public void Only_approved_revision_may_be_cloned(string state)=>Assert.Throws<RevisionStateConflictException>(()=>RevisionLifecycleRules.Clone(state));
    [Fact]public void Approved_revision_can_be_cloned()=>RevisionLifecycleRules.Clone("Approved");
}
