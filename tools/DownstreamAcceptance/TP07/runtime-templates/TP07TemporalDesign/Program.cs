using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TP07.Runtime;

namespace TP07.TemporalDesign;

internal enum DesignCode { Usage,FreshOutput,MissingConnection,SpecMissing,StageFailed,ChildFailed,ChildTimeout,
    CliIdentity,CliCatalogActivity,GeneratedSource,GeneratedModel,ScaffoldFacet,OwnedSqlScope,GuardNotAccepted,
    GuardCode,GuardOrder,ProtectedStateChanged,SchemaFacet,MissingOwnedObject,UnexpectedHistory,UnexpectedWrite,
    GuardExpression,GuardControl,InvalidRegistry,GeneratedConnectionLeak }
internal sealed class DesignFailure(DesignCode code):InvalidOperationException("TP07_design_failure")
{ internal DesignCode Code{get;}=code; }

internal sealed class DesignExecution(CandidateGuard guard,string output)
{
    internal CandidateGuard Guard{get;}=guard;internal string Output{get;}=output;
    internal string LedgerPath{get;set;}="";internal bool PreserveOwnership{get;set;}
    internal bool DesignTimeOnly{get;set;}
    internal OwnedObjects? Owned{get;set;}internal DesignSpec? Spec{get;set;}internal DesignAudit Audit{get;}=new(guard);
    internal Dictionary<string,object?> Report{get;}=new(){["schema_version"]=1,["task"]="TP07-D",["candidate_acceptance"]=false,["full_temporal_acceptance"]=false,["cases"]=new List<Dictionary<string,object?>>()};
    internal HashSet<string> Tables=>Owned?.Entries.Select(e=>e.Table).ToHashSet(StringComparer.Ordinal)??[];
    internal void Save()=>SafeEvidence.Write(Path.Combine(Output,"design-report.json"),Report);
}

public static class Program
{
    private static CandidateGuard? startupGuard;
    private static readonly List<object> verifiedStartupIdentities=[];
    private static ProtocolActivity? processActivity;
    private static TaskCli? startupCli;
    internal static DesignExecution Current{get;private set;}=null!;
    internal static DbCommand? ActiveCommand{get;set;}
    internal static readonly CancellationTokenSource Deadline=new(TimeSpan.FromMinutes(25));
    internal static CancellationToken Token=>Deadline.Token;
    internal static readonly string[] Registry=["R05-CATALOG","R05-CLI-SCAFFOLD","R05-COMPILE-GENERATED","R05-SCAFFOLD6-MICRO","R05-SCAFFOLD6-NULL","R05-SNAPSHOT-MIGRATION",
        "DOWN-G00-expression-dt","DOWN-G00-expression-dto","DOWN-G01-extremes-dt","DOWN-G01-extremes-dto",
        "DOWN-G02-control-dt","DOWN-G02-control-dto","R06-UP-dt","R06-UP-dto","DOWN-G03-refusal-dt","DOWN-G03-refusal-dto",
        "DOWN-G04-success-dt","DOWN-G04-success-dto","DOWN-G05-scope",
        "R06-TLS6-dt","R06-TLS6-dto"];

