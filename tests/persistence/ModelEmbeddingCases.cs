using Learning;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
public static class ModelEmbeddingCases
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action,string code){try{action();throw new Exception("Accepted invalid embedding: "+code);}catch(ApiError e){Check(e.Code==code,"Wrong rejection "+e.Code+" expected "+code);}}
    public static async Task Run()
    {
        var space=new ModelEmbeddingSpace("LocalOmlx","controlled-embedding-model","fixed-content-digest-v1",3,ModelEmbeddingSpace.Preprocessing);space.Validate();
        Check(space.Id==(space with{}).Id && space.Id!=(space with{ModelVersion="v2"}).Id && space.Id!=(space with{Dimensions=4}).Id && space.Id!=(space with{Model="other"}).Id,"Space omits model/version/dimensions");
        Reject(()=>(space with{PreprocessingVersion="unknown"}).Validate(),"EMBEDDING_CONFIGURATION_INVALID");
        var kc=new KC(Guid.NewGuid(),Guid.NewGuid(),"E1","有余数除法","独立计算整数除法的商与余数","余数小于除数",Subject:"MATH",GradeMin:2,GradeMax:2);
        var cross=kc with{Id=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Subject="LANGUAGE",Name="同词但不同学科"};var concept=kc with{Id=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Type="Concept"};
        var candidate=new BuilderCandidateOutput("有余数除法","MATH","Procedure",3,3,kc.Behavior,kc.Boundary,[Guid.NewGuid()],["原来源引文"],.99m);
        var revision=Guid.NewGuid();var inputs=new[]{new ModelEmbeddingInput(revision,"Candidate",candidate.Name+" "+candidate.MeasurableBehavior+" "+candidate.Boundary),new ModelEmbeddingInput(kc.RevisionId,"KC",kc.Name+" "+kc.Behavior+" "+kc.Boundary),new ModelEmbeddingInput(cross.RevisionId,"KC",cross.Name+" "+cross.Behavior+" "+cross.Boundary),new ModelEmbeddingInput(concept.RevisionId,"KC",concept.Name+" "+concept.Behavior+" "+concept.Boundary)};
        string Response(string model)=>Json.Write(new{model,@object="list",data=new[]{3,1,0,2}.Select(i=>new{@object="embedding",index=i,embedding=new[]{3d,4d,0d}}),usage=new{prompt_tokens=17,total_tokens=17}});
        var raw=new ModelEmbeddingResponse(Response(space.Model),new(17,null,0,null,"LocalMeasured"));var vectors=ModelEmbeddings.Validate(raw,space,inputs);
        Check(vectors[0].EntityRevisionId==revision && vectors[1].EntityRevisionId==kc.RevisionId && Math.Abs(vectors[0].Vector[0]-.6)<1e-10,"Out-of-order response not restored by index");
        Check(ModelEmbeddings.Similarity(vectors[0],vectors[1],space)==1,"Cosine normalization failed");
        Reject(()=>ModelEmbeddings.Similarity(vectors[0],vectors[1] with{Space=(space with{ModelVersion="v2"}).Id},space),"EMBEDDING_SPACE_CONFLICT");
        Reject(()=>ModelEmbeddings.Similarity(vectors[0] with{Dimensions=2,Vector=[1,0]},vectors[1] with{Dimensions=2,Vector=[1,0]},space),"EMBEDDING_SPACE_CONFLICT");
        Reject(()=>ModelEmbeddings.Similarity(vectors[0] with{Space=Retrieval.Space},vectors[1] with{Space=Retrieval.Space},space),"EMBEDDING_SPACE_CONFLICT");
        Reject(()=>ModelEmbeddings.Validate(raw with{Output=Response("other")},space,inputs),"EMBEDDING_MODEL_MISMATCH");
        void Bad(Action<JsonNode> change,string code="EMBEDDING_RESPONSE_INVALID"){var json=JsonNode.Parse(raw.Output)!;change(json);Reject(()=>ModelEmbeddings.Validate(raw with{Output=json.ToJsonString()},space,inputs),code);}
        Bad(n=>n["data"]![0]!["index"]=1);Bad(n=>n["data"]![0]!["index"]=4);Bad(n=>((JsonArray)n["data"]!).RemoveAt(0));Bad(n=>n["data"]![0]!["embedding"]=new JsonArray(1,2));Bad(n=>n["data"]![0]!["embedding"]=new JsonArray(0,0,0),"EMBEDDING_VECTOR_INVALID");Bad(n=>n["data"]![0]!["embedding"]![0]=null);
        Reject(()=>ModelEmbeddings.Validate(raw with{Output=raw.Output.Replace("\"index\":3","\"index\":3,\"index\":3",StringComparison.Ordinal)},space,inputs),"EMBEDDING_RESPONSE_INVALID");
        Reject(()=>ModelEmbeddings.Unit([double.NaN,1,0],3),"EMBEDDING_VECTOR_INVALID");Reject(()=>ModelEmbeddings.Unit([double.PositiveInfinity,1,0],3),"EMBEDDING_VECTOR_INVALID");
        var big=ModelEmbeddings.Unit([double.MaxValue,double.MaxValue,0],3);Check(double.IsFinite(big[0]) && Math.Abs(big[0]-1/Math.Sqrt(2))<1e-10,"Finite large vectors overflowed");
        Reject(()=>ModelEmbeddings.Inputs(space,[inputs[0],inputs[0]]),"EMBEDDING_INPUT_INVALID");Reject(()=>ModelEmbeddings.Inputs(space,[inputs[0] with{Text=new string('数',24001)}]),"EMBEDDING_INPUT_LIMIT");Check(space.Prepare("  ＡＢＣ 数学  ")=="abc 数学","Preprocessing changed");
        var recall=ModelEmbeddings.Candidates(revision,candidate,[kc,cross,concept],space,vectors[0],vectors.Skip(1).ToArray(),10,[]);
        Check(recall.Length==1 && recall[0].KCId==kc.Id && recall[0].EmbeddingSpace==space.Id && recall[0].RetrievalEvidence?.VectorRank==1,"Real vector recall excludes wrong subject/type but must retain cross-grade capability");
        Reject(()=>ModelEmbeddings.Candidates(revision,candidate,[kc],space,vectors[0],[vectors[1] with{TextHash="wrong-text"}],10,[]),"EMBEDDING_SPACE_CONFLICT");
        Reject(()=>ModelEmbeddings.Candidates(revision,candidate,[kc],space,vectors[0],[],10,[]),"EMBEDDING_INDEX_INCOMPLETE");
        Reject(()=>ModelEmbeddings.Candidates(Guid.NewGuid(),candidate,[kc],space,vectors[0],[vectors[1]],10,[]),"EMBEDDING_SPACE_CONFLICT");
        await Transport(space,inputs,raw.Output);
        Console.WriteLine("PASS embedding space/version/dimensions isolation, stable normalized real vectors, complete ordered identity/text hashes, malformed/non-finite/zero vectors rejected; hybrid recall exact subject/type with cross-grade retention; controlled local transport preserves raw response/actual or unknown usage without real model calls");
    }
    static async Task Transport(ModelEmbeddingSpace space,ModelEmbeddingInput[] inputs,string output)
    {
        var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();using var listener=new HttpListener();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();
        var key=Path.Combine(Path.GetTempPath(),"learning-controlled-embedding-key-"+Guid.NewGuid());await File.WriteAllTextAsync(key,"test-only-key");if(!OperatingSystem.IsWindows())File.SetUnixFileMode(key,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        var config=new LocalEmbeddingConfiguration("embedding-local/1",$"http://127.0.0.1:{port}/v1",space,10000);
        try
        {
            async Task<ModelEmbeddingResponse> Call(string body,int status=200)
            {
                var pending=new LocalEmbeddingProvider(config,key).Generate(inputs,CancellationToken.None);var context=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));using var reader=new StreamReader(context.Request.InputStream);var sent=JsonNode.Parse(await reader.ReadToEndAsync())!;
                Check(context.Request.Url!.AbsolutePath=="/v1/embeddings" && sent["model"]!.GetValue<string>()==space.Model && sent["encoding_format"]!.GetValue<string>()=="float" && !sent["truncation"]!.GetValue<bool>() && sent["dimensions"]==null,"Frozen model/truncation/float or exact full-dimensional request lost");
                Check(sent["input"]![0]!.GetValue<string>()==ModelEmbeddings.Inputs(space,inputs)[0],"Preprocessed input differs from hashed content");
                var bytes=Encoding.UTF8.GetBytes(body);context.Response.StatusCode=status;context.Response.ContentLength64=bytes.Length;await context.Response.OutputStream.WriteAsync(bytes);context.Response.Close();return await pending;
            }
            var real=await Call(output);Check(real.Usage.InputTokens==17 && real.Usage.OutputTokens==null && real.Output==output,"Actual usage/raw output lost or unknown invented");ModelEmbeddings.Validate(real,space,inputs);
            var broken=JsonNode.Parse(output)!;broken["data"]![0]!["embedding"]=new JsonArray(1);var returned=await Call(broken.ToJsonString());Check(returned.Usage.InputTokens==17,"Invalid vector erased real returned usage");Reject(()=>ModelEmbeddings.Validate(returned,space,inputs),"EMBEDDING_RESPONSE_INVALID");
            var unknown=await Call("{\"invalid\":true}");Check(unknown.Usage.InputTokens==null && unknown.Usage.OutputTokens==null,"Missing usage silently became zero");
            try{await Call("{}",401);throw new Exception("Authentication error accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_AUTH_FAILED","Wrong auth error");}
            Reject(()=>(config with{Endpoint="https://example.com/v1"}).Validate(),"EMBEDDING_CONFIGURATION_INVALID");
            var waiting=new LocalEmbeddingProvider(config with{TimeoutMilliseconds=100},key).Generate(inputs,CancellationToken.None);var held=await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));try{await waiting;throw new Exception("Timeout invented returned vectors");}catch(OperationCanceledException){}finally{held.Response.Close();}
            if(!OperatingSystem.IsWindows()){File.SetUnixFileMode(key,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead);try{await new LocalEmbeddingProvider(config,key).Generate(inputs,CancellationToken.None);throw new Exception("Non-private key accepted");}catch(ApiError e){Check(e.Code=="LOCAL_PROVIDER_KEY_NOT_PRIVATE","Wrong private-key rejection");}}
        }
        finally{File.Delete(key);}
    }
}
