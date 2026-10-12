using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using W.Dm;

internal static partial class Program
{
    static readonly CancellationTokenSource Lifetime = new(TimeSpan.FromMinutes(18));
    static CancellationToken Token => Lifetime.Token;
    static int NextId = 10001;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ConnectionIdentity> ConnectionIds = new();
    sealed record ConnectionIdentity(string Value);
    static string ConnectionId(object connection) => ConnectionIds.GetValue(connection, _ => new(Guid.NewGuid().ToString("N"))).Value;
    static Trace? Observation;
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "offline") return Offline(args);
        int exit = 1;
        try
        {
            Need(args.Length == 9 && args[0] is "run" or "cleanup", "usage");
            Identity = LoadStrict(args[1]); Profile = args[3]; Repo = args[5]; OwnershipPath = args[6]; Output = args[7];
            Need(Profile is "shared" or "tls" && args[4] == "supplemental", "configuration");
            Need(Environment.Version.Major == 10, "runtime_net10");
            Report["schema_version"] = 1; Report["task"] = "TP-06-supplement"; Report["runtime"] = Environment.Version.ToString();
            Report["expected_only"] = false; Report["database_accessed"] = true;
            Report["lane"] = "supplemental";
            Report["capabilities_sha256"]=LoadStrict(args[8]).GetProperty("capabilities_sha256").GetString();
            Report["tool_mvid"]=typeof(Program).Assembly.ManifestModule.ModuleVersionId.ToString("D");
            Report["tool_dll_sha256"]=Hash(File.ReadAllBytes(typeof(Program).Assembly.Location));
            Report["deps_sha256"]=Hash(File.ReadAllBytes(Path.ChangeExtension(typeof(Program).Assembly.Location,".deps.json")));
            Report["mode"] = args[0]; Report["profile"] = Profile; Report["phase"] = "candidate"; Report["cases"] = Cases;
            Report["identity_sha256"] = Hash(File.ReadAllBytes(args[1])); Report["source_manifest_sha256"] = Hash(File.ReadAllBytes(args[8]));
            Report["product_source_manifest_sha256"] = Identity.GetProperty("producer").GetProperty("source_manifest_sha256").GetString();
            Report["producer_source_head"] = Identity.GetProperty("producer").GetProperty("source_head").GetString();
            Report["comparison_head"] = Identity.GetProperty("comparison_head").GetString();
            CheckExactCandidate(args[1],args[8]);
            Report["identities"] = CheckIdentities(args[2]); CheckSourceBinding(args[8]); CheckTools(args[8]);
            Need(Identity.GetProperty("phase").GetString() == "candidate", "candidate_required");
            CaseManifest = LoadStrict(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[8]))!,"case-manifest.json"));
            Configure();
            using (var w = Open("W")) { Report["W_profile"] = ProfileInfo(w); }
            if (Profile == "shared" && args[0]=="run") using (var o = Open("O")) { Report["O_profile"] = ProfileInfo(o); }
            else Report["O_profile"] = new { status = Profile=="tls"?"closed":"not_used", reason = Profile=="tls"?"no_verified_strict_official_tls_configuration":"cleanup_requires_verified_W_only" };
            Report["server_identity_verified"] = true;
            if (args[0] == "cleanup") { LoadOwnership(); Cleanup(); }
            else
            {
                ReserveSupplemental();
                try
                {
                    using (Observation = new Trace())
                    {
                        await Transactions(); await Bindings(); await Native(); await Leading(); await Readers();
                    }
                    Report["status"] = PendingLeading?PendingLeadingStatus:"observed_complete";
                }
                finally { Cleanup(); }
            }
            Report["case_count"] = Cases.Count;
            exit = Report.GetValueOrDefault("cleanup_verified") is true && !PendingLeading ? 0 : 1;
        }
        catch (Exception error)
        {
            Report["status"] = "execution_failed"; Report["failure_stage"] = Stage; Report["failure"] = Safe(error);
            if (Reserved && Report.GetValueOrDefault("cleanup_verified") is not true) Cleanup();
        }
        Report["process_exit_code"] = exit;
        try { Write(Output, Report); } catch { return 2; }
        return exit;
    }
    static void CheckExactCandidate(string identities,string manifest)
    {
        Need(Hash(File.ReadAllBytes(identities))=="e14eb1baca9a3c07b04f988dd3bcdb652048fd1a61211ad8c2cb117a9e0955a3","exact_identity_bytes");
        var producer=Identity.GetProperty("producer"); var w=Identity.GetProperty("W");
        Need(w.GetProperty("version").GetString()=="0.1.0-temporal.20261008144915" && w.GetProperty("package_sha256").GetString()=="da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d" && w.GetProperty("dll_sha256").GetString()=="843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980" && w.GetProperty("mvid").GetString()=="aa23e673-957c-4b1e-bf0f-291ff60077e6","exact_candidate_identity");
        Need(producer.GetProperty("source_manifest_sha256").GetString()=="8e7d1dda7f9b8dcf47f31e0918507fea449e1395b7487b9b756b15ec0267030f" && producer.GetProperty("source_archive_sha256").GetString()=="836e47fce12756256b483f0e14717f63002be31059555652782d96331e694441","exact_candidate_source_archive");
        Need(LoadStrict(manifest).GetProperty("capabilities_sha256").GetString()=="f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa","exact_capability_bytes");
    }
    static void CheckTools(string manifest)
    {
        var value = LoadStrict(manifest); string directory = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        string root = Path.GetFullPath(Path.Combine(directory, ".."));
        Need(value.GetProperty("task").GetString() == "TP-06-supplement", "tool_task");
        var paths=new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.GetProperty("tools").EnumerateArray())
        {
            string path = item.GetProperty("path").GetString()!; Need(paths.Add(path),"duplicate_tool_path");
            Need(!Path.IsPathRooted(path) && !path.Split('/').Any(p => p is ".." or ".local" or "secrets") && !path.Contains('\\'), "tool_path");
            Need(Hash(File.ReadAllBytes(Path.Combine(root, path))) == item.GetProperty("sha256").GetString(), "tool_hash");
        }
        foreach(string required in new[]{"Program.cs","Safety.cs","Cases.cs","Observations.cs","SequentialObservations.cs","Leading.cs","Readers.cs","Offline.cs","TemporalCandidateAcceptance.csproj","global.json","run.py","contracts.py","validate.py","case-manifest.json","v11-contracts.py","v11-packed-oracle.py"}) Need(paths.Contains("build/"+required),"required_tool_input");
        Need(LoadStrict(Path.Combine(directory,"case-manifest.json")).GetProperty("schema_version").GetInt32() == 1, "case_manifest");
        Report["case_manifest_sha256"] = Hash(File.ReadAllBytes(Path.Combine(directory,"case-manifest.json")));
    }
    static void ReserveSupplemental()
    {
        Stage = "reserve_ownership"; string nonce = Guid.NewGuid().ToString("N").ToUpperInvariant(); Table = "TPN_" + nonce[..24];
        var initial = new Ownership(1, nonce, Table, Profile, ExpectedVersion, Identity.GetProperty("W").GetProperty("package_sha256").GetString()!, (string)Report["identity_sha256"]!, true, true, "OWN_" + nonce[..24], "reserved");
        CreateWithLifecycle(initial, new LiveOwnershipDatabase(), ownership => {
            CurrentOwnership = ownership;
            if (!Reserved) { using var f = new FileStream(OwnershipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); JsonSerializer.Serialize(f, ownership, Json); Reserved = true; }
            else Write(OwnershipPath, ownership);
            Report["owned_table"] = Table; Report["creation_state"] = ownership.CreationState;
        });
        Report["ddl_implicit_commit"] = "assumed_no_ddl_rollback_claim";
        if (Profile == "shared") { using var c = Open("W"); Exec(c, $"ALTER TABLE {Table} ADD D7 TIMESTAMP(7)"); Exec(c, $"ALTER TABLE {Table} ADD Z7 DATETIME(7) WITH TIME ZONE"); }
    }
    static async Task<DmConnection> OpenAsync()
    {
        var c = new DmConnection(WSettings);
        try
        {
            await c.OpenAsync(Token); string user = Convert.ToString(await ScalarAsync(c,"SELECT USER FROM DUAL"),CultureInfo.InvariantCulture)?.Trim() ?? "";
            string schema = Convert.ToString(await ScalarAsync(c,"SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL"),CultureInfo.InvariantCulture)?.Trim() ?? "";
            Need(user == User && schema == User,"async_test_identity");
            var info = ProfileInfo(c,user,schema); Need(info.ServerVersion == ExpectedVersion && info.MessageVersion == (Profile=="shared"?21:11) && info.Encoding == (Profile=="shared"?"utf-8":"gb18030"),"async_server_version_encoding_message"); return c;
        }
        catch (Exception e) { await c.DisposeAsync(); throw new ProbeInfrastructureException(e); }
    }
    static async Task<object?> ScalarAsync(DbConnection c,string sql) { await using var cmd = Command(c,sql); return await cmd.ExecuteScalarAsync(Token); }
    static Dictionary<string,object?> NewCase(string id)
    {
        Stage = id; if (Observation is not null) Observation.Stage = id;
        var value = new Dictionary<string,object?> { ["case_id"] = id,["status"] = "observed",["stages"] = new List<Dictionary<string,object?>>() }; Cases.Add(value); return value;
    }
    static Dictionary<string,object?> NewStage(Dictionary<string,object?> item,string id,string api,int prepares=0)
    {
        var value = new Dictionary<string,object?> { ["stage_id"] = id,["api"] = api,["prepare_calls"] = prepares,["status"] = "observed",["error"] = null,["actual_receipts"] = new List<string>(),["observations"] = new List<object>() };
        ((List<Dictionary<string,object?>>)item["stages"]!).Add(value); return value;
    }
    static DmParameter Param(DbCommand cmd,object value,DbType? type=null,int? scale=null,DmDbType? provider=null)
    {
        var p = (DmParameter)cmd.CreateParameter(); p.ParameterName="v";p.Value=value; if(type.HasValue)p.DbType=type.Value; if(provider.HasValue)p.DmSqlType=provider.Value;
        if(scale.HasValue)p.Scale=(byte)scale.Value; cmd.Parameters.Add(p); return p;
    }
    static object Parameter(DmParameter p) => new { clr_type=p.Value?.GetType().FullName,value_ticks=Observe(()=>p.Value).Ticks,value_is_dbnull=p.Value is DBNull,declared_db_type=p.DbType.ToString(),declared_dm_type=p.DmSqlType.ToString(),scale=(int)p.Scale,precision=(int)p.Precision,db_type_explicit=ReadField(p,"explicitDbType") is true,dm_type_explicit=ReadField(p,"explicitDmSqlType") is true,scale_explicit=ReadField(p,"m_SetScaleFlag") is true };
    static void Snapshot(Dictionary<string,object?> stage,DbCommand command,DbDataReader reader)
    {
        var selected=SelectOwnedStatement(command,reader); stage["statement_owner"]=selected.Owner; stage["statement_id"]=selected.Id;
        stage["parameter_metadata"]=ParameterMetadata(command,reader).FirstOrDefault();
        var info=selected.Statement is null?null:Invoke(selected.Statement,"H"); var values=info is null?null:Invoke(info,"GetParamsInfo");
        object? first=(values as Array)?.GetValue(0); object? binding=first?.GetType().GetProperty("TemporalBinding",Flags)?.GetValue(first);
        object? scale=binding?.GetType().GetProperty("EncodedScale",Flags)?.GetValue(binding)??binding?.GetType().GetProperty("EncodingScale",Flags)?.GetValue(binding);
        stage["encoded_scale"]=scale is int n?n:null; stage["encoding_source"]=scale is int?"immutable_temporal_binding":"unobservable";
    }
    sealed class Trace:IDisposable
    {
        readonly FieldInfo Hook,SendHook; readonly object? Previous,PreviousSend; internal string Stage="none";
        internal readonly List<Frame> Frames=[]; internal readonly List<SentFrame> SentFrames=[]; internal Action<Frame>? Observer; internal bool ObserverFailed;
        internal Trace()
        {
            var assembly=typeof(DmConnection).Assembly;
            Hook=assembly.GetType("W.Dm.Internal.Legacy.A.DmResultProtocolTrace")?.GetField("AfterFrame",BindingFlags.Static|BindingFlags.NonPublic)??throw new InvalidOperationException("numeric_trace_missing");
            SendHook=assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")?.GetField("AfterFrameSent",BindingFlags.Static|BindingFlags.NonPublic)??throw new InvalidOperationException("numeric_send_trace_missing");
            Previous=Hook.GetValue(null);PreviousSend=SendHook.GetValue(null);Need(Previous is null&&PreviousSend is null,"isolated_trace_required");
            var callback=typeof(Trace).GetMethod(nameof(Sent),BindingFlags.Instance|BindingFlags.NonPublic)!.MakeGenericMethod(SendHook.FieldType.GenericTypeArguments[0]);
            var sender=Delegate.CreateDelegate(SendHook.FieldType,this,callback);
            SendHook.SetValue(null,sender);
            Hook.SetValue(null,(Action<short,short,int,int>)((a,b,c,d)=>{var frame=new Frame(Stage,a,b,c,d);Frames.Add(frame);try{Observer?.Invoke(frame);}catch{ObserverFailed=true;}}));
        }
        void Sent<T>(T operation,short opcode)
        {
            object? value=operation;
            long? Number(string key)=>value?.GetType().GetProperty(key,Flags)?.GetValue(value) is long n?n:null;
            SentFrames.Add(new(Stage,opcode,Number("SessionId"),Number("LeaseGeneration"),Number("ExecutionId"),Number("InvocationId")));
        }
        internal int Count(short opcode)=>Frames.Count(f=>f.RequestOpcode==opcode);
        public void Dispose(){Observer=null;Hook.SetValue(null,Previous);SendHook.SetValue(null,PreviousSend);}
    }
    sealed record SentFrame(string Stage,short RequestOpcode,long? SessionId,long? LeaseGeneration,long? ExecutionId,long? InvocationId);
    sealed record Frame(string Stage,short RequestOpcode,short ResponseOpcode,int SqlCode,int BodyLength);
}
