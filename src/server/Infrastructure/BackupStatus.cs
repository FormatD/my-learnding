using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Learning;
public record BackupStatus(bool Configured,bool Available,bool Verified,string Status,bool ArchiveVerified,bool RestoreVerified,bool IndependentDisk,bool SchedulerActive,DateTimeOffset? LastSnapshotAt,double? SnapshotAgeHours,DateTimeOffset? NextDueAt,string? ErrorCode,string Notice);
public sealed class BackupProbe(string? configPath,string runnerPath,string connection)
{
    static string ReadPrivate(string path)
    {
        var file=new FileInfo(path);
        if(!file.Exists || file.LinkTarget!=null || file.Length>65536)throw new IOException();
        if(!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path)&(UnixFileMode.GroupRead|UnixFileMode.GroupWrite|UnixFileMode.GroupExecute|UnixFileMode.OtherRead|UnixFileMode.OtherWrite|UnixFileMode.OtherExecute))!=0)throw new IOException();
        return File.ReadAllText(path);
    }
    static string Text(JsonElement obj,string key)=>obj.GetProperty(key).GetString()??throw new JsonException();
    static bool Active(int pid,string config,string runner)
    {
        if(pid<=0)return false;
        using var process=new Process{StartInfo=new("/bin/ps"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false}};
        foreach(var argument in new[]{"-p",pid.ToString(),"-o","command="})process.StartInfo.ArgumentList.Add(argument);
        process.Start();if(!process.WaitForExit(1000)){process.Kill();throw new IOException();}
        var command=process.StandardOutput.ReadToEnd();return process.ExitCode==0 && command.TrimEnd().EndsWith(Path.GetFullPath(runner)+" --config "+Path.GetFullPath(config)+" --watch",StringComparison.Ordinal);
    }
    public BackupStatus Read(DateTimeOffset? observed=null)
    {
        if(string.IsNullOrWhiteSpace(configPath))return new(false,true,false,"NotConfigured",false,false,false,false,null,null,null,null,"尚未配置并验收每日加密备份、独立磁盘与30天保留；手工导出不能替代完整备份。");
        try
        {
            var now=observed??DateTimeOffset.UtcNow;
            using var configDoc=JsonDocument.Parse(ReadPrivate(Path.GetFullPath(configPath)));var config=configDoc.RootElement;
            var interval=config.TryGetProperty("intervalHours",out var configuredHours)?configuredHours.GetDouble():23;
            var timeoutSeconds=config.TryGetProperty("timeoutSeconds",out var configuredTimeout)?configuredTimeout.GetDouble():900;
            if(interval is <1 or >23 || timeoutSeconds is <10 or >1800 || (config.TryGetProperty("retentionDays",out var retention) && retention.GetInt32()!=30))throw new JsonException();
            var pg=config.GetProperty("postgres");var expected=new NpgsqlConnectionStringBuilder(connection);
            if(Text(pg,"database")!=expected.Database || Text(pg,"user")!=expected.Username || pg.GetProperty("port").GetInt32()!=expected.Port || !new[]{"localhost","127.0.0.1","::1"}.Contains(Text(pg,"host")) || !new[]{"localhost","127.0.0.1","::1"}.Contains(expected.Host))throw new IOException();
            var statePath=Text(config,"stateFile");using var stateDoc=JsonDocument.Parse(ReadPrivate(statePath));var state=stateDoc.RootElement;
            if(Text(state,"format")!="learning-backup/1")throw new JsonException();var status=Text(state,"status");
            if(!new[]{"Running","Succeeded","Failed"}.Contains(status))throw new JsonException();
            DateTimeOffset? snapshot=null,next=null;double? age=null;var verified=false;
            if(state.TryGetProperty("lastSuccess",out var last) && last.ValueKind!=JsonValueKind.Null)
            {
                snapshot=last.GetProperty("snapshotStartedAt").GetDateTimeOffset();if(snapshot>now.AddMinutes(1))throw new JsonException();age=Math.Max(0,(now-snapshot.Value).TotalHours);
                var name=Text(last,"archive");if(!Regex.IsMatch(name,@"\Alearning-\d{8}T\d{6}Z-[0-9a-f]{12}\.pgdump\.gpg\z"))throw new JsonException();
                var archive=new FileInfo(Path.Combine(Text(config,"backupDir"),name));
                if(archive.Exists && archive.LinkTarget==null && archive.Length==last.GetProperty("bytes").GetInt64() && last.GetProperty("archiveVerified").GetBoolean())
                {using var input=archive.OpenRead();verified=Convert.ToHexString(SHA256.HashData(input)).Equals(Text(last,"sha256"),StringComparison.OrdinalIgnoreCase);}
                next=snapshot.Value.AddHours(interval);
            }
            var active=false;var heartbeat=Path.ChangeExtension(statePath,".heartbeat.json");
            if(File.Exists(heartbeat))
            {
                using var beatDoc=JsonDocument.Parse(ReadPrivate(heartbeat));var beat=beatDoc.RootElement;var beatTime=beat.GetProperty("observedAt").GetDateTimeOffset();
                if(Text(beat,"format")!="learning-backup/1" || !new[]{"Checking","Waiting"}.Contains(Text(beat,"status")))throw new JsonException();
                var maxAge=Text(beat,"status")=="Checking" && status=="Running"?timeoutSeconds+120:180;
                active=beatTime<=now.AddMinutes(1) && (now-beatTime).TotalSeconds<=maxAge && Active(beat.GetProperty("pid").GetInt32(),configPath,runnerPath);
            }
            var code=state.TryGetProperty("errorCode",out var error)&&error.ValueKind==JsonValueKind.String?error.GetString():null;
            if(code!=null && !Regex.IsMatch(code,@"\A[A-Z0-9_]{1,80}\z"))code="BACKUP_EXECUTION_FAILED";
            // A readable archive and a live process do not prove physical independence or sustained RPO.
            return new(true,true,false,status,verified,false,false,active,snapshot,age,next,code,"本机归档检查与实际恢复分开记录；独立物理磁盘、持续24小时恢复点及4小时恢复仍未验收。关机或睡眠期间调度不能执行。");
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException or System.ComponentModel.Win32Exception)
        {return new(true,false,false,"Unavailable",false,false,false,false,null,null,null,"BACKUP_STATUS_UNAVAILABLE","无法核对备份配置、归档或实际调度进程。请检查本机备份服务，不能据此认定备份正常。");}
    }
    public static OperationSignal[] Signals(BackupStatus state)
    {
        if(!state.Configured)return [];
        if(!state.Available)return [new("BackupUnavailable","Warning","无法核对备份状态",1,"检查本机备份服务和私有配置；当前状态不能证明数据已备份。")];
        var result=new List<OperationSignal>();
        if(!state.SchedulerActive)result.Add(new("BackupSchedulerStopped","Warning","备份调度未确认运行",1,"启动本机备份调度；心跳文件不能单独证明进程仍在运行。"));
        if(state.Status=="Failed")result.Add(new("BackupFailed","Error","最近一次备份失败",1,"核对本机备份日志、磁盘与数据库；最后成功记录保留，修复后重新备份。"));
        if(state.SnapshotAgeHours>=24)result.Add(new("BackupOverdue","Error","成功备份已超过24小时",1,"立即检查备份服务；当前恢复点超出目标。"));
        if(state.LastSnapshotAt!=null && !state.ArchiveVerified)result.Add(new("BackupArchiveUnavailable","Error","最后成功归档无法核对",1,"归档可能缺失或摘要不一致；检查备份目录。"));
        if(state.LastSnapshotAt==null && state.Status!="Running")result.Add(new("BackupMissing","Warning","尚无可核对的成功备份",1,"完成首次加密归档及实际恢复演练。"));
        return result.ToArray();
    }
}
