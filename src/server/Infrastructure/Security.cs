using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Learning;
public record Actor(Guid SessionId, Guid FamilyId, Guid? AccountId, Guid? StudentId, string Role, string Roles)
{
    public Guid Id => AccountId??SessionId;
    public bool Can(string role)=>Role!="Child" && (Roles.Split(',').Contains(role) || role=="ContentEditor" && Roles.Split(',').Contains("Publisher"));
    public void Require(string role)
    {
        if (!Can(role)) throw new ApiError(403,"FORBIDDEN","此操作需要家长或指定管理权限。");
    }
    public async Task<Student> Student(Database db, Guid id)
    {
        if(Role!="Child")Require("Parent");
        if (Role=="Child" && StudentId!=id) throw new ApiError(404,"NOT_FOUND","找不到该学生。");
        return await db.Students.SingleOrDefaultAsync(s => s.Id==id && s.FamilyId==FamilyId) ?? throw new ApiError(404,"NOT_FOUND","找不到该学生。");
    }
}
public static class Security
{
    public const string Cookie="learning-session";
    public static string Password(string text)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var key=Rfc2898DeriveBytes.Pbkdf2(text,salt,210000,HashAlgorithmName.SHA256,32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
    public static bool Check(string text,string stored)
    {
        var parts=stored.Split('.');
        return CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(text,Convert.FromBase64String(parts[0]),210000,HashAlgorithmName.SHA256,32),Convert.FromBase64String(parts[1]));
    }
    public static Actor Actor(this HttpContext ctx) => (Actor)ctx.Items["actor"]!;
    public static async Task<string> CreateSession(Database db, HttpContext ctx, Guid family, Guid? account, Guid? student, string role)
    {
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.AuthSessions.Add(new() { FamilyId=family,AccountId=account,StudentId=student,Role=role,TokenHash=Content.Hash(token),ExpiresAt=DateTimeOffset.UtcNow.AddHours(role=="Child"?8:12) });
        await db.SaveChangesAsync();
        ctx.Response.Cookies.Append(Cookie,token,new() { HttpOnly=true,Secure=ctx.Request.IsHttps,SameSite=SameSiteMode.Strict,Path="/",MaxAge=TimeSpan.FromHours(role=="Child"?8:12) });
        return token;
    }
}
