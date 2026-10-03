using Learning;
public static class BuilderBudgetCases
{
    static void Check(bool v){if(!v)throw new Exception("budget invariant failed");}
    public static void Calculate()
    {
        var day=new DateOnly(2026,10,3);var known=new BuilderCall{Status="Returned",BillingStatus="Confirmed",Currency="USD",ChargedCost=.4m,InputTokens=10,OutputTokens=20,BudgetDay=day,BudgetState="Settled"};
        var pending=new BuilderCall{Status="Started",ReservedCost=.5m,ReservedTokens=60,BudgetDay=day.AddDays(-1)};
        var denied=new BuilderCall{Status="Denied",ReservedCost=99,ReservedTokens=99,BudgetDay=day};
        var old=new BuilderCall{Status="Returned",BillingStatus="Confirmed",Currency="USD",ChargedCost=.1m,InputTokens=5,OutputTokens=5,BudgetDay=day.AddDays(-1)};
        var state=BuilderBudget.Calculate([known,pending,denied,old],[],day);Check(state.CostCommitted==.9m && state.TokensCommitted==90 && state.ActiveCalls==1 && state.UnresolvedCalls==1);
        state=BuilderBudget.Calculate([known,pending,old],[new(){CallId=pending.Id,ChargedCost=.2m,Currency="USD",InputTokens=4,OutputTokens=6}],day);Check(state.CostCommitted==.4m && state.TokensCommitted==30 && state.ActiveCalls==0 && state.UnresolvedCalls==0);
        var legacy=new BuilderCall{Status="Returned"};state=BuilderBudget.Calculate([legacy],[],day);Check(state.UnboundedUnknownCalls==1 && state.UnresolvedCalls==1 && state.ActiveCalls==0);
        known.BudgetState="Overrun";state=BuilderBudget.Calculate([known],[],day);Check(state.OverrunCalls==1 && state.CostCommitted==.4m);
        BuilderQuote.Local.Validate();foreach(var q in new[]{new BuilderQuote(-1,0,"USD"),new BuilderQuote(.0000001m,0,"USD"),new BuilderQuote(0,-1,"USD"),new BuilderQuote(0,0,"EUR"),new BuilderQuote(1,0,null,true)}){try{q.Validate();throw new Exception("invalid quote accepted");}catch(ApiError e){Check(e.Code=="BUILDER_QUOTE_INVALID");}}
    }
}
