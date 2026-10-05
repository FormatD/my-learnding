using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
namespace Learning;

public sealed class FileUploadTicket : Row
{
    public Guid OwnerAccountId { get; set; }
    public string Name { get; set; }="";
    public string MimeType { get; set; }="";
    public int Size { get; set; }
    public string Hash { get; set; }="";
    public string Purpose { get; set; }="Attachment";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; }="AwaitingBytes";
    public Guid? CompletedFileId { get; set; }
    [JsonIgnore] public string CredentialHash { get; set; }="";
    [JsonIgnore] public byte[] StagedBytes { get; set; }=[];
}
public record UploadTicketInput(string Name,string MimeType,int Size,string Sha256,string Purpose="Attachment");
public record UploadTicketReceipt(Guid FileId,string UploadUrl,string Credential,DateTimeOffset ExpiresAt,string FamilyVersion);
public record UploadTicketStatus(Guid FileId,string Name,string MimeType,int Size,string Hash,string Purpose,DateTimeOffset ExpiresAt,string Status,Guid? CompletedFileId);
public record UploadStageReceipt(Guid FileId,string Status,int BytesUploaded,string FamilyVersion);
public record UploadCompleteReceipt(Guid Id,string Name,string MimeType,string Hash,int Size,string FamilyVersion,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? FileSnapshotHash=null);
public record UploadBodyMetadata;
public record UploadCommandGuardMetadata(bool Creation=false);
public record SensitiveCommandResponseMetadata;
public record AdditionalProblemStatusMetadata(params int[] StatusCodes);

