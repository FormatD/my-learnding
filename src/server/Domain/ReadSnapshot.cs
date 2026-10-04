using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace Learning;

// Cross-table read responses and their optimistic version must describe one committed state.
public static class ReadSnapshot
{
    public static async Task<IDbContextTransaction> Begin(Database db,HttpContext context)
    {
        var ct=context.RequestAborted;var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,ct);
        try
        {
            var family=context.Actor().FamilyId;
            var version=await db.Families.AsNoTracking().Where(f=>f.Id==family).Select(f=>(long?)f.Version).SingleOrDefaultAsync(ct);
            if(version==null)throw new ApiError(401,"LOGIN_REQUIRED","请重新登录。");
            context.Response.Headers.ETag=$"\"{version.Value}\"";return transaction;
        }
        catch{await transaction.DisposeAsync();throw;}
    }
}
