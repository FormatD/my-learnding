using Learning;
public static class AssessmentStateDeltaCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static void Run()
    {
        var values=new[]{"{\"text\":\"混合𠀀<>&\\\"\\\\\",\"decimal\":1.00,\"array\":[1,null,{\"a\":2}],\"obj\":{\"x\":3}}","{\"text\":null,\"decimal\":1.0,\"array\":[1,null,{\"a\":4},5],\"obj\":{\"y\":6}}","{\"array\":[],\"other\":false}","{}"};
        values=values.Select(v=>Json.Write(System.Text.Json.Nodes.JsonNode.Parse(v))).ToArray();
        values=[..values,Json.Write(new{createdAt=new DateTimeOffset(2026,1,1,8,0,0,TimeSpan.FromHours(8)),amount=1.00m})];
        foreach(var a in values)foreach(var b in values)Check(AssessmentStateDeltas.Apply(a,AssessmentStateDeltas.Create(a,b))==b,"delta changed exact canonical JSON bytes");
        foreach(var invalid in new[]{"{}","{\"version\":\"future\",\"changes\":[]}","{\"version\":\"assessment-state-delta/1\",\"changes\":[{\"path\":[\"absent\"],\"value\":\"1\"}]}","{\"version\":\"assessment-state-delta/1\",\"changes\":[{\"path\":[\"array\"],\"value\":\"[]\",\"retain\":9}]}"})
        {try{AssessmentStateDeltas.Apply(values[0],invalid);throw new Exception("invalid delta accepted");}catch(ApiError error){Check(error.Code=="INCREMENTAL_STATE_INVALID","wrong delta failure");}}
    }
}
