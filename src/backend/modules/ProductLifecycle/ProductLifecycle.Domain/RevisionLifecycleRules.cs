namespace ProductLifecycle.Domain;

public sealed class RevisionStateConflictException : Exception { }
public static class RevisionLifecycleRules
{
    public static string Next(string state,string action)=>(state,action) switch
    {
        ("Draft","submit")=>"PendingApproval",
        ("PendingApproval","approve")=>"Approved",
        ("PendingApproval","reject")=>"Draft",
        _=>throw new RevisionStateConflictException()
    };
    public static void Clone(string state)
    {
        if(state!="Approved")throw new RevisionStateConflictException();
    }
}
