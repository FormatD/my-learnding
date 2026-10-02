using System.Collections.Concurrent;
namespace Learning;
public class ExportCleanup(IConfiguration config,ILogger<ExportCleanup> logger):BackgroundService
{
    static readonly ConcurrentDictionary<string,bool> Building=new();
    public static string NewPath(IConfiguration config,Guid family)
    {
        var folder=config["ExportDirectory"]!;Directory.CreateDirectory(folder);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var path=Path.Combine(folder,family.ToString("N")+"-"+Guid.NewGuid().ToString("N")+".zip");Building[path]=true;return path;
    }
    public static void Built(string path)=>Building.TryRemove(path,out _);
    public static void Remove(string path){Building.TryRemove(path,out _);try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    static bool ExportName(string path)
    {
        var parts=Path.GetFileNameWithoutExtension(path).Split('-');return parts.Length==2 && parts.All(p=>Guid.TryParseExact(p,"N",out _));
    }
    public static void RemoveFamily(string directory,Guid family)
    {
        if(!Directory.Exists(directory))return;
        foreach(var path in Directory.EnumerateFiles(directory,family.ToString("N")+"-*.zip"))if(ExportName(path)){File.Delete(path);Building.TryRemove(path,out _);}
    }
    public static int Sweep(string directory,DateTime utcNow,TimeSpan age)
    {
        if(!Directory.Exists(directory))return 0;var count=0;
        foreach(var path in Directory.EnumerateFiles(directory,"*.zip"))
        {
            if(!ExportName(path) || Building.ContainsKey(path) || File.GetLastWriteTimeUtc(path)>utcNow-age)continue;
            File.Delete(path);count++;
        }
        return count;
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try{Sweep(config["ExportDirectory"]!,DateTime.UtcNow,TimeSpan.FromMinutes(15));}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){logger.LogError(ex,"Temporary export cleanup failed");}
            await Task.Delay(TimeSpan.FromSeconds(30),ct);
        }
    }
}
