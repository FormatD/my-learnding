using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Learning;
public record MemberInput(string UserName,string Password,string[]? Roles=null);
public record MemberRolesInput(string[] Roles,string Reason);
public static class FamilyData
{
    static Type[] PrivateTypes=>typeof(Row).Assembly.GetTypes().Where(t=>t.IsSubclassOf(typeof(Row))).OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
    static async Task<object> ReadRows<T>(Database db,Guid family,CancellationToken ct) where T:Row =>await db.Set<T>().AsNoTracking().Where(row=>row.FamilyId==family).OrderBy(row=>row.Id).ToArrayAsync(ct);
    static async Task<int> CountRows<T>(Database db,Guid family) where T:Row =>await db.Set<T>().CountAsync(row=>row.FamilyId==family);
    static async Task<Family> Owner(Database db,Actor actor)
    {
        actor.Require("Parent");if(!await db.AuthSessions.AsNoTracking().AnyAsync(s=>s.Id==actor.SessionId && !s.Revoked && s.ExpiresAt>DateTimeOffset.UtcNow))throw new ApiError(401,"LOGIN_REQUIRED","请重新登录。");var family=await db.Families.SingleOrDefaultAsync(f=>f.Id==actor.FamilyId)??throw new ApiError(401,"LOGIN_REQUIRED","家庭已删除，请重新登录。");
        if(family.OwnerAccountId!=actor.AccountId || family.OwnerAccountId==null)throw new ApiError(403,"FAMILY_OWNER_REQUIRED","此操作需要家庭负责人。");
        return family;
    }
    static async Task<SortedDictionary<string,int>> Counts(Database db,Guid family)
    {
        var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
        foreach(var type in PrivateTypes.Where(t=>t!=typeof(AuthSession) && t!=typeof(CommandRecord)))counts[type.Name]=await (Task<int>)typeof(FamilyData).GetMethod(nameof(CountRows),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(type).Invoke(null,[db,family])!;
        counts["PendingProjection"]=await db.Outbox.CountAsync(o=>o.FamilyId==family && o.ProcessedAt==null);
        counts["QueuedBuilder"]=await db.BuilderRuns.CountAsync(r=>r.FamilyId==family && r.Status=="Queued");
        return counts;
    }
    static string Hash(Family family,SortedDictionary<string,int> counts)=>Content.Hash(Json.Write(new {family.Id,family.Version,family.OwnerAccountId,counts}));
    static string FileName(PrivateFile file)
    {
        var name=Path.GetFileName(file.Name.Replace('\\','/'));name=new string(name.Select(c=>char.IsControl(c)?'_':c).ToArray());
        return name is "" or "." or ".."?"attachment"+(file.MimeType=="application/pdf"?".pdf":file.MimeType=="image/png"?".png":".jpg"):name;
    }
    static async Task WriteEntry(ZipArchive archive,string path,byte[] bytes,CancellationToken ct=default)
    {
        var entry=archive.CreateEntry(path,CompressionLevel.Fastest);await using var output=entry.Open();await output.WriteAsync(bytes,ct);
    }
    static string[] Roles(string[]? roles,bool allowEmpty=false)
    {
        if(roles==null)throw new ApiError(422,"INVALID_ROLES","请明确提交成员权限。");
        var normalized=roles.Distinct().OrderBy(r=>r,StringComparer.Ordinal).ToArray();
        if((normalized.Length==0 && !allowEmpty) || normalized.Any(r=>r is not "Parent" and not "ContentEditor" and not "Publisher"))throw new ApiError(422,"INVALID_ROLES","请选择家长、内容编辑或发布权限。");
        return normalized;
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/family/members",async(Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();await Owner(db,actor);
            return await (from membership in db.Set<FamilyMembership>() join account in db.Accounts on membership.AccountId equals account.Id where membership.FamilyId==actor.FamilyId select new {membership.Id,membership.AccountId,membership.Roles,account.UserName}).ToArrayAsync();
        });
        api.MapPost("/family/members",async(MemberInput input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();await Owner(db,actor);var roles=Roles(input.Roles??["Parent"]);
            if(string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Trim().Length<3 || input.Password==null || input.Password.Length is <12 or >128)throw new ApiError(422,"INVALID_CREDENTIALS","用户名至少 3 字，密码需 12～128 字。");
            if(await db.Accounts.AnyAsync(a=>a.UserName==input.UserName.Trim()))throw new ApiError(409,"USERNAME_UNAVAILABLE","用户名不可用。");
            var account=new Account {FamilyId=actor.FamilyId,UserName=input.UserName.Trim(),PasswordHash=Security.Password(input.Password),Roles=string.Join(',',roles)};
            var membership=new FamilyMembership {FamilyId=actor.FamilyId,AccountId=account.Id,Roles=account.Roles};db.AddRange(account,membership);return TypedResults.Created("/api/v1/family/members",new {membership.Id,membership.AccountId,membership.Roles,account.UserName});
        });
        api.MapPut("/family/members/{id:guid}/roles",async(Guid id,MemberRolesInput input,Database db,HttpContext ctx)=>
        {
            var actor=ctx.Actor();var family=await Owner(db,actor);var membership=await db.Set<FamilyMembership>().SingleOrDefaultAsync(m=>m.Id==id && m.FamilyId==actor.FamilyId)??throw new ApiError(404,"NOT_FOUND","成员不存在。");var roles=Roles(input.Roles,true);
            if(string.IsNullOrWhiteSpace(input.Reason))throw new ApiError(422,"REASON_REQUIRED","请填写权限调整依据。");
            if(membership.AccountId==family.OwnerAccountId && !roles.Contains("Parent"))throw new ApiError(422,"OWNER_PARENT_REQUIRED","负责人必须保留家长权限。");
            var before=membership.Roles;membership.Roles=string.Join(',',roles);var account=await db.Accounts.SingleAsync(a=>a.Id==membership.AccountId);account.Roles=membership.Roles;
            foreach(var session in await db.AuthSessions.Where(s=>s.FamilyId==family.Id && (s.AccountId==account.Id || s.Role=="Child")).ToListAsync())session.Revoked=true;
            db.Commands.RemoveRange(await db.Commands.Where(c=>c.FamilyId==family.Id && c.ActorId==account.Id).ToListAsync());
            db.Audits.Add(new Audit {FamilyId=family.Id,ActorId=actor.Id,Action="MemberRolesChanged",Details=Json.Write(new {membership.Id,before,after=membership.Roles,input.Reason})});
            return TypedResults.Ok(new {membership.Id,membership.Roles,notice="相关成员与孩子会话已撤销，需重新登录；权限缓存已清理。"});
        });
        api.MapGet("/family/export",async(Database db,HttpContext ctx,IConfiguration config)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync();var actor=ctx.Actor();await db.Lock(actor.FamilyId);var family=await Owner(db,actor);
            var data=new SortedDictionary<string,object>(StringComparer.Ordinal);
            foreach(var type in PrivateTypes.Where(t=>t!=typeof(Account) && t!=typeof(AuthSession) && t!=typeof(CommandRecord) && t!=typeof(PrivateFile)))data[type.Name]=await (Task<object>)typeof(FamilyData).GetMethod(nameof(ReadRows),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(type).Invoke(null,[db,family.Id,ctx.RequestAborted])!;
            var members=await (from membership in db.Set<FamilyMembership>() join account in db.Accounts on membership.AccountId equals account.Id where membership.FamilyId==family.Id select new {account.Id,account.FamilyId,account.UserName,membership.Roles,account.CreatedAt}).ToArrayAsync();
            var files=await db.Set<PrivateFile>().AsNoTracking().Where(f=>f.FamilyId==family.Id).OrderBy(f=>f.Id).Select(f=>new {f.Id,f.FamilyId,f.Name,f.MimeType,f.Hash,size=f.Bytes.Length}).ToArrayAsync(ctx.RequestAborted);
            var metadata=files.Select(f=>new {f.Id,f.FamilyId,f.Name,f.MimeType,f.Hash,f.size,archivePath=$"files/{f.Id}/{FileName(new PrivateFile{Name=f.Name,MimeType=f.MimeType})}"}).ToArray();
            var path=ExportCleanup.NewPath(config,family.Id);
            try
            {
                await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                using(var archive=new ZipArchive(output,ZipArchiveMode.Create,true))
                {
                    await WriteEntry(archive,"manifest.json",Encoding.UTF8.GetBytes(Json.Write(new {format="learning-family-export/1",exportedAt=DateTimeOffset.UtcNow,family,members,data,files=metadata,notice="包含业务历史及原始附件，不包含密码、登录令牌和传输响应缓存；不能直接作为数据库恢复包。"})),ctx.RequestAborted);
                    foreach(var f in metadata)
                    {
                        var bytes=await db.Set<PrivateFile>().Where(file=>file.Id==f.Id && file.FamilyId==family.Id).Select(file=>file.Bytes).SingleAsync(ctx.RequestAborted);
                        await WriteEntry(archive,f.archivePath,bytes,ctx.RequestAborted);
                    }
                }
                File.SetLastWriteTimeUtc(path,DateTime.UtcNow);await tx.CommitAsync();ExportCleanup.Built(path);
                ctx.Response.OnCompleted(()=>{ExportCleanup.Remove(path);return Task.CompletedTask;});
                return Results.File(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,65536,true),"application/zip",$"learning-family-{family.Id}.zip");
            }
            catch{ExportCleanup.Remove(path);throw;}
        }).WithMetadata(new DownloadResponseMetadata(["application/zip"]));
        api.MapGet("/family/delete-preview",async(Database db,HttpContext ctx)=>
        {
            await using var tx=await db.Database.BeginTransactionAsync();var actor=ctx.Actor();await db.Lock(actor.FamilyId);var family=await Owner(db,actor);var counts=await Counts(db,family.Id);await tx.CommitAsync();
            return TypedResults.Ok(new {family=family.Name,counts,previewHash=Hash(family,counts),requiredConfirm="永久删除家庭",notice="将删除所有学生、成员账户、内容、来源、附件、向量与业务历史，并撤销全部会话。"});
        });
        api.MapPost("/family:delete",async(DeleteInput input,Database db,HttpContext ctx,IConfiguration config)=>
        {
            var actor=ctx.Actor();var family=await Owner(db,actor);var counts=await Counts(db,family.Id);
            if(input.PreviewHash!=Hash(family,counts))throw new ApiError(412,"PREVIEW_CHANGED","家庭数据范围已变化，请重新预览。");
            var account=await db.Accounts.SingleAsync(a=>a.Id==actor.AccountId);
            if(input.Confirm!="永久删除家庭" || string.IsNullOrEmpty(input.Password) || input.Password.Length>128 || !Security.Check(input.Password,account.PasswordHash))throw new ApiError(422,"DELETE_CONFIRMATION_REQUIRED","需要负责人密码和“永久删除家庭”的明确确认。");
            var receipt=Guid.NewGuid();var path=config["FamilyDeletionLedger"]??Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config["DeletionLedger"]??"../../.local/deleted-students.txt"))!,"deleted-families.txt");Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using(var ledger=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough))
            {await ledger.WriteAsync(Encoding.UTF8.GetBytes($"{family.Id},{receipt}\n"));ledger.Flush(true);}
            // Break the owner reference before cascading all private rows and accounts.
            ExportCleanup.RemoveFamily(config["ExportDirectory"]!,family.Id);
            family.OwnerAccountId=null;await db.SaveChangesAsync();db.Families.Remove(family);ctx.Items["familyDeleted"]=true;ctx.Response.Cookies.Delete(Security.Cookie);
            return TypedResults.Ok(new {deleted=true,familyId=family.Id,receiptId=receipt,scope="Family",backupPolicy="恢复旧备份必须应用独立家庭删除请求清单"});
        });
    }
}
