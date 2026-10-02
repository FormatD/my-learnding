using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace Learning;
public class PrivateFile : Row
{
    public string Name { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Hash { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}
public class PaperWrong : Row
{
    public Guid StudentId { get; set; }
    public Guid? DraftId { get; set; }
    public Guid? QuestionId { get; set; }
    public Guid? ReleaseId { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string ConfirmationReason { get; set; } = "";
    public Guid? FileId { get; set; }
    public string Stem { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Status { get; set; } = "PendingMapping";
    public string ErrorType { get; set; } = "Unknown";
}
public record FileInput(string Name,string MimeType,string Base64);
public record PDFInput(Guid FileId,string Title,bool AllowExternalAI=false,string UsageScope="FamilyOnly");
public record PaperInput(string Stem,string Answer,Guid? FileId=null,string ErrorType="Unknown");
public static class Files
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/files",(FileInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require(input.MimeType=="application/pdf"?"ContentEditor":"Parent");byte[] bytes;
            try { bytes=Convert.FromBase64String(input.Base64); } catch (FormatException) { throw new ApiError(422,"INVALID_FILE","文件编码无效。"); }
            if (bytes.Length==0 || bytes.Length>10_000_000 || input.Name.Length>200) throw new ApiError(422,"FILE_SIZE","文件大小须在 10 MB 以内。");
            var valid=input.MimeType switch { "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),"image/png" => bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}),"image/jpeg" => bytes.Length>3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255,_ => false };
            if (!valid) throw new ApiError(422,"FILE_TYPE","仅支持文件头正确的 PDF、PNG、JPEG。");
            var file=new PrivateFile { FamilyId=a.FamilyId,Name=Path.GetFileName(input.Name),MimeType=input.MimeType,Bytes=bytes,Hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() };db.Add(file);return Results.Created($"/api/v1/files/{file.Id}",new { file.Id,file.Name,file.MimeType,file.Hash,size=bytes.Length });
        });
        api.MapGet("/files/{id:guid}",async (Guid id,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();if (a.Role=="Child") throw new ApiError(404,"NOT_FOUND","文件不存在。");
            var file=await db.Set<PrivateFile>().SingleOrDefaultAsync(f => f.Id==id && f.FamilyId==a.FamilyId) ?? throw new ApiError(404,"NOT_FOUND","文件不存在。");
            if(!a.Can("Parent") && (!a.Can("ContentEditor") || !await db.Sources.AnyAsync(s=>s.FamilyId==a.FamilyId && s.FileId==file.Id)))throw new ApiError(404,"NOT_FOUND","文件不存在。");
            return Results.File(file.Bytes,file.MimeType,file.Name);
        });
        api.MapPost("/content/pdf-sources",async (PDFInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("ContentEditor");var file=await db.Set<PrivateFile>().SingleOrDefaultAsync(f => f.Id==input.FileId && f.FamilyId==a.FamilyId && f.MimeType=="application/pdf") ?? throw new ApiError(404,"NOT_FOUND","PDF 不存在。");
            var old=await db.Sources.SingleOrDefaultAsync(s => s.FamilyId==a.FamilyId && s.Hash==file.Hash);if (old!=null) return Results.Ok(old);
            var source=new Source { FamilyId=a.FamilyId,FileId=file.Id,Title=input.Title,Hash=file.Hash,UsageScope=input.UsageScope,AllowExternalAI=input.AllowExternalAI };db.Add(source);
            var job=new BuilderRun {FamilyId=a.FamilyId,SourceId=source.Id,Type="ParsePDF",Provider="LocalParser",Model="pdfpig/0.1.16",PromptVersion="parser/1",InputHash=Content.Hash(file.Hash+":pdfpig/0.1.16")};db.Add(job);
            return Results.Accepted("/api/v1/builder",new {source,job});
        });
        api.MapPost("/students/{id:guid}/paper-wrongs",async (Guid id,PaperInput input,Database db,HttpContext ctx) =>
        {
            var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);
            await PaperLearning.ValidateInput(db,a,input);
            var wrong=new PaperWrong { FamilyId=a.FamilyId,StudentId=id,FileId=input.FileId,Stem=input.Stem,Answer=input.Answer,ErrorType=input.ErrorType };db.Add(wrong);return Results.Created("/api/v1/students/"+id+"/paper-wrongs",wrong);
        });
        PaperLearning.Map(api);
        api.MapGet("/students/{id:guid}/paper-wrongs",async (Guid id,Database db,HttpContext ctx) => { var a=ctx.Actor();a.Require("Parent");await a.Student(db,id);return await db.Set<PaperWrong>().Where(p => p.StudentId==id).OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(); });
    }
    public static List<(int Page,string Text)> ParsePDF(byte[] bytes)
    {
        var pages=new List<(int Page,string Text)>();
        try {using var pdf=PdfDocument.Open(bytes);if(pdf.NumberOfPages>200)throw new ApiError(422,"PDF_PAGE_LIMIT","每次导入最多 200 页。");foreach(var page in pdf.GetPages())pages.Add((page.Number,page.Text));}
        catch(ApiError){throw;}catch{throw new ApiError(422,"PDF_PARSE_FAILED","PDF 无法解析，请提供未加密文本型 PDF。");}
        if(pages.All(p=>string.IsNullOrWhiteSpace(p.Text)))throw new ApiError(422,"NEEDS_OCR","PDF 无文本层，需要 OCR。");
        return pages;
    }
}
