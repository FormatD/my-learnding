using Microsoft.EntityFrameworkCore;
namespace Learning;
public record ReadPagePosition(int Version,string Scope,Guid FamilyId,Guid? FilterId,int PageSize,DateTimeOffset CreatedAt,Guid Id);
public record ReadPage<T>(int PageSize,int Total,T[] Rows,string? NextCursor);
public static class StableReadPage
{
 public static async Task<ReadPage<T>> Load<T>(IQueryable<T> query,string scope,Guid family,Guid? filter,int? pageSize,string? cursor,CancellationToken ct) where T:Row
 {
  var size=pageSize??20;if(size<1||size>50)throw new ApiError(422,"INVALID_PAGE","每页请使用1～50条。");ReadPagePosition? position=null;
  if(cursor!=null)
  {
   try{
    if(cursor.Length>1024)throw new FormatException();position=Json.Read<ReadPagePosition>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));
    if(position==null||position.Version!=1||position.Scope!=scope||position.FamilyId!=family||position.FilterId!=filter||position.PageSize!=size||position.Id==Guid.Empty||position.CreatedAt.Offset!=TimeSpan.Zero||position.CreatedAt==default)throw new FormatException();
   }catch(Exception ex)when(ex is FormatException or System.Text.Json.JsonException or ArgumentException){throw new ApiError(422,"INVALID_CURSOR","翻页位置无效，请刷新后重试。");}
  }
  var total=await query.CountAsync(ct);
  if(position!=null){var time=position.CreatedAt;var id=position.Id;query=query.Where(row=>row.CreatedAt<time||row.CreatedAt==time&&row.Id.CompareTo(id)>0);}
  var window=await query.OrderByDescending(row=>row.CreatedAt).ThenBy(row=>row.Id).Take(size+1).ToArrayAsync(ct);var rows=window.Take(size).ToArray();string? next=null;
  if(window.Length>size){var last=rows[^1];next=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Json.Write(new ReadPagePosition(1,scope,family,filter,size,last.CreatedAt,last.Id))));}
  return new(size,total,rows,next);
 }
}
