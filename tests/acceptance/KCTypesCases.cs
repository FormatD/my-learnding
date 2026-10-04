using Learning;
using System.Text.Json;
public static class KCTypesCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static void Run()
    {
        var original=Content.Fixture();string[] types=["Concept","Procedure","Representation","Strategy","Application","Expression","Misconception"];
        foreach(var type in types)
        {
            var catalog=original with{Kcs=original.Kcs.Select(k=>k with{Type=type}).ToArray()};Check(Content.Validate(catalog).Length==0,"valid ability classification rejected: "+type);
            Check(catalog.Questions.SequenceEqual(original.Questions),"classification changed question rules");
        }
        foreach(var type in new string?[]{null,"strategy","Unknown",""})Check(!KCTypes.ValidDefinition(type),"unknown classification accepted");
        var changed=original.Kcs[0] with{Type="Strategy"};Check(Content.MeasurementSignature(changed)!=Content.MeasurementSignature(original.Kcs[0]),"new classification silently reused measurement identity");
        using var schema=JsonDocument.Parse(BuilderProtocol.Schema);var recordedTypes=schema.RootElement.GetProperty("properties").GetProperty("candidates").GetProperty("items").GetProperty("properties").GetProperty("kcType").GetProperty("enum").EnumerateArray().Select(e=>e.GetString()).ToArray();
        Check(recordedTypes.SequenceEqual(new[]{"Procedure","Concept","Application","Representation","Misconception"}),"candidate/1 schema broadened without a new version");
        var fragment=new BuilderFragment(Guid.NewGuid(),"原来源");var candidate=new BuilderCandidateOutput("能力","MATH","Strategy",3,3,"独立选择顺序","不含建模",[fragment.Id],[fragment.Text],.5m);
        try{BuilderProtocol.Validate(Json.Write(new BuilderEnvelope("kc-candidate/1",[candidate])),[fragment]);throw new Exception("old candidate protocol accepted unrecorded type");}catch(ApiError e){Check(e.Code=="BUILDER_SCHEMA_INVALID","wrong legacy schema rejection");}
    }
}
