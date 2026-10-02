using Microsoft.EntityFrameworkCore;
namespace Learning;
public static class CatalogDirectory
{
    public static IEnumerable<string> Validate(Catalog c)
    {
        var books=c.Textbooks??[];var units=c.Units??[];var courses=c.Courses??[];
        var directoryIds=books.Select(b=>b.Id).Concat(units.Select(u=>u.Id)).Concat(courses.Select(course=>course.Id)).Concat(c.Lessons.Select(l=>l.Id)).Concat(c.Kcs.Select(k=>k.Id)).Concat(c.Questions.Select(q=>q.Id)).ToArray();
        if(directoryIds.Any(id=>id==Guid.Empty) || directoryIds.Distinct().Count()!=directoryIds.Length)yield return "教材、单元和课程身份必须非空且唯一";
        var revisions=books.Select(b=>b.RevisionId).Concat(units.Select(u=>u.RevisionId)).Concat(courses.Select(course=>course.RevisionId)).Concat(c.Lessons.Where(l=>l.RevisionId!=null).Select(l=>l.RevisionId!.Value)).Concat(c.Kcs.Select(k=>k.RevisionId)).Concat(c.Questions.Select(q=>q.RevisionId)).ToArray();
        if(revisions.Any(id=>id==Guid.Empty) || revisions.Distinct().Count()!=revisions.Length)yield return "内容修订身份必须非空且唯一";
        foreach(var b in books)if(b.RevisionId==Guid.Empty || string.IsNullOrWhiteSpace(b.Publisher) || string.IsNullOrWhiteSpace(b.Edition) || string.IsNullOrWhiteSpace(b.Subject) || b.Grade is <1 or >12 || string.IsNullOrWhiteSpace(b.Semester) || b.SourceId==Guid.Empty)yield return "教材需要版本身份、出版社、明确版本说明、学科、年级和学期";
        foreach(var u in units)if(u.RevisionId==Guid.Empty || !books.Any(b=>b.Id==u.TextbookId) || string.IsNullOrWhiteSpace(u.Title) || u.Sequence<1)yield return "单元需要修订身份、有效教材、名称和顺序";
        if(units.GroupBy(u=>new{u.TextbookId,u.Sequence}).Any(g=>g.Count()>1))yield return "同一本教材的单元顺序不能重复";
        foreach(var course in courses)if(course.RevisionId==Guid.Empty || string.IsNullOrWhiteSpace(course.Provider) || string.IsNullOrWhiteSpace(course.Subject) || string.IsNullOrWhiteSpace(course.Title) || (course.SourceRefs??[]).Contains(Guid.Empty))yield return "课程需要修订身份、提供方、学科、名称和有效来源引用";
        foreach(var l in c.Lessons)
        {
            if(l.UnitId!=null && l.CourseId!=null || l.UnitId!=null && !units.Any(u=>u.Id==l.UnitId) || l.CourseId!=null && !courses.Any(course=>course.Id==l.CourseId))yield return $"{l.Title}: 课时只能归属一个有效教材单元或课程";
            if(l.RevisionId==Guid.Empty || (l.UnitId!=null || l.CourseId!=null || (l.SourceRefs??[]).Length>0) && l.RevisionId==null || l.EstimatedMinutes is <1 or >180 || (l.SourceRefs??[]).Contains(Guid.Empty))yield return $"{l.Title}: 课时需要有效修订、预计时长和来源引用";
        }
        if(c.Lessons.GroupBy(l=>new{l.UnitId,l.CourseId,l.Sequence}).Any(g=>g.Count()>1))yield return "同一单元或课程的课时顺序不能重复";
    }
    public static async Task ValidateSources(Database db,Guid family,Catalog c)
    {
        var sourceIds=(c.Textbooks??[]).Where(b=>b.SourceId!=null).Select(b=>b.SourceId!.Value).Concat((c.Courses??[]).SelectMany(course=>course.SourceRefs??[])).Concat(c.Lessons.SelectMany(l=>l.SourceRefs??[])).Distinct().ToArray();
        if(sourceIds.Length>0 && await db.Sources.CountAsync(s=>s.FamilyId==family && sourceIds.Contains(s.Id))!=sourceIds.Length)throw new ApiError(422,"DIRECTORY_SOURCE_UNAVAILABLE","教材、课程和课时来源必须属于当前家庭，不能引用未知来源。");
    }
}