    [ModuleInitializer]
    public static void InitializeHost()
    {
        startupGuard=CandidateGuard.VerifyProcessEnvironment(typeof(Program).Assembly);
        verifiedStartupIdentities.Add(startupGuard.RuntimeIdentity!);
        processActivity=new ProtocolActivity();
        if(Environment.GetEnvironmentVariable("TP07_CLI_TOOL_REQUIRED")=="1")
            startupCli=PreparedCli(SafeEvidence.Load(Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST")??"").GetProperty("replay_root").GetString()!);
        string? receipt=Environment.GetEnvironmentVariable("TP07_PROCESS_RECEIPT");
        if(!string.IsNullOrEmpty(receipt))
        {
            Require(!File.Exists(receipt)&&!Directory.Exists(receipt),DesignCode.FreshOutput);
            WriteProcessReceipt(receipt,false);
            AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{try{WriteProcessReceipt(receipt,true);}catch{Environment.ExitCode=3;}};
        }
    }
    private static void WriteProcessReceipt(string path,bool final)=>SafeEvidence.Write(path,new
    {
        schema_version=1,task="TP07-D-process",final,pid=Environment.ProcessId,
        startup_assembly_sha256=SafeEvidence.HashFile(typeof(Program).Assembly.Location),
        startup_identity=startupGuard!.RuntimeIdentity,
        verified_startup_assemblies=verifiedStartupIdentities.ToArray(),
        entry_assembly=Assembly.GetEntryAssembly()?.GetName().Name,
        framework_assembly_sha256=SafeEvidence.HashFile(typeof(Host).Assembly.Location),
        prepared_cli_tool_identity=startupCli?.Identity,
        protocol_frames=processActivity!.Frames.ToArray(),observer_failed=processActivity.Failed
    });
    public static void VerifyGeneratedHost(Assembly startupAssembly)
    {verifiedStartupIdentities.Add(CandidateGuard.VerifyProcessEnvironment(startupAssembly).RuntimeIdentity!);}
    public static DbContextOptions<TContext> GeneratedOptions<TContext>() where TContext:DbContext
    {
        var guard=startupGuard??CandidateGuard.VerifyProcessEnvironment(typeof(Program).Assembly);
        var builder=new DbContextOptionsBuilder<TContext>().UseDameng(guard.BuildConnectionString())
            .EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
        if(Current!=null)builder.AddInterceptors(Current.Audit,new DesignConnectionAudit(guard));
        return builder.Options;
    }
    public static void WriteGeneratedSnapshotProof(DbContext six,DbContext metadata,string sixTable,string metadataTable,string path)
        =>ScaffoldCases.WriteSnapshotProof(six,metadata,sixTable,metadataTable,path);
    public static IHostBuilder CreateHostBuilder(string[] args)=>NamedHost(args);
    public static IHostBuilder NamedHost(string[] args)=>Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((_,configuration)=>{configuration.Sources.Clear();configuration.AddInMemoryCollection(
            new Dictionary<string,string?>{["ConnectionStrings:TP07"]=startupGuard!.BuildConnectionString()});})
        .ConfigureLogging(logging=>logging.ClearProviders());

    public static async Task<int> Main(string[] args)
    {
        int exit=1;
        try
        {
            var options=Arguments(args);
            string profile=Get(options,"profile"),output=Path.GetFullPath(Get(options,"output"));
            var guard=startupGuard??throw new DesignFailure(DesignCode.CliIdentity);
            Require(profile==guard.Profile,DesignCode.Usage);
            Require(!Directory.Exists(output)&&!File.Exists(output),DesignCode.FreshOutput);Directory.CreateDirectory(output);
            Current=new(guard,output);Current.Report["profile"]=profile;Current.Report["identity"]=guard.VerifyRuntime(typeof(Program).Assembly);
            Current.Report["registry"]=Registry;Current.Report["status"]="running";Current.Save();
            string group=Get(options,"group");Current.Report["group"]=group;
            Current.Report["registry_contracts"]=Registry.Select(id=>new{case_id=id,group=id switch
            {"R05-CATALOG" or "R05-CLI-SCAFFOLD"=>"scaffold","R05-COMPILE-GENERATED" or "R05-SCAFFOLD6-MICRO" or "R05-SCAFFOLD6-NULL"=>"scaffold-consume","R05-SNAPSHOT-MIGRATION"=>"scaffold-finalize",_=>"guarded"},
                profiles=id.StartsWith("R06-TLS6",StringComparison.Ordinal)?new[]{"tls"}:id.StartsWith("R05",StringComparison.Ordinal)||id=="DOWN-G05-scope"?new[]{"shared","tls"}:new[]{"shared"},expected_only=id=="DOWN-G05-scope"}).ToArray();
            Current.Report["build_manifest_path"]=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST");
            Current.Report["build_manifest_sha256"]=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256");
            if(group is "scaffold-consume" or "scaffold-finalize")ScaffoldCases.LoadContinuation(options);
            else{Current.LedgerPath=Path.Combine(output,"ownership.json");Current.Owned=new OwnedObjects(guard,Current.LedgerPath);}
            bool stageCompleted=false;
            try
            {
                if(group=="scaffold")await ScaffoldCases.Prepare(options);
                else if(group=="scaffold-consume")await ScaffoldCases.Consume(options);
                else if(group=="scaffold-finalize")await ScaffoldCases.Finalize(options);
                else if(group=="guarded")await GuardedMigrationCases.Run();
                else throw new DesignFailure(DesignCode.Usage);
                Require(!ObserverFailed,DesignCode.CliIdentity);
                stageCompleted=true;
                if(!Current.PreserveOwnership)Current.Report["status"]="execution_complete";exit=0;
            }
            finally
            {
                if(Current.PreserveOwnership&&stageCompleted)
                {
                    Current.Report["cleanup_verified"]=false;Current.Report["cleanup_status"]="pending_continuation_or_independent_runner_finally";
                    string snapshot=Path.Combine(output,"ownership-snapshot.json");File.Copy(Current.LedgerPath,snapshot,false);
                    Current.Report["ownership_snapshot_sha256"]=SafeEvidence.HashFile(snapshot);
                }
                else
                {
                    try{Current.Report["cleanup"]=Current.Owned!.Cleanup();Current.Report["cleanup_verified"]=true;}
                    catch(Exception cleanup){Current.Report["cleanup_verified"]=false;Current.Report["cleanup_error"]=Error(cleanup);exit=1;throw;}
                }
                Current.Report["ownership_ledger_active"]=Current.LedgerPath;Current.Report["ownership_ledger_sha256"]=SafeEvidence.HashFile(Current.LedgerPath);
            }
        }
        catch(Exception error)
        {
            if(Current!=null){Current.Report["status"]="execution_failed";Current.Report["error"]=Error(error);}
        }
        if(Current!=null){Current.Report["exit_code"]=exit;Current.Report["commands"]=Current.Audit.Commands;Current.Report["protocol_frames"]=processActivity!.Frames;Current.Report["sent_frames"]=processActivity.Sends;Current.Report["observer_failed"]=processActivity.Failed;Current.Save();}
        return exit;
    }

    internal static DbContextOptions<DesignContext> Options()=>new DbContextOptionsBuilder<DesignContext>()
        .UseDameng(Current.Guard.BuildConnectionString(),options=>options.MigrationsHistoryTable(Current.Spec!.History,CandidateGuard.User))
        .ReplaceService<IModelCacheKeyFactory,DesignModelCacheKey>().AddInterceptors(Current.Audit,new DesignConnectionAudit(Current.Guard))
        .EnableSensitiveDataLogging(false).EnableDetailedErrors(false).Options;
    internal static DesignContext Context()=>new(Options());
    internal static IReadOnlyList<ProtocolFrame> ProtocolFrames=>processActivity!.Frames;
    internal static IReadOnlyList<SentFrame> SentFrames=>processActivity!.Sends;
    internal static bool ObserverFailed=>processActivity!.Failed;
    internal static void BindDesignFactory(DesignSpec spec,OwnedObjects owned)
    {
        string receipt=Environment.GetEnvironmentVariable("TP07_PROCESS_RECEIPT")??throw new DesignFailure(DesignCode.CliIdentity);
        Current=new(startupGuard!,Path.GetDirectoryName(Path.GetFullPath(receipt))!){Spec=spec,Owned=owned,DesignTimeOnly=true};
    }
    internal static void Require(bool condition,DesignCode code){if(!condition)throw new DesignFailure(code);}
    internal static object Error(Exception error)
    {
        while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
        return error is DesignFailure own?new{type=error.GetType().FullName,code=own.Code.ToString(),number=(int?)null}:SafeEvidence.Safe(error);
    }
    internal static async Task Case(string id,Func<Dictionary<string,object?>,Task> action)
    {
        Require(Registry.Contains(id,StringComparer.Ordinal),DesignCode.InvalidRegistry);Current.Audit.Stage=id;
        var record=new Dictionary<string,object?>{["case_id"]=id,["status"]="attempted",["expected_only"]=false};
        ((List<Dictionary<string,object?>>)Current.Report["cases"]!).Add(record);Current.Save();
        try{await action(record);record["status"]="completed";Current.Save();}
        catch(Exception error){record["status"]="failed";record["error"]=Error(error);Current.Save();throw;}
    }
    internal static Dictionary<string,string> Arguments(string[] args)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i+=2){Require(i+1<args.Length&&args[i].StartsWith("--",StringComparison.Ordinal),DesignCode.Usage);Require(result.TryAdd(args[i][2..],args[i+1]),DesignCode.Usage);}
        Require(result.Keys.All(k=>k is "profile" or "output" or "group" or "project" or "replay-root" or "generation-report" or "generation-report-sha256"),DesignCode.Usage);return result;
    }
    internal static string Get(IReadOnlyDictionary<string,string> options,string key)=>options.TryGetValue(key,out string? value)?value:throw new DesignFailure(DesignCode.Usage);
    internal sealed record TaskCli(string Dotnet,string Home,string EntryPoint,object Identity);
    internal static TaskCli PreparedCli(string replay)
    {
        string proof=Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST")??"",proofSha=Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST_SHA256")??"";
        Require(System.Text.RegularExpressions.Regex.IsMatch(proofSha,"\\A[a-f0-9]{64}\\z")&&SafeEvidence.HashFile(proof)==proofSha,DesignCode.CliIdentity);
        var manifest=SafeEvidence.Load(proof);
        Require(manifest.GetProperty("schema_version").GetInt32()==1&&manifest.GetProperty("task").GetString()=="TP07-D-cli-tool"
            &&Path.GetFullPath(manifest.GetProperty("replay_root").GetString()!)==Path.GetFullPath(replay),DesignCode.CliIdentity);
        string Absolute(string name){string value=manifest.GetProperty(name).GetString()!;Require(Path.IsPathFullyQualified(value)&&value==Path.GetFullPath(value),DesignCode.CliIdentity);return value;}
        string taskRoot=Absolute("task_root"),home=Absolute("cli_home"),toolRoot=Absolute("tool_root");
        bool Inside(string path)=>Path.GetFullPath(path).StartsWith(taskRoot+Path.DirectorySeparatorChar,StringComparison.Ordinal);
        Require(Directory.Exists(taskRoot)&&Directory.Exists(home)&&Directory.Exists(toolRoot)&&Inside(home)&&Inside(toolRoot),DesignCode.CliIdentity);
        if(Environment.GetEnvironmentVariable("TP07_CLI_TOOL_REQUIRED")=="1")Require(Environment.GetEnvironmentVariable("DOTNET_CLI_HOME")==home,DesignCode.CliIdentity);
        string probe=Path.Combine(home,"tp07-write-probe-"+Guid.NewGuid().ToString("N"));
        using(var writable=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose))writable.WriteByte(0);
        string Artifact(JsonElement artifact)
        {
            string path=artifact.GetProperty("path").GetString()!,hash=artifact.GetProperty("sha256").GetString()!;
            Require(Path.IsPathFullyQualified(path)&&path==Path.GetFullPath(path)
                &&System.Text.RegularExpressions.Regex.IsMatch(hash,"\\A[a-f0-9]{64}\\z")&&SafeEvidence.HashFile(path)==hash,DesignCode.CliIdentity);return path;
        }
        string dotnet=Artifact(manifest.GetProperty("dotnet_host"));
        string local=Artifact(manifest.GetProperty("local_tool_manifest"));
        Require(local==Path.Combine(Path.GetFullPath(replay),".config","dotnet-tools.json"),DesignCode.CliIdentity);
        var localTool=SafeEvidence.Load(local).GetProperty("tools").GetProperty("dotnet-ef");
        Require(localTool.GetProperty("version").GetString()=="10.0.12"&&localTool.GetProperty("commands").GetArrayLength()==1
            &&localTool.GetProperty("commands")[0].GetString()=="dotnet-ef"&&!localTool.GetProperty("rollForward").GetBoolean(),DesignCode.CliIdentity);
        string package=Artifact(manifest.GetProperty("package"));Require(Inside(package),DesignCode.CliIdentity);
        using var zip=ZipFile.OpenRead(package);
        using(var nuspec=zip.GetEntry("dotnet-ef.nuspec")!.Open())
        {
            var metadata=XDocument.Load(nuspec).Descendants().Single(element=>element.Name.LocalName=="metadata");
            Require(metadata.Elements().Single(element=>element.Name.LocalName=="id").Value=="dotnet-ef"
                &&metadata.Elements().Single(element=>element.Name.LocalName=="version").Value=="10.0.12",DesignCode.CliIdentity);
        }
        string[] files=["DotnetToolSettings.xml","dotnet-ef.dll","dotnet-ef.deps.json","dotnet-ef.runtimeconfig.json","tools/net8.0/any/ef.dll","tools/net8.0/any/ef.runtimeconfig.json"];
        var hashes=manifest.GetProperty("tool_files");Require(hashes.EnumerateObject().Count()==files.Length,DesignCode.CliIdentity);
        foreach(string file in files)
        {
            string path=Path.Combine(toolRoot,file),expected=hashes.GetProperty(file).GetString()!;
            Require(SafeEvidence.HashFile(path)==expected,DesignCode.CliIdentity);
            using var input=zip.GetEntry("tools/net8.0/any/"+file)!.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);
            Require(SafeEvidence.Hash(bytes.ToArray())==expected,DesignCode.CliIdentity);
        }
        var settings=XDocument.Load(Path.Combine(toolRoot,"DotnetToolSettings.xml")).Descendants("Command").Single();
        Require((string?)settings.Attribute("Name")=="dotnet-ef"&&(string?)settings.Attribute("EntryPoint")=="dotnet-ef.dll"
            &&(string?)settings.Attribute("Runner")=="dotnet",DesignCode.CliIdentity);
        // These caches, if retained by external preparation, are artifacts only. Exact exec never resolves them.
        var caches=new Dictionary<string,string>(StringComparer.Ordinal);
        if(manifest.TryGetProperty("preparation_cache_files",out var cached))foreach(var item in cached.EnumerateObject())
        {Require(caches.Count<32&&Path.IsPathFullyQualified(item.Name)&&Path.GetFullPath(item.Name).StartsWith(home+Path.DirectorySeparatorChar,StringComparison.Ordinal)
            &&SafeEvidence.HashFile(item.Name)==item.Value.GetString(),DesignCode.CliIdentity);caches.Add(item.Name,item.Value.GetString()!);}
        object identity=new{manifest_sha256=proofSha,version="10.0.12",route="exact_dotnet_exec_no_tool_resolver",cli_home=home,
            dotnet_host=new{path=dotnet,sha256=SafeEvidence.HashFile(dotnet)},local_tool_manifest_sha256=SafeEvidence.HashFile(local),
            package_sha256=SafeEvidence.HashFile(package),tool_root=toolRoot,tool_files=files.ToDictionary(file=>file,file=>SafeEvidence.HashFile(Path.Combine(toolRoot,file))),
            preparation_cache_files=caches,resolver_cache_used=false};
        if(Current!=null){Current.Report["cli_tool_manifest_path"]=Path.GetFullPath(proof);Current.Report["cli_tool_manifest_sha256"]=proofSha;Current.Report["cli_tool_identity"]=identity;Current.Save();}
        return new(dotnet,home,Path.Combine(toolRoot,"dotnet-ef.dll"),identity);
    }
    internal static async Task<int> Child(string cwd,IReadOnlyList<string> arguments,string receipt,bool requireCatalog=false,bool requireProcess=false)
    {
        Require(!File.Exists(receipt)&&!Directory.Exists(receipt),DesignCode.FreshOutput);
        string replay=SafeEvidence.Load(Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST")!).GetProperty("replay_root").GetString()!;
        var prepared=PreparedCli(replay);IReadOnlyList<string> actual=arguments;
        if(arguments.Count>=4&&arguments[0]=="tool"&&arguments[1]=="run"&&arguments[2]=="dotnet-ef"&&arguments[3]=="--")
            actual=new[]{"exec",prepared.EntryPoint}.Concat(arguments.Skip(4)).ToArray();
        else Require(arguments.Count>=2&&arguments[0]=="exec"&&arguments[1]==Path.Combine(cwd,"bin/Debug/net10.0/TP07GeneratedConsumer.dll"),DesignCode.Usage);
        var start=new ProcessStartInfo(prepared.Dotnet){WorkingDirectory=cwd,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        foreach(string arg in actual)start.ArgumentList.Add(arg);
        var invocation=new Dictionary<string,object?>{["stage"]=Current.Audit.Stage,["cwd"]=cwd,["argv"]=actual.ToArray(),["declared_argv"]=arguments.ToArray(),["executable"]=prepared.Dotnet,["tool_identity"]=prepared.Identity,["status"]="attempted",["process_receipt"]=receipt};
        if(!Current.Report.TryGetValue("child_invocations",out object? recorded))Current.Report["child_invocations"]=recorded=new List<Dictionary<string,object?>>();
        ((List<Dictionary<string,object?>>)recorded!).Add(invocation);Current.Save();
        start.Environment["TP07_PROCESS_RECEIPT"]=receipt;
        start.Environment["TP07_CLI_TOOL_REQUIRED"]="1";
        start.Environment["DOTNET_CLI_HOME"]=prepared.Home;start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"]="1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"]="1";
        // Only public paths/settings enter argv/env here. TEST credentials remain inherited from wrapper.
        using var process=Process.Start(start)??throw new DesignFailure(DesignCode.ChildFailed);
        invocation["process_id"]=process.Id;
        Task consumeOut=process.StandardOutput.BaseStream.CopyToAsync(Stream.Null,Token),consumeError=process.StandardError.BaseStream.CopyToAsync(Stream.Null,Token);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(Token);timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try{await process.WaitForExitAsync(timeout.Token);await Task.WhenAll(consumeOut,consumeError);}
        catch(OperationCanceledException){try{process.Kill(entireProcessTree:true);}catch{}throw new DesignFailure(DesignCode.ChildTimeout);}
        invocation["exit_code"]=process.ExitCode;Require(process.ExitCode==0,DesignCode.ChildFailed);
        if(requireCatalog||requireProcess)
        {
            JsonElement observed=SafeEvidence.Load(receipt);Require(observed.GetProperty("final").GetBoolean()&&!observed.GetProperty("observer_failed").GetBoolean(),DesignCode.CliIdentity);
            Require(observed.GetProperty("startup_identity").ValueKind==JsonValueKind.Object
                &&observed.GetProperty("verified_startup_assemblies").GetArrayLength()>0
                &&observed.GetProperty("prepared_cli_tool_identity").GetProperty("manifest_sha256").GetString()==Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST_SHA256"),DesignCode.CliIdentity);
            if(requireCatalog)Require(observed.GetProperty("protocol_frames").EnumerateArray().Any(frame=>frame.GetProperty("request_opcode").GetInt32() is 5 or 6 or 13),DesignCode.CliCatalogActivity);
        }
        invocation["status"]="completed";if(File.Exists(receipt))invocation["process_receipt_sha256"]=SafeEvidence.HashFile(receipt);Current.Save();
        return process.ExitCode;
    }
    internal static string Quote(string identifier)=>'"'+SafeEvidence.Identifier(identifier)+'"';
    internal static async Task<int> Execute(DbConnection connection,string sql)
    {await using var command=CandidateGuard.Command(connection,sql);Current.Audit.StartRaw(command,"ExecuteNonQueryAsync");try{int affected=await command.ExecuteNonQueryAsync(Token);Current.Audit.EndRaw(command,"ExecuteNonQueryAsync");return affected;}catch(Exception error){Current.Audit.FailRaw(command,error);throw;}finally{ActiveCommand=null;}}
    internal static async Task<object?> Scalar(DbConnection connection,string sql)
    {await using var command=CandidateGuard.Command(connection,sql);Current.Audit.StartRaw(command,"ExecuteScalarAsync");try{object? value=await command.ExecuteScalarAsync(Token);Current.Audit.EndRaw(command,"ExecuteScalarAsync");return value;}catch(Exception error){Current.Audit.FailRaw(command,error);throw;}finally{ActiveCommand=null;}}

    private sealed class ProtocolActivity
    {
        internal List<ProtocolFrame> Frames{get;}=[];internal List<SentFrame> Sends{get;}=[];internal bool Failed{get;private set;}
        internal ProtocolActivity()
        {
            var hook=typeof(W.Dm.DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")!.GetField("AfterFrame",BindingFlags.Static|BindingFlags.NonPublic)!;
            Require(hook.GetValue(null)==null,DesignCode.CliIdentity);
            hook.SetValue(null,(Action<short,short,int,int>)((request,response,code,length)=>{try{Frames.Add(new(Frames.Count,Current?.Audit.Stage??"cli",request,response,code,length,ActiveCommand==null?null:DesignAudit.Id(ActiveCommand),ActiveCommand==null?null:DesignAudit.StatementId(ActiveCommand)));}catch{Failed=true;}}));
            var send=typeof(W.Dm.DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")!.GetField("AfterFrameSent",BindingFlags.Static|BindingFlags.NonPublic)!;
            Require(send.GetValue(null)==null,DesignCode.CliIdentity);
            MethodInfo method=GetType().GetMethod(nameof(Sent),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(send.FieldType.GenericTypeArguments[0]);
            send.SetValue(null,Delegate.CreateDelegate(send.FieldType,this,method));
        }
        private void Sent<T>(T identity,short opcode)
        {
            try
            {
                object? value=identity;
                long Number(string name)=>value?.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value) is long number?number:throw new DesignFailure(DesignCode.CliIdentity);
                Sends.Add(new(Sends.Count,Current?.Audit.Stage??"cli",opcode,Number("SessionId"),Number("LeaseGeneration"),Number("ExecutionId"),Number("InvocationId")));
            }catch{Failed=true;}
        }
    }
}
internal sealed record ProtocolFrame(int Sequence,string Stage,int RequestOpcode,int ResponseOpcode,int SqlCode,int BodyLength,int? CommandId,int? StatementId);
internal sealed record SentFrame(int Sequence,string Stage,int Opcode,long SessionId,long LeaseGeneration,long ExecutionId,long InvocationId);
