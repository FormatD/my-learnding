using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace Learning;
public record BuilderStageDefinition(string Id,string Title,string Notice);
public record BuilderStageTask
{
    public Guid Id {get;init;}
    public string Title {get;init;}="";
    public string Status {get;init;}="";
    public string Provider {get;init;}="";
    public DateTimeOffset CreatedAt {get;init;}
}
public record BuilderStageCount(string Id,string Title,string Notice,int Total);
public record BuilderStageTasks(string Stage,int Page,int PageSize,int Total,BuilderStageTask[] Items);
public record BuilderStageData(string Id,string Title,int Page,int PageSize,int Total,JsonElement[] Items);
public record BuilderStageDetail(string Stage,Guid Id,JsonElement Record,BuilderStageData[] Data);
public static class BuilderStageApi
{
    static readonly string[] ContentAuditActions=["BuilderManualRetry","BuilderSemanticPreparationQueued","BuilderSemanticPreparationManualRetry","BuilderSemanticSuggestionPrepared","CandidateReview","ContentSnapshotReviewed","MappingSuggestionsPrepared","MappingSuggestionsReviewed","MappingPreparationQueued","MappingPreparationManualRetry","IndependentMappingDraftCreated","ReleaseWithdrawal","BackgroundJobCancellationRequested","BackgroundJobCancellationObserved"];
    static readonly BuilderStageDefinition[] Stages=[
        new("sources","1 · 来源与分段","文本导入同步完成，展示真实来源记录；不会补造后台任务。"),
        new("parsing","2 · PDF 解析","仅展示已保存的解析任务；另行识别后导入的文本仍在来源阶段。"),
        new("extraction","3 · 候选生成与召回","包含本机模型和模拟运行，召回数据保存在各条候选中。"),
        new("indexes","4 · 向量索引","仅展示实际保存的索引；当前真实模型索引尚未接入生产。"),
        new("semantic","5 · 语义建议","展示每份固定准备及实际后台任务、调用和原返回，不代替人工审核。"),
        new("review","6 · 候选人工审核","每条候选是一项待审或已审工作，未处理的候选仍显示待审核。"),
        new("drafts","7 · 内容草稿","包含家庭全部真实草稿；生成草稿不代表已发布。"),
        new("mapping","8 · 映射准备","展示全部后台准备，不限于最近50条。"),
        new("mapping-review","9 · 映射建议与审核","含本机模型、模拟与手工维护批次，各项原建议和审核决定可查看。"),
        new("publication","10 · 正式发布","只展示实际发布和审核依据；没有发布则不补造完成记录。")];
    static IQueryable<BuilderStageTask> Query(Database db,Guid f,string stage)=>stage switch
    {
        "sources"=>db.Sources.Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title=x.Title,Status=x.Text==""?"NoText":"Imported",Provider="Local",CreatedAt=x.CreatedAt}),
        "parsing"=>db.BuilderRuns.Where(x=>x.FamilyId==f && x.Type=="ParsePDF").Select(x=>new BuilderStageTask{Id=x.Id,Title=db.Sources.Where(s=>s.Id==x.SourceId && s.FamilyId==f).Select(s=>s.Title).FirstOrDefault()??"原来源",Status=x.Status,Provider=x.Provider,CreatedAt=x.CreatedAt}),
        "extraction"=>db.BuilderRuns.Where(x=>x.FamilyId==f && x.Type=="Candidates").Select(x=>new BuilderStageTask{Id=x.Id,Title=db.Sources.Where(s=>s.Id==x.SourceId && s.FamilyId==f).Select(s=>s.Title).FirstOrDefault()??"原来源",Status=x.Status,Provider=x.Provider,CreatedAt=x.CreatedAt}),
        "indexes"=>db.Set<ModelEmbeddingIndex>().Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title="完整能力向量索引",Status="Saved",Provider="LocalOmlx",CreatedAt=x.CreatedAt}),
        "semantic"=>from x in db.Set<BuilderSemanticPreparation>() join j in db.Set<BackgroundJob>() on x.JobId equals j.Id where x.FamilyId==f && j.FamilyId==f select new BuilderStageTask{Id=x.Id,Title=db.Candidates.Where(c=>c.Id==x.CandidateId && c.FamilyId==f).Select(c=>c.Name).FirstOrDefault()??"原候选",Status=j.Status,Provider="LocalOmlx",CreatedAt=x.CreatedAt},
        "review"=>db.Candidates.Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title=x.Name,Status=x.Status,Provider=db.BuilderRuns.Where(r=>r.Id==x.RunId && r.FamilyId==f).Select(r=>r.Provider).FirstOrDefault()??"Unknown",CreatedAt=x.CreatedAt}),
        "drafts"=>db.Drafts.Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title=x.Title,Status=x.Status,Provider="Content",CreatedAt=x.CreatedAt}),
        "mapping"=>from x in db.Set<MappingPreparation>() join j in db.Set<BackgroundJob>() on x.JobId equals j.Id where x.FamilyId==f && j.FamilyId==f select new BuilderStageTask{Id=x.Id,Title=x.SourceTitle,Status=j.Status,Provider="Preparation",CreatedAt=x.CreatedAt},
        "mapping-review"=>db.Set<MappingRun>().Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title=x.SourceTitle,Status=x.Status,Provider=x.Provider,CreatedAt=x.CreatedAt}),
        "publication"=>db.Releases.Where(x=>x.FamilyId==f).Select(x=>new BuilderStageTask{Id=x.Id,Title="正式内容版本 "+x.Number,Status=x.Withdrawn?"Withdrawn":"Published",Provider="Content",CreatedAt=x.CreatedAt}),
        _=>throw new ApiError(422,"INVALID_BUILDER_STAGE","请选择已支持的建库阶段。")
    };
    static int Page(int? page){var p=page??1;if(p is <1 or >100000)throw new ApiError(422,"INVALID_PAGE","请使用有效页码。");return p;}
    static JsonElement Element<T>(T value)=>JsonSerializer.SerializeToElement(value,Json.Options);
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/builder/stages",async(Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");await using var snapshot=await ReadSnapshot.Begin(db,ctx);var rows=new List<BuilderStageCount>();foreach(var s in Stages)rows.Add(new(s.Id,s.Title,s.Notice,await Query(db,a.FamilyId,s.Id).CountAsync(ctx.RequestAborted)));await snapshot.CommitAsync(ctx.RequestAborted);return rows.ToArray();
        });
        api.MapGet("/builder/stages/{stage}/tasks",async(string stage,int? page,int? pageSize,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var p=Page(page);var size=pageSize??20;if(size is <1 or >50)throw new ApiError(422,"INVALID_PAGE","每页1至50条。");await using var snapshot=await ReadSnapshot.Begin(db,ctx);var q=Query(db,a.FamilyId,stage);var total=await q.CountAsync(ctx.RequestAborted);var items=await q.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((p-1)*size).Take(size).ToArrayAsync(ctx.RequestAborted);await snapshot.CommitAsync(ctx.RequestAborted);return new BuilderStageTasks(stage,p,size,total,items);
        });
        api.MapGet("/builder/stages/{stage}/tasks/{id:guid}",async(string stage,Guid id,string? related,int? relatedPage,Database db,HttpContext ctx)=>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var ct=ctx.RequestAborted;var p=Page(relatedPage);await using var snapshot=await ReadSnapshot.Begin(db,ctx);if(!await Query(db,a.FamilyId,stage).AnyAsync(x=>x.Id==id,ct))throw new ApiError(404,"NOT_FOUND","找不到本家庭此阶段记录。");
            var parts=new List<BuilderStageData>();JsonElement record=default;
            IQueryable<T> Owned<T>()where T:Row=>db.Set<T>().AsNoTracking().Where(x=>x.FamilyId==a.FamilyId);
            async Task<T> One<T>()where T:Row=>await Owned<T>().SingleAsync(x=>x.Id==id,ct);
            async Task Data<T>(string key,string title,IQueryable<T> q)where T:Row
            {
                var current=related==key?p:1;var total=await q.CountAsync(ct);var rows=await q.OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip((current-1)*20).Take(20).ToArrayAsync(ct);parts.Add(new(key,title,current,20,total,rows.Select(Element).ToArray()));
            }
            async Task Job(Guid inputRef,Guid? jobId=null)
            {
                var jobs=Owned<BackgroundJob>().Where(j=>jobId!=null?j.Id==jobId:j.InputRef==inputRef);await Data("jobs","后台任务与固定输入",jobs);await Data("leases","每次领取与中断记录",Owned<JobLeaseAttempt>().Where(l=>jobs.Select(j=>j.Id).Contains(l.JobId)));
            }
            switch(stage)
            {
                case "sources":
                    var source=await One<Source>();record=Element(source);await Data("chunks","实际来源片段",Owned<Chunk>().Where(c=>c.SourceId==id));await Data("runs","来源对应的解析与生成任务",Owned<BuilderRun>().Where(r=>r.SourceId==id));break;
                case "parsing":case "extraction":
                    var run=await One<BuilderRun>();record=Element(run);await Data("source","原来源（当前保存文本）",Owned<Source>().Where(s=>s.Id==run.SourceId));await Data("chunks","来源片段",Owned<Chunk>().Where(c=>c.SourceId==run.SourceId));await Data("attempts","每次处理尝试",Owned<BuilderAttempt>().Where(t=>t.RunId==id));await Data("candidates","原候选、召回及审核数据",Owned<Candidate>().Where(c=>c.RunId==id));await Data("calls","每次模型调用与用量",Owned<BuilderCall>().Where(c=>c.RunId==id));var builderCalls=Owned<BuilderCall>().Where(c=>c.RunId==id);await Data("call-reconciliations","调用结束核对依据",Owned<BuilderBudgetReconciliation>().Where(r=>builderCalls.Select(c=>c.Id).Contains(r.CallId)));if(run.LibraryReleaseId!=null)await Data("library","创建时选择的正式能力库",Owned<Release>().Where(r=>r.Id==run.LibraryReleaseId));await Job(id);break;
                case "indexes":record=Element(await One<ModelEmbeddingIndex>());break;
                case "semantic":
                    var semantic=await One<BuilderSemanticPreparation>();record=Element(semantic);await Data("candidate","原候选（当前审核状态）",Owned<Candidate>().Where(c=>c.Id==semantic.CandidateId));await Data("suggestions","原语义建议",Owned<BuilderSemanticSuggestion>().Where(s=>s.PreparationId==id));var calls=Owned<BuilderSemanticCall>().Where(c=>c.PreparationId==id);await Data("calls","每次模型调用与固定输入",calls);await Data("responses","原模型返回与完整性",Owned<BuilderSemanticResponse>().Where(r=>calls.Select(c=>c.Id).Contains(r.CallId)));await Data("call-reconciliations","调用结束核对依据",Owned<BuilderSemanticReconciliation>().Where(r=>calls.Select(c=>c.Id).Contains(r.CallId)));await Job(id,semantic.JobId);break;
                case "review":
                    var candidate=await One<Candidate>();record=Element(candidate);await Data("runs","原生成任务",Owned<BuilderRun>().Where(r=>r.Id==candidate.RunId));var candidateRun=await Owned<BuilderRun>().SingleAsync(r=>r.Id==candidate.RunId,ct);await Data("source","原来源（当前保存文本）",Owned<Source>().Where(s=>s.Id==candidateRun.SourceId));await Data("chunks","原来源片段",Owned<Chunk>().Where(c=>c.SourceId==candidateRun.SourceId));if(candidateRun.LibraryReleaseId!=null)await Data("library","创建时正式能力定义及测量题",Owned<Release>().Where(r=>r.Id==candidateRun.LibraryReleaseId));await Data("semantic","对应的语义准备",Owned<BuilderSemanticPreparation>().Where(s=>s.CandidateId==id));await Data("aliases","审核后保存的别名",Owned<Alias>().Where(s=>s.CandidateId==id));await Data("drafts","审核生成的草稿",Owned<ContentDraft>().Where(d=>d.Id==candidate.CreatedDraftId));break;
                case "drafts":
                    record=Element(await One<ContentDraft>());await Data("candidates","形成此草稿的候选",Owned<Candidate>().Where(c=>c.CreatedDraftId==id));await Data("reviews","完整内容审核记录",Owned<ContentReviewRecord>().Where(r=>r.DraftId==id));await Data("mapping","对应映射准备",Owned<MappingPreparation>().Where(m=>m.SourceDraftId==id));await Data("mapping-runs","映射建议批次",Owned<MappingRun>().Where(m=>m.SourceDraftId==id));break;
                case "mapping":
                    var mapping=await One<MappingPreparation>();record=Element(mapping);await Data("runs","成功保存的建议批次",Owned<MappingRun>().Where(r=>r.Id==mapping.RunId));await Data("calls","每次本机映射调用与用量",Owned<MappingModelCall>().Where(c=>c.PreparationId==id));var mappingCalls=Owned<MappingModelCall>().Where(c=>c.PreparationId==id);await Data("call-reconciliations","调用结束核对依据",Owned<MappingCallReconciliation>().Where(r=>mappingCalls.Select(c=>c.Id).Contains(r.CallId)));await Job(id,mapping.JobId);break;
                case "mapping-review":
                    var batch=await One<MappingRun>();record=Element(batch);var suggestions=Owned<MappingSuggestion>().Where(s=>s.RunId==id);await Data("suggestions","每项映射原建议",suggestions);await Data("decisions","逐项人工审核决定",Owned<MappingReviewDecision>().Where(d=>suggestions.Select(s=>s.Id).Contains(d.SuggestionId)));await Data("preparations","原后台准备",Owned<MappingPreparation>().Where(m=>m.RunId==id));await Data("library","创建时固定正式能力库",Owned<Release>().Where(r=>r.Id==batch.LibraryReleaseId));break;
                case "publication":
                    record=Element(await One<Release>());await Data("reviews","实际发布审核依据",Owned<ContentReviewRecord>().Where(r=>r.PublishedReleaseId==id));await Data("items","正式内容身份与修订",Owned<ReleaseItem>().Where(r=>r.ReleaseId==id));break;
            }
            await Data("audits","相关操作与恢复依据",Owned<Audit>().Where(r=>r.StudentId==null && ContentAuditActions.Contains(r.Action) && r.Details.Contains(id.ToString())));
            if(related!=null && !parts.Any(d=>d.Id==related))throw new ApiError(422,"INVALID_BUILDER_DATA","此阶段没有所选相关数据。");await snapshot.CommitAsync(ct);return new BuilderStageDetail(stage,id,record,parts.ToArray());
        });
    }
}
