namespace Learning;
public static class KCMetadata
{
    public static readonly string[] Subjects=["MATH","ENGLISH","CHINESE","SCIENCE","OTHER"];
    public static readonly string[] DifficultyLevels=["Easy","Medium","Hard"];
    public static bool SubjectGradeValid(KC k)=>k.Subject==null && k.GradeMin==null && k.GradeMax==null || k.Subject!=null && Subjects.Contains(k.Subject) && k.GradeMin is >=1 and <=12 && k.GradeMax is >=1 and <=12 && k.GradeMin<=k.GradeMax;
    static bool Description(string? value,int max)=>value==null || !string.IsNullOrWhiteSpace(value) && value.Length<=max && value==value.Trim() && !value.Any(char.IsControl);
    public static bool DescriptionsValid(KC k)=>Description(k.Domain,200) && Description(k.CognitiveLevel,100) && (k.DifficultyLevel==null || DifficultyLevels.Contains(k.DifficultyLevel));
    public static bool Valid(KC k)=>SubjectGradeValid(k) && DescriptionsValid(k);
}