public static class FileUploads
{
    public const string CredentialHeader="X-Upload-Credential";
    public static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static void Authorize(Actor actor,string purpose,string mime)
    {
        if(purpose=="LearningResource"||mime=="application/pdf")actor.Require("ContentEditor");else actor.Require("Parent");
        if(actor.AccountId==null)throw new ApiError(403,"FORBIDDEN","上传需要已授权的家庭账号。");
    }
    static void Validate(UploadTicketInput input)
    {
        if(input.Purpose is not "Attachment" and not "LearningResource")throw new ApiError(422,"INVALID_FILE_PURPOSE","请选择普通附件或学习材料。");
        var allowed=input.Purpose=="LearningResource"?new[]{"application/pdf","image/png","image/jpeg","audio/wav","audio/mpeg"}:new[]{"application/pdf","image/png","image/jpeg"};
        if(!allowed.Contains(input.MimeType))throw new ApiError(422,"FILE_TYPE","文件类型不支持当前用途。");
        if(input.Size is <1 or >10_000_000||string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>200||input.Name.Any(char.IsControl)||string.IsNullOrWhiteSpace(Path.GetFileName(input.Name)))throw new ApiError(422,"FILE_SIZE","文件须在10 MB以内且有有效名称。");
        if(input.Sha256?.Length!=64||!System.Text.RegularExpressions.Regex.IsMatch(input.Sha256??"","^[a-f0-9]{64}$"))throw new ApiError(422,"INVALID_FILE_HASH","请提供文件的SHA-256摘要。");
    }
    static async Task<string> NextVersion(Database db,Actor actor)=> ((await db.Families.SingleAsync(f=>f.Id==actor.FamilyId)).Version+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    static async Task<FileUploadTicket> Owned(Database db,HttpContext ctx)
    {
        var actor=ctx.Actor();if(actor.AccountId==null||actor.Role=="Child")throw new ApiError(403,"FORBIDDEN","上传需要家庭账号。");
        var id=Guid.Parse(ctx.Request.RouteValues["id"]!.ToString()!);
        var ticket=await db.Set<FileUploadTicket>().SingleOrDefaultAsync(t=>t.Id==id&&t.FamilyId==actor.FamilyId&&t.OwnerAccountId==actor.AccountId,ctx.RequestAborted)??throw new ApiError(404,"NOT_FOUND","找不到本人上传凭据。");
        Authorize(actor,ticket.Purpose,ticket.MimeType);
        if(ticket.ExpiresAt<=DateTimeOffset.UtcNow||ticket.Status=="Expired")throw new ApiError(410,"UPLOAD_EXPIRED","上传凭据已到期，请重新选择文件上传。");
        var credential=ctx.Request.Headers[CredentialHeader].ToString();
        if(credential.Length!=64||!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Content.Hash(credential)),Convert.FromHexString(ticket.CredentialHash)))throw new ApiError(403,"UPLOAD_CREDENTIAL_INVALID","上传凭据无效。");
        return ticket;
    }
    public static async Task CheckReplay(Database db,HttpContext ctx,string body)
    {
        if(ctx.GetEndpoint()?.Metadata.GetMetadata<UploadCommandGuardMetadata>() is not { } guard)return;
        if(guard.Creation){var input=Json.Read<UploadTicketInput>(body);Authorize(ctx.Actor(),input.Purpose,input.MimeType);if(!ctx.Request.HasJsonContentType())throw new ApiError(415,"INVALID_UPLOAD_MEDIA","请提交JSON上传声明。");}
        else{await Owned(db,ctx);if(ctx.GetEndpoint()?.Metadata.GetMetadata<UploadBodyMetadata>()!=null)CheckMedia(ctx.Request);}
    }
    static void CheckMedia(HttpRequest request){if(!string.Equals(request.ContentType?.Split(';')[0].Trim(),"application/octet-stream",StringComparison.OrdinalIgnoreCase))throw new ApiError(415,"INVALID_UPLOAD_MEDIA","请提交原始文件字节。");}
    static void CheckBytes(FileUploadTicket ticket,byte[] bytes)
    {
        if(bytes.Length!=ticket.Size||Digest(bytes)!=ticket.Hash)throw new ApiError(422,"UPLOAD_CONTENT_MISMATCH","实际文件大小或摘要与上传声明不符。");
        if(!ResourceFiles.ValidHeader(ticket.MimeType,bytes))throw new ApiError(422,"FILE_TYPE","文件签名与声明类型不符。");
    }
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/files/upload-tickets",async(UploadTicketInput input,Database db,HttpContext ctx)=>{
            var actor=ctx.Actor();Validate(input);Authorize(actor,input.Purpose,input.MimeType);
            var credential=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();var now=DateTimeOffset.UtcNow;
            var ticket=new FileUploadTicket{FamilyId=actor.FamilyId,OwnerAccountId=actor.AccountId!.Value,Name=Path.GetFileName(input.Name),MimeType=input.MimeType,Size=input.Size,Hash=input.Sha256,Purpose=input.Purpose,CreatedAt=now,ExpiresAt=now.AddMinutes(15),CredentialHash=Content.Hash(credential)};
            db.Add(ticket);return TypedResults.Created($"/api/v1/files/upload-tickets/{ticket.Id}",new UploadTicketReceipt(ticket.Id,$"/api/v1/files/uploads/{ticket.Id}",credential,ticket.ExpiresAt,await NextVersion(db,actor)));
        }).WithMetadata(new SensitiveCommandResponseMetadata(),new UploadCommandGuardMetadata(Creation:true));
        api.MapGet("/files/upload-tickets/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>{
            var actor=ctx.Actor();if(actor.AccountId==null||actor.Role=="Child")throw new ApiError(403,"FORBIDDEN","上传需要家庭账号。");
            var ticket=await db.Set<FileUploadTicket>().AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id&&t.FamilyId==actor.FamilyId&&t.OwnerAccountId==actor.AccountId,ctx.RequestAborted)??throw new ApiError(404,"NOT_FOUND","找不到本人上传记录。");Authorize(actor,ticket.Purpose,ticket.MimeType);
            return new UploadTicketStatus(ticket.Id,ticket.Name,ticket.MimeType,ticket.Size,ticket.Hash,ticket.Purpose,ticket.ExpiresAt,ticket.ExpiresAt<=DateTimeOffset.UtcNow&&ticket.Status!="Completed"?"Expired":ticket.Status,ticket.CompletedFileId);
        });
        api.MapPut("/files/uploads/{id:guid}",async(Guid id,Database db,HttpContext ctx)=>{
            var ticket=await Owned(db,ctx);if(ticket.Status=="Completed")throw new ApiError(409,"UPLOAD_ALREADY_COMPLETE","文件已完成上传，不能覆盖。");
            CheckMedia(ctx.Request);
            using var buffer=new MemoryStream();await ctx.Request.Body.CopyToAsync(buffer,ctx.RequestAborted);var bytes=buffer.ToArray();CheckBytes(ticket,bytes);
            ticket.StagedBytes=bytes;ticket.Status="Stored";return new UploadStageReceipt(ticket.Id,ticket.Status,bytes.Length,await NextVersion(db,ctx.Actor()));
        }).WithMetadata(new UploadBodyMetadata(),new UploadCommandGuardMetadata(),new AdditionalProblemStatusMetadata(410));
        api.MapPost("/files/{id:guid}:complete",async(Guid id,Database db,HttpContext ctx)=>{
            var ticket=await Owned(db,ctx);PrivateFile file;
            if(ticket.Status=="Completed")file=await db.Set<PrivateFile>().SingleAsync(f=>f.Id==ticket.CompletedFileId&&f.FamilyId==ticket.FamilyId,ctx.RequestAborted);
            else{
                if(ticket.Status!="Stored")throw new ApiError(409,"UPLOAD_NOT_STORED","文件字节尚未上传。");CheckBytes(ticket,ticket.StagedBytes);
                file=new PrivateFile{Id=ticket.Id,FamilyId=ticket.FamilyId,Name=ticket.Name,MimeType=ticket.MimeType,Hash=ticket.Hash,Bytes=ticket.StagedBytes,Purpose=ticket.Purpose=="LearningResource"?"LearningResource":null};db.Add(file);
                ticket.CompletedFileId=file.Id;ticket.Status="Completed";ticket.StagedBytes=[];
            }
            return new UploadCompleteReceipt(file.Id,file.Name,file.MimeType,file.Hash,file.Bytes.Length,await NextVersion(db,ctx.Actor()),file.Purpose=="LearningResource"?ResourceFiles.Snapshot(file):null);
        }).WithMetadata(new UploadCommandGuardMetadata(),new AdditionalProblemStatusMetadata(410));
    }
}

