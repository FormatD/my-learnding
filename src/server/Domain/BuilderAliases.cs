using Microsoft.EntityFrameworkCore;
namespace Learning;
public static class BuilderAliases
{
    static BuilderAliasSnapshot Snapshot(Alias alias)=>new(alias.Id,alias.KCId,alias.Text,alias.Normalized,alias.CandidateId,alias.ReviewedBy);
    static IQueryable<Alias> Eligible(Database db,Guid family,Guid[] kcIds)=>from alias in db.Set<Alias>() join candidate in db.Candidates on alias.CandidateId equals candidate.Id where alias.FamilyId==family && candidate.FamilyId==family && candidate.Status=="Accepted" && candidate.ExistingKCId==alias.KCId && kcIds.Contains(alias.KCId) select alias;
    public static async Task<BuilderAliasSnapshot[]> Capture(Database db,Guid family,Catalog? catalog,CancellationToken ct=default)
    {
        if(catalog==null)return [];
        var rows=await Eligible(db,family,catalog.Kcs.Select(k=>k.Id).ToArray()).AsNoTracking().Take(1001).ToArrayAsync(ct);
        if(rows.Length>1000)throw new ApiError(422,"BUILDER_ALIAS_LIMIT","当前正式能力的审核别名超过1000项，请核对范围后再准备任务。");
        return rows.Select(Snapshot).OrderBy(a=>a.KCId).ThenBy(a=>a.Normalized,StringComparer.Ordinal).ThenBy(a=>a.Id).ToArray();
    }
    public static async Task Validate(Database db,BuilderRun run,Catalog? catalog,BuilderRetrievalConfiguration? configuration,CancellationToken ct=default)
    {
        if(configuration?.Aliases is not {Length:>0} aliases)return;
        var ids=aliases.Select(a=>a.Id).ToArray();var rows=await Eligible(db,run.FamilyId,catalog?.Kcs.Select(k=>k.Id).ToArray()??[]).Where(a=>ids.Contains(a.Id)).AsNoTracking().ToArrayAsync(ct);
        if(rows.Length!=aliases.Length || aliases.Any(a=>!rows.Any(row=>Snapshot(row)==a)))throw new ApiError(422,"BUILDER_ALIAS_SNAPSHOT_CHANGED","原审核别名或其关联来源已变化，请核对原请求；不采用后来新增的别名。");
    }
}
