using Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class BuilderWorkerLaneCases
{
 public static async Task Run(Database db)
 {
  await db.Database.MigrateAsync();
  var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
  using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
  var keyFile=Path.Combine(Path.GetTempPath(),"learning-controlled-key-"+Guid.NewGuid());
  await File.WriteAllTextAsync(keyFile,"controlled-test-only");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(keyFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  try
  {
   var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Omlx:Endpoint",$"http://127.0.0.1:{port}/v1"},{"Omlx:Model","controlled-paused-local"},{"Omlx:ApiKeyFile",keyFile},{"Omlx:TimeoutMilliseconds","30000"}}).Build();
   var family=new Family();var catalog=Content.Fixture();var release=new Release{FamilyId=family.Id,Number=1,Payload=Json.Write(catalog),Hash=Content.Hash(Json.Write(catalog))};var student=new Student{FamilyId=family.Id,ActiveReleaseId=release.Id};
   var source=new Source{FamilyId=family.Id,Title="受控本机 HTTP 等待",Text="余数必须小于除数。",Hash=Content.Hash("余数必须小于除数。")};var config=BuilderConfiguration.Current(configuration,"LocalOmlx");var payload=Json.Write(config);var hash=Content.Hash(payload);
   var run=new BuilderRun{FamilyId=family.Id,SourceId=source.Id,LibraryReleaseId=release.Id,Provider=config.Provider,Model=config.Model,PromptVersion=config.PromptVersion,InputVersion="builder-input/4",ModelConfigHash=hash,ModelConfigPayload=payload,InputHash=Content.Hash(source.Hash+":"+config.Provider+":"+config.Model+":"+config.PromptVersion+":builder-input/4:"+release.Hash+":"+hash)};
   db.AddRange(family,release,student,source,run,new Chunk{FamilyId=family.Id,SourceId=source.Id,Locator="段落1",Text=source.Text});await db.SaveChangesAsync();await Publishing.Register(db,release);await db.SaveChangesAsync();
   var services=new ServiceCollection();services.AddScoped(_=>new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(db.Database.GetConnectionString()).Options,configuration));await using var container=services.BuildServiceProvider();
   using var worker=new ProjectionWorker(container.GetRequiredService<IServiceScopeFactory>(),NullLogger<ProjectionWorker>.Instance);await worker.StartAsync(CancellationToken.None);
   try
   {
    // Hold the actual local HTTP completion response, then append a real graded learning input.
    var request=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));using var reader=new StreamReader(request.Request.InputStream);var body=await reader.ReadToEndAsync();if(!body.Contains("controlled-paused-local"))throw new Exception("Expected frozen local transport not used");
    var question=catalog.Questions[0];var task=new StudyTask{FamilyId=family.Id,StudentId=student.Id,ReleaseId=release.Id,QuestionId=question.Id};var session=new LearningSession{FamilyId=family.Id,StudentId=student.Id,TaskId=task.Id,ReleaseId=release.Id,QuestionId=question.Id};var attempt=new Attempt{FamilyId=family.Id,StudentId=student.Id,SessionId=session.Id,Number=1,ClientSubmissionId=Guid.NewGuid()};var ev=new Outbox{FamilyId=family.Id,StudentId=student.Id,AttemptId=attempt.Id};
    using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3)))await using(var tx=await db.Database.BeginTransactionAsync(deadline.Token)){await db.Lock(family.Id,deadline.Token);db.AddRange(task,session,attempt,new Grading{FamilyId=family.Id,AttemptId=attempt.Id,Number=1,Result="Correct"},ev);await db.SaveChangesAsync(deadline.Token);await tx.CommitAsync(deadline.Token);}
    var watch=System.Diagnostics.Stopwatch.StartNew();while(!await db.Set<ConsumerReceipt>().AsNoTracking().AnyAsync(r=>r.EventId==ev.Id)){if(watch.Elapsed>TimeSpan.FromSeconds(5))throw new Exception("Learning projection blocked by paused local completion");await Task.Delay(50);}
    if(!await db.Evidence.AsNoTracking().AnyAsync(e=>e.AttemptId==attempt.Id))throw new Exception("Actual assessment evidence missing");
    if((await db.BuilderRuns.AsNoTracking().SingleAsync(r=>r.Id==run.Id)).Status!="Queued")throw new Exception("Controlled completion returned before learning projection");
    var bytes=Encoding.UTF8.GetBytes(Json.Write(new{model=config.Model,choices=new[]{new{finish_reason="stop",message=new{content=Json.Write(new BuilderEnvelope("kc-candidate/2",[]))}}},usage=new{prompt_tokens=10,completion_tokens=5}}));request.Response.ContentType="application/json";request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
    watch.Restart();while((await db.BuilderRuns.AsNoTracking().SingleAsync(r=>r.Id==run.Id)).Status!="Completed"){if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Builder result did not complete");await Task.Delay(50);}
    Console.WriteLine("PASS actual hosted worker processes same-family graded attempt and assessment receipt while local HTTP model response remains paused; separate scopes and real call ledger");
   }
   finally{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));await worker.StopAsync(stop.Token);}
  }
  finally{File.Delete(keyFile);}
 }
}