public sealed class FileUploadCleanup(IServiceScopeFactory scopes,IConfiguration config,ILogger<FileUploadCleanup> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var seconds=Math.Clamp(config.GetValue<int?>("FileUploadCleanupSeconds")??60,1,600);
        while(!ct.IsCancellationRequested){
            try{
                await using var scope=scopes.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<Database>();var now=DateTimeOffset.UtcNow;
                var families=await db.Set<FileUploadTicket>().Where(t=>t.ExpiresAt<=now&&(t.Status=="AwaitingBytes"||t.Status=="Stored")).Select(t=>t.FamilyId).Distinct().Take(100).ToArrayAsync(ct);
                foreach(var family in families){await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Lock(family);var expired=await db.Set<FileUploadTicket>().Where(t=>t.FamilyId==family&&t.ExpiresAt<=DateTimeOffset.UtcNow&&(t.Status=="AwaitingBytes"||t.Status=="Stored")).ToArrayAsync(ct);foreach(var t in expired){t.Status="Expired";t.StagedBytes=[];}if(expired.Length>0){var row=await db.Families.SingleAsync(f=>f.Id==family,ct);row.Version++;await db.SaveChangesAsync(ct);}await tx.CommitAsync(ct);db.ChangeTracker.Clear();}
            }catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}catch(Exception ex){logger.LogError(ex,"Private upload expiry cleanup failed");}
            await Task.Delay(TimeSpan.FromSeconds(seconds),ct);
        }
    }
}
