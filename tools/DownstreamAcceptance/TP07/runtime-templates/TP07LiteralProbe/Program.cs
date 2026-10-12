using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TP07.Runtime;
using W.Dm;

namespace TP07LiteralProbe;

internal static class Program
{
    static int Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--selftest-literal-grammar")return LiteralGrammarSelftest(args);
        var report=new Dictionary<string,object?>{["schema_version"]=1,["task"]="TP07-L0",["expected_only"]=false,["database_accessed"]=false,["literal_runtime_accepted"]=false};
        bool outputAcquired=false;string output="";OwnedObjects? owned=null;int exit=1;
        try
        {
            var options=ParseArgs(args);string profile=options["--profile"],manifest=options["--candidate-manifest"];output=Path.GetFullPath(options["--output"]);
            SafeEvidence.Require(!Directory.Exists(output),EvidenceCode.FreshOutput);Directory.CreateDirectory(output);outputAcquired=true;
            Environment.SetEnvironmentVariable("TP07_STARTUP_MANIFEST",options["--build-manifest"]);Environment.SetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256",options["--build-manifest-sha256"]);
            var guard=CandidateGuard.Load(profile,manifest,options.GetValueOrDefault("--capabilities"),options.GetValueOrDefault("--repo")??"/Users/wanghongyi/Projects/W.DmProvider");report["profile"]=profile;report["identity_manifest_sha256"]=SafeEvidence.HashFile(manifest);report["capabilities_sha256"]=SafeEvidence.HashFile(guard.CapabilitiesPath);report["runtime_identity"]=guard.VerifyRuntime();
            if(options.ContainsKey("--cleanup"))
            {
                owned=OwnedObjects.LoadForCleanup(guard,options["--ledger"]);report["database_accessed"]=true;report["cleanup"]=owned.Cleanup();report["status"]="cleanup_complete";exit=0;
            }
            else
            {
                owned=new OwnedObjects(guard,Path.Combine(output,"ownership.json"));var cases=LiteralCases.ForProfile(profile);var results=new List<Dictionary<string,object?>>();report["required_case_ids"]=cases.Select(c=>c.Id).ToArray();report["cases"]=results;
                using var context=new LiteralContext(guard.BuildConnectionString());var source=context.GetService<IRelationalTypeMappingSource>();report["provider"]=guard.VerifyProvider(source);
                var connection=context.Database.GetDbConnection();report["factory_connection"]=guard.VerifyObject(connection,"connection");connection.Open();report["database_accessed"]=true;report["physical_profile"]=guard.VerifyOpenedConnection(connection);
                foreach(var spec in cases)
                {
                    var result=new Dictionary<string,object?>{["case_id"]=spec.Id,["status"]="attempted",["expected"]=SafeEvidence.Value(spec.Value),["bare_characterization"]=spec.Bare,["catalog_contract"]="unverified"};results.Add(result);
                    var entry=owned.RegisterLiteral(spec.Id,spec.StoreType);owned.Create(entry);result["owned_table"]=entry.Table;result["catalog"]=owned.CatalogContract(entry);
                    var property=context.Model.FindEntityType(typeof(LiteralRow))!.FindProperty(spec.Property)!;var mapping=source.FindMapping(property)??throw new EvidenceException(EvidenceCode.LiteralSource);
                    result["mapping"]=new{type=mapping.GetType().FullName,store_type=mapping.StoreType,model_precision=property.GetPrecision(),model_scale=property.GetScale(),mapping_precision=mapping.Precision,mapping_scale=mapping.Scale};
                    if(spec.Bare)SafeEvidence.Require(mapping.StoreType.Equals(spec.StoreType,StringComparison.OrdinalIgnoreCase)&&mapping.Precision is null,EvidenceCode.FacetPolicy);
                    else SafeEvidence.Require(mapping.Precision==spec.ParameterScale,EvidenceCode.FacetPolicy);
                    string? literal=null;var generation=SafeEvidence.StartStep(result,"GenerateSqlLiteral");
                    try{literal=mapping.GenerateSqlLiteral(spec.Value);SafeEvidence.Require(!spec.ExpectedLocalOverflow,EvidenceCode.ExpectedRejection);CheckLiteral(literal);result["generated_literal"]=literal;result["generated_literal_sha256"]=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(literal));result["generated_literal_match"]=LiteralFractionMatches(literal,spec.Value,spec.ParameterScale);SafeEvidence.Require(result["generated_literal_match"] is true,EvidenceCode.LiteralPrecision);SafeEvidence.CompleteStep(generation,new{literal_returned=true});}
                    catch(OverflowException error)when(spec.ExpectedLocalOverflow){result["literal_error"]=SafeEvidence.Safe(error);result["generated_literal_match"]=true;result["generated_literal"]=null;SafeEvidence.CompleteStep(generation,new{literal_returned=false,expected_local_overflow=true});}
                    catch(Exception error){SafeEvidence.FailStep(generation,error);throw;}
                    if(spec.ExpectedLocalOverflow)
                    {
                        var absent=LiteralOracle.ReadFresh(guard,entry.Table,1,spec.Value is DateTimeOffset,partialSink:result);SafeEvidence.Require(absent.Rows==0,EvidenceCode.ExpectedRejection);result["literal_business_writes"]=0;result["fresh_rows"]=absent.Rows;result["persisted_exact"]=null;result["persistence_status"]="not_applicable_expected_local_literal_rejection";result["status"]="expected_local_rejection";continue;
                    }
                    var execution=SafeEvidence.StartStep(result,"PersistGeneratedLiteral");
                    try{using var cmd=CandidateGuard.Command(connection,$"INSERT INTO {entry.Table}(ID,V) VALUES(1,{literal})");result["literal_command"]=guard.VerifyObject(cmd,"command");int count=cmd.ExecuteNonQuery();result["literal_affected_rows"]=count;SafeEvidence.Require(count==1,EvidenceCode.MissingRow);SafeEvidence.CompleteStep(execution,new{affected_rows=count});}
                    catch(Exception error){SafeEvidence.FailStep(execution,error);throw;}
                    var persisted=LiteralOracle.ReadFresh(guard,entry.Table,1,spec.Value is DateTimeOffset,spec.Bare?null:spec.ParameterScale,partialSink:result);result["literal_oracle"]=persisted;result["persisted_exact"]=LiteralOracle.Exact(persisted,spec.Value);SafeEvidence.Require(result["persisted_exact"] is true,EvidenceCode.OracleMismatch);
                    using(var control=CandidateGuard.Command(connection,$"INSERT INTO {entry.Table}(ID,V) VALUES(2,:v)"))
                    {
                        var parameter=new DmParameter{ParameterName="v",DbType=spec.Value is DateTimeOffset?DbType.DateTimeOffset:DbType.DateTime2,Scale=(byte)spec.ParameterScale,Value=spec.Value};control.Parameters.Add(parameter);result["ado_control_route"]=new{command=guard.VerifyObject(control,"command"),parameter=guard.VerifyObject(parameter,"parameter"),scale=(int)parameter.Scale,precision=(int)parameter.Precision,evidence_role="ado_control_not_EF_SaveChanges"};SafeEvidence.Require(control.ExecuteNonQuery()==1,EvidenceCode.MissingRow);
                    }
                    var ado=LiteralOracle.ReadFresh(guard,entry.Table,2,spec.Value is DateTimeOffset,spec.Bare?null:spec.ParameterScale,partialSink:result);result["ado_control_oracle"]=ado;result["ado_control_exact"]=LiteralOracle.Exact(ado,spec.Value);SafeEvidence.Require(result["ado_control_exact"] is true,EvidenceCode.OracleMismatch);
                    result["catalog_contract"]=spec.Bare?"observed_unverified_bare_declaration":"explicit_mapping_declaration_observed";result["status"]="observed_exact";
                }
                report["case_count"]=results.Count;report["status"]="literal_cases_complete";report["literal_runtime_accepted"]=true;exit=0;
            }
        }
        catch(Exception error){report["status"]="execution_failed";report["failure"]=SafeEvidence.Safe(error);}
        finally
        {
            if(owned is not null)try{report["finally_cleanup"]=owned.Cleanup();}catch(Exception error){report["cleanup_error"]=SafeEvidence.Safe(error);report["literal_runtime_accepted"]=false;exit=1;}
        }
        report["process_exit_code"]=exit;
        try{if(outputAcquired)SafeEvidence.Write(Path.Combine(output,"report.json"),report);}catch{return 2;}
        return exit;
    }
    static Dictionary<string,string> ParseArgs(string[] args)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);for(int i=0;i<args.Length;i++){string key=args[i];if(key=="--cleanup"){SafeEvidence.Require(result.TryAdd(key,"true"),EvidenceCode.Usage);continue;}SafeEvidence.Require(key is "--profile" or "--candidate-manifest" or "--output" or "--capabilities" or "--repo" or "--ledger" or "--build-manifest" or "--build-manifest-sha256",EvidenceCode.Usage);SafeEvidence.Require(i+1<args.Length&&result.TryAdd(key,args[++i]),EvidenceCode.Usage);}
        SafeEvidence.Require(result.ContainsKey("--profile")&&result.ContainsKey("--candidate-manifest")&&result.ContainsKey("--output")&&result.ContainsKey("--build-manifest")&&result.ContainsKey("--build-manifest-sha256")&&(!result.ContainsKey("--cleanup")||result.ContainsKey("--ledger")),EvidenceCode.Usage);return result;
    }
    static void CheckLiteral(string literal)
    {
        const string clock=@"[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2}";
        const string zone=@"(?<offset>[+-][0-9]{2}:[0-9]{2})";
        string[] patterns=
        [
            @"\ATO_TIMESTAMP\('(?<clock>"+clock+@"\.[0-9]{7})', 'YYYY-MM-DD HH24:MI:SS\.FF7'\)\z",
            @"\ATO_TIMESTAMP_TZ\('(?<clock>"+clock+@"\.[0-9]{7}) "+zone+@"', 'YYYY-MM-DD HH24:MI:SS\.FF7 TZH:TZM'\)\z",
            @"\ATIMESTAMP '(?<clock>"+clock+@"(?:\.[0-9]{1,6})?)(?:"+zone+@")?'\z"
        ];
        SafeEvidence.Require(literal.Length<=256,EvidenceCode.LiteralSafety);
        Match? accepted=null;
        foreach(string pattern in patterns){var match=Regex.Match(literal,pattern,RegexOptions.CultureInvariant);if(match.Success){accepted=match;break;}}
        SafeEvidence.Require(accepted is not null,EvidenceCode.LiteralSafety);
        string[] formats=["yyyy-MM-dd HH:mm:ss","yyyy-MM-dd HH:mm:ss.f","yyyy-MM-dd HH:mm:ss.ff","yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.ffff","yyyy-MM-dd HH:mm:ss.fffff","yyyy-MM-dd HH:mm:ss.ffffff","yyyy-MM-dd HH:mm:ss.fffffff"];
        SafeEvidence.Require(DateTime.TryParseExact(accepted!.Groups["clock"].Value,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out var value),EvidenceCode.LiteralSafety);
        if(accepted.Groups["offset"].Success)
        {
            string offset=accepted.Groups["offset"].Value;int hour=int.Parse(offset.AsSpan(1,2),CultureInfo.InvariantCulture),minute=int.Parse(offset.AsSpan(4,2),CultureInfo.InvariantCulture);
            int total=(hour*60+minute)*(offset[0]=='-'?-1:1);
            SafeEvidence.Require(minute<60&&total is >=-779 and <=840,EvidenceCode.LiteralSafety);
            try{_ =new DateTimeOffset(value,TimeSpan.FromMinutes(total));}
            catch(ArgumentException){throw new EvidenceException(EvidenceCode.LiteralSafety);}
        }
    }
    static List<(string Id,string Literal,bool Accept)> GrammarVectors()
    {
        const string date="2026-07-23 14:15:16",format="YYYY-MM-DD HH24:MI:SS.FF7",offsetFormat="YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM";
        string dt=$"TO_TIMESTAMP('{date}.0000001', '{format}')",dto=$"TO_TIMESTAMP_TZ('{date}.0000001 +05:30', '{offsetFormat}')";
        var vectors=new List<(string Id,string Literal,bool Accept)>
        {
            ("GRAM-FF7-DT-tick1",dt,true),("GRAM-FF7-DTO-p330",dto,true),
            ("GRAM-FF7-DTO-m30",$"TO_TIMESTAMP_TZ('{date}.0000001 -00:30', '{offsetFormat}')",true),
            ("GRAM-FF7-DTO-p840",$"TO_TIMESTAMP_TZ('{date}.0000001 +14:00', '{offsetFormat}')",true)
        };
        for(int scale=0;scale<=6;scale++)vectors.Add(("GRAM-LEGACY-DT-p"+scale,$"TIMESTAMP '{date}{(scale==0?"":"."+"123456"[..scale])}'",true));
        vectors.Add(("GRAM-LEGACY-DTO-p6",$"TIMESTAMP '{date}.123456+05:30'",true));
        vectors.AddRange
        ([
            ("GRAM-REJECT-old-dt7",$"TIMESTAMP '{date}.0000001'",false),
            ("GRAM-REJECT-old-dto7",$"TIMESTAMP '{date}.0000001+05:30'",false),
            ("GRAM-REJECT-function",dt.Replace("TO_TIMESTAMP","TO_DATE",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-ff9",dt.Replace("0000001","000000100",StringComparison.Ordinal).Replace("FF7","FF9",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-ff9",dto.Replace("0000001","000000100",StringComparison.Ordinal).Replace("FF7","FF9",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-six",dt.Replace("0000001","000001",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-eight",dt.Replace("0000001","00000010",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-six",dto.Replace("0000001","000001",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-eight",dto.Replace("0000001","00000010",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-comma-missing",dt.Replace("', '","','",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-comma-extra",dt.Replace("', '","',  '",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-offset-missing",dto.Replace(" +05:30","+05:30",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-offset-extra",dto.Replace(" +05:30","  +05:30",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-comma-missing",dto.Replace("', '","','",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dt-format",dt.Replace("HH24","HH12",StringComparison.Ordinal),false),
            ("GRAM-REJECT-dto-format",dto.Replace("TZH:TZM","TZR",StringComparison.Ordinal),false),
            ("GRAM-REJECT-extra-sql",dt+"; SELECT 1 FROM DUAL",false),
            ("GRAM-REJECT-comment",dt+" -- suffix",false),
            ("GRAM-REJECT-wrapped","CAST("+dt+" AS TIMESTAMP(7))",false),
            ("GRAM-REJECT-extra-argument",dt[..^1]+", 1)",false),
            ("GRAM-REJECT-clock",dt.Replace("2026-07-23","2026-13-23",StringComparison.Ordinal),false),
            ("GRAM-REJECT-offset",dto.Replace("+05:30","+05:60",StringComparison.Ordinal),false),
            ("GRAM-REJECT-length",dt+new string(' ',257),false)
        ]);
        return vectors;
    }
    static int LiteralGrammarSelftest(string[] args)
    {
        string path="";bool acquired=false;int exit=1;
        var report=new Dictionary<string,object?>{["schema_version"]=1,["task"]="TP07-L0-literal-grammar",["expected_only"]=false,
            ["database_accessed"]=false,["candidate_acceptance"]=false,["literal_runtime_accepted"]=false,["status"]="grammar_selftest_failed"};
        try
        {
            SafeEvidence.Require(args.Length==3&&args[1]=="--output",EvidenceCode.Usage);path=Path.GetFullPath(args[2]);
            SafeEvidence.Require(!Directory.Exists(path)&&!File.Exists(path),EvidenceCode.FreshOutput);Directory.CreateDirectory(path);acquired=true;
            var assembly=typeof(Program).Assembly;
            report["tool_identity"]=new{name=assembly.GetName().Name,dll_sha256=SafeEvidence.HashFile(assembly.Location),mvid=assembly.ManifestModule.ModuleVersionId.ToString("D"),
                deps_sha256=SafeEvidence.HashFile(Path.ChangeExtension(assembly.Location,".deps.json")),runtime=Environment.Version.ToString()};
            var vectors=GrammarVectors();var results=new List<Dictionary<string,object?>>();report["cases"]=results;
            foreach(var vector in vectors)
            {
                bool actual=false,controlled=false;object? error=null;
                try{CheckLiteral(vector.Literal);actual=true;controlled=true;}
                catch(EvidenceException rejection)when(rejection.Code==EvidenceCode.LiteralSafety){controlled=true;error=SafeEvidence.Safe(rejection);}
                catch(Exception failure){error=SafeEvidence.Safe(failure);}
                bool passed=controlled&&actual==vector.Accept;
                results.Add(new(){["case_id"]=vector.Id,["literal"]=vector.Literal,["literal_sha256"]=SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(vector.Literal)),
                    ["expected_accept"]=vector.Accept,["actual_accept"]=actual,["status"]=passed?"completed":"failed",["error"]=error});
            }
            report["case_count"]=results.Count;report["accepted_case_count"]=vectors.Count(vector=>vector.Accept);report["rejected_case_count"]=vectors.Count(vector=>!vector.Accept);
            int passedCount=results.Count(result=>Equals(result["status"],"completed"));report["passed"]=passedCount;report["failed"]=results.Count-passedCount;
            SafeEvidence.Require(vectors.Count==35&&passedCount==35,EvidenceCode.LiteralSafety);report["status"]="grammar_selftest_complete";exit=0;
        }
        catch(Exception failure){report["failure"]=SafeEvidence.Safe(failure);}
        report["process_exit_code"]=exit;
        if(acquired)try{SafeEvidence.Write(Path.Combine(path,"report.json"),report);}catch{return 2;}
        return exit;
    }
    static bool LiteralFractionMatches(string literal,object value,int scale)
    {
        long ticks=value is DateTimeOffset dto?dto.Ticks:((DateTime)value).Ticks;string expected=(ticks%10000000).ToString("D7",CultureInfo.InvariantCulture)[..scale];return literal.Contains("."+expected,StringComparison.Ordinal);
    }
}
