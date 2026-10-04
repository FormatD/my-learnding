using Learning;
using Microsoft.Extensions.Configuration;
public static class KCMetadataCases
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static void Run()
    {
        var legacy=new KC(Guid.NewGuid(),Guid.NewGuid(),"MATH.G3.LEGACY","混合运算","独立计算","不含建模");var old=Json.Write(legacy);Check(!old.Contains("subject") && !old.Contains("gradeMin") && !old.Contains("domain") && !old.Contains("difficultyLevel") && !old.Contains("cognitiveLevel") && Json.Write(Json.Read<KC>(old))==old,"legacy JSON shape or hash changed");Check(KCMetadata.Valid(legacy),"legacy unknown rejected");
        var math=legacy with{Id=Guid.NewGuid(),Subject="MATH",GradeMin=2,GradeMax=6};Check(KCMetadata.Valid(math) && Content.MeasurementSignature(math)==Content.MeasurementSignature(legacy),"definition metadata changed mathematical signature");
        foreach(var invalid in new[]{math with{Subject="Math"},math with{Subject=null},math with{GradeMin=0},math with{GradeMax=13},math with{GradeMin=7},math with{GradeMin=null},legacy with{GradeMin=3}})Check(!KCMetadata.Valid(invalid),"invalid subject/range accepted");
        var described=math with{Domain="数与运算",DifficultyLevel="Hard",CognitiveLevel="理解与应用"};Check(KCMetadata.Valid(described) && Content.MeasurementSignature(described)==Content.MeasurementSignature(math),"descriptions changed evidence signature");
        Check(Json.Read<KC>(Json.Write(described))==described,"descriptions did not roundtrip");
        foreach(var invalid in new[]{described with{Domain=" "},described with{Domain=new string('x',201)},described with{Domain="数\n运算"},described with{Domain=" 数"},described with{CognitiveLevel=new string('x',101)},described with{CognitiveLevel="\u0085"},described with{DifficultyLevel="hard"}})Check(!KCMetadata.Valid(invalid),"invalid descriptor accepted");
        var sample=Content.Fixture();var enriched=sample with{Kcs=sample.Kcs.Select(k=>k with{Domain="数与运算",DifficultyLevel="Hard",CognitiveLevel="理解与应用"}).ToArray()};Check(Content.Validate(enriched).Length==0 && sample.Questions.SequenceEqual(enriched.Questions),"descriptors changed questions or validation");
        var profile=BuilderConfiguration.Current(new ConfigurationBuilder().Build()).Retrieval!;Check(profile.Version=="builder-retrieval/4" && profile.SubjectPolicy=="ExactRecordedSubject" && profile.GradePolicy=="NoGradeExclusion","new subject policy not fixed");
        var other=math with{Id=Guid.NewGuid(),Subject="ENGLISH"};var crossGrade=math with{Id=Guid.NewGuid(),GradeMin=10,GradeMax=12};var candidate=new BuilderCandidateOutput(math.Name,"MATH",math.Type,3,3,math.Behavior,math.Boundary,[Guid.NewGuid()],["来源"],.5m);
        var matches=Retrieval.Candidates(candidate,[legacy,math,other,crossGrade],profile);Check(matches.Length==2 && matches.Any(m=>m.KCId==crossGrade.Id) && matches.All(m=>m.KCId!=legacy.Id && m.KCId!=other.Id),"subject exclusion or grade breadth incorrect");
        var alias=new BuilderAliasSnapshot(Guid.NewGuid(),other.Id,math.Name,Retrieval.Normalize(math.Name),Guid.NewGuid(),Guid.NewGuid());Check(Retrieval.Candidates(candidate,[legacy,math,other],profile with{Aliases=[alias]}).Single().KCId==math.Id,"alias bypassed recorded subject");
        var original=profile with{Version="builder-retrieval/3",SubjectPolicy=null,GradePolicy=null};original.Validate();Check(!Json.Write(original).Contains("subjectPolicy") && Retrieval.Candidates(candidate,[legacy,math,other,crossGrade],original).Length==4,"old lexical profile reinterpreted subject");
        foreach(var invalid in new[]{profile with{SubjectPolicy=null},profile with{GradePolicy="ExactGrade"},original with{SubjectPolicy="ExactRecordedSubject"}}){try{invalid.Validate();throw new Exception("invalid fixed subject policy accepted");}catch(ApiError e){Check(e.Code=="BUILDER_CONFIGURATION_INVALID","wrong fixed subject policy rejection");}}
    }
}
