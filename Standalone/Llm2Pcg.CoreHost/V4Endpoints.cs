using System.Text.Json;
using System.Text.RegularExpressions;
using Llm2Pcg.Core.V4;

internal static class V4Endpoints
{
    private static readonly SemaphoreSlim GenerationGate = new(1,1);
    private static readonly JsonSerializerOptions Options = new() { IncludeFields=true, PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
    private static readonly JsonElement Schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"pcg-request-v4.schema.json"))).RootElement.Clone();
    private static readonly JsonElement Catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"semantic-forest-v1.json"))).RootElement.Clone();

    public static object Execute(JsonElement root, bool includeWorld=true)
    {
        Check(root,Schema,"$");
        SpatialRequest request=JsonSerializer.Deserialize<SpatialRequest>(root.GetRawText(),Options);
        SpatialRequestValidator.Validate(request);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        var timer=System.Diagnostics.Stopwatch.StartNew();
        SpatialWorld world=SpatialGenerator.Generate(request);
        timer.Stop();
        return new { ok=true, experimental=true, unityReady=false, world=includeWorld?world:null,
            summary=new { world.worldHash,world.semanticHash,world.constraints,world.placementDiagnostics,
                placementCount=world.placements.Count,world.width,world.height },
            performance=new { elapsedMs=timer.Elapsed.TotalMilliseconds,allocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated } };
    }
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v4/capabilities",()=>Results.Ok(new {experimental=true,worldTypes=new[]{"Forest","Desert","Snowfield","Swamp"},unityReady=false,catalog=Catalog}));
        app.MapPost("/api/v4/world/generate",(JsonElement root)=>
        {
            if(!GenerationGate.Wait(0))return Results.Json(new {ok=false,code="V4_CORE_BUSY",message="One v4 generation is already running."},statusCode:429);
            try { return Results.Json(Execute(root),Options); }
            catch(ConstraintFailure e) {return Results.Json(new {ok=false,code=e.Code,message=e.Message},statusCode:e.Code=="INVALID_V4_REQUEST"?400:422);}
            finally {GenerationGate.Release();}
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(65536));
    }
    public static int RunBatch()
    {
        string line;
        while((line=Console.ReadLine())!=null)
        {
            try
            {
                if(line.Length>65536)throw new ConstraintFailure("INVALID_V4_REQUEST","Request too large.");
                using var json=JsonDocument.Parse(line,new JsonDocumentOptions{MaxDepth=32});
                JsonElement root=json.RootElement;
                object result=root.TryGetProperty("catalog",out _)?new {ok=true,catalog=Catalog,runtimeTypes=SemanticCatalog.Categories.ToDictionary(c=>c,c=>SemanticCatalog.Types(c))}:
                    Execute(root.GetProperty("request"),root.TryGetProperty("includeWorld",out var full)&&full.ValueKind==JsonValueKind.True);
                Console.WriteLine(JsonSerializer.Serialize(result,Options));
            }
            catch(Exception e) { Console.WriteLine(JsonSerializer.Serialize(new {ok=false,code=e is ConstraintFailure f?f.Code:"INVALID_V4_REQUEST",message=e.Message})); }
        }
        return 0;
    }
    public static void SelfTest()
    {
        var disconnected=new byte[16*16];disconnected[0]=1;disconnected[255]=1;
        if(SpatialGenerator.Crosses(disconnected,16,16,"NorthSouth"))throw new Exception("Disconnected border islands incorrectly counted as a crossing.");
        for(int y=0;y<16;y++)disconnected[y*16+8]=1;
        if(!SpatialGenerator.Crosses(disconnected,16,16,"NorthSouth"))throw new Exception("Connected central river not recognized.");
        foreach(string category in SemanticCatalog.Categories)
        {
            var expected=Catalog.GetProperty("categories").GetProperty(category).GetProperty("types").EnumerateArray().Select(t=>t.GetString());
            if(!expected.SequenceEqual(SemanticCatalog.Types(category)))throw new Exception("Semantic catalog drift: "+category);
        }
        Console.WriteLine("PASS v4 connected-component invariant and semantic catalog parity.");
    }
    // Same deliberately bounded schema vocabulary as Shared/schema-validator.mjs.
    // Raw validation prevents missing numeric fields/default zeros and rejects unknown/duplicate keys.
    private static void Check(JsonElement value,JsonElement schema,string path)
    {
        void Need(bool condition) {if(!condition)throw new ConstraintFailure("INVALID_V4_REQUEST",path+": schema mismatch.");}
        if(schema.TryGetProperty("const",out var constant))Need(value.ValueKind==constant.ValueKind && value.ToString()==constant.ToString());
        if(schema.TryGetProperty("enum",out var choices))Need(choices.EnumerateArray().Any(c=>c.ValueKind==value.ValueKind&&c.ToString()==value.ToString()));
        if(!schema.TryGetProperty("type",out var type))return;
        switch(type.GetString())
        {
            case "object":
                Need(value.ValueKind==JsonValueKind.Object);
                var properties=schema.GetProperty("properties");
                foreach(var key in schema.GetProperty("required").EnumerateArray())Need(value.TryGetProperty(key.GetString(),out _));
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var field in value.EnumerateObject())
                {
                    Need(seen.Add(field.Name)&&properties.TryGetProperty(field.Name,out _));
                    Check(field.Value,properties.GetProperty(field.Name),path+"."+field.Name);
                }
                break;
            case "array":
                Need(value.ValueKind==JsonValueKind.Array);
                if(schema.TryGetProperty("maxItems",out var max))Need(value.GetArrayLength()<=max.GetInt32());
                foreach(var item in value.EnumerateArray())Check(item,schema.GetProperty("items"),path+"[]");
                break;
            case "integer":
                Need(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out _));
                int n=value.GetInt32();
                Need(n>=schema.GetProperty("minimum").GetInt32()&&n<=schema.GetProperty("maximum").GetInt32());
                break;
            case "boolean": Need(value.ValueKind==JsonValueKind.True||value.ValueKind==JsonValueKind.False);break;
            case "string":
                Need(value.ValueKind==JsonValueKind.String);
                string text=value.GetString();
                if(schema.TryGetProperty("pattern",out var pattern))Need(Regex.IsMatch(text,pattern.GetString()));
                if(schema.TryGetProperty("maxLength",out var length))Need(text.Length<=length.GetInt32());
                break;
        }
    }
}
