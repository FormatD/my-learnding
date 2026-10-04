namespace Learning;
public static class KCMetadata
{
    public static readonly string[] Subjects=["MATH","ENGLISH","CHINESE","SCIENCE","OTHER"];
    public static bool Valid(KC k)=>k.Subject==null && k.GradeMin==null && k.GradeMax==null || k.Subject!=null && Subjects.Contains(k.Subject) && k.GradeMin is >=1 and <=12 && k.GradeMax is >=1 and <=12 && k.GradeMin<=k.GradeMax;
}
