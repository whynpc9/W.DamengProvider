using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using TP07.Runtime;
using TP07LiteralProbe;
using W.EntityFrameworkCore.Dameng.Design.Internal;

namespace TP07.TemporalDesign;

internal static class ScaffoldCases
{
    internal static async Task Prepare(IReadOnlyDictionary<string,string> options)
    {
        Program.PreparedCli(Replay(options));
        var state=Program.Current;var owned=state.Owned!;
        var metadata=owned.RegisterDesignTable("R05-CATALOG",DesignObjectShape.ScaffoldMetadata);
        var six=owned.RegisterDesignTable("R05-SCAFFOLD6-MICRO",DesignObjectShape.ScaffoldSix);
        await Program.Case("R05-CATALOG",async record=>
        {
            await Create(metadata);await Create(six);
            var services=new ServiceCollection();new DamengDesignTimeServices().ConfigureDesignTimeServices(services);
            using var provider=services.BuildServiceProvider();var factory=provider.GetRequiredService<IDatabaseModelFactory>();
            state.Guard.VerifyProvider(factory);
            using var connection=state.Guard.OpenTestConnection();
            var model=factory.Create(connection,new DatabaseModelFactoryOptions([metadata.Table,six.Table],[CandidateGuard.User]));
            Program.Require(model.Tables.Count==2&&model.Tables.All(t=>t.Name==metadata.Table||t.Name==six.Table),DesignCode.ScaffoldFacet);
            record["factory_route"]=new{factory_type=factory.GetType().FullName,connection=state.Guard.VerifyObject(connection,"connection"),activity="actual_IDatabaseModelFactory_Create_DbConnection"};
            record["catalog"]=new[]{owned.CatalogContract(metadata),owned.CatalogContract(six)};
            record["database_model"]=model.Tables.Select(t=>new{t.Name,t.Schema,columns=t.Columns.Select(c=>new{c.Name,c.StoreType,c.IsNullable,c.DefaultValueSql}),primary_key=t.PrimaryKey?.Name,indexes=t.Indexes.Select(i=>i.Name)}).ToArray();
            Program.Require(model.Tables.Single(t=>t.Name==six.Table).Columns.Single(c=>c.Name=="D").StoreType=="TIMESTAMP(6)",DesignCode.ScaffoldFacet);
            Program.Require(model.Tables.Single(t=>t.Name==six.Table).Columns.Single(c=>c.Name=="Z").StoreType=="DATETIME(6) WITH TIME ZONE",DesignCode.ScaffoldFacet);
            var actualMetadata=model.Tables.Single(t=>t.Name==metadata.Table);var expected=MetadataStores();
            Program.Require(actualMetadata.Columns.Count==expected.Count&&actualMetadata.Columns.All(column=>expected.GetValueOrDefault(column.Name)==column.StoreType
                &&column.IsNullable==(column.Name!="ID")),DesignCode.ScaffoldFacet);
        });

        string project=Path.GetFullPath(Program.Get(options,"project")),replay=Replay(options);
        Program.Require(project==Path.Combine(replay,"test","TP07TemporalDesign","TP07TemporalDesign.csproj")
            &&File.Exists(project)&&Directory.Exists(replay),DesignCode.Usage);
        string generated=Path.Combine(replay,"test","TP07Generated",six.Nonce);Program.Require(!Directory.Exists(generated)&&!File.Exists(generated),DesignCode.FreshOutput);Directory.CreateDirectory(generated);
        string tools=ToolManifest(replay);
        await Program.Case("R05-CLI-SCAFFOLD",async record=>
        {
            // No secret is passed in argv; the startup host resolves only the named in-memory setting.
            string receipt=Path.Combine(state.Output,"scaffold-process.json");
            await Program.Child(replay,["tool","run","dotnet-ef","--","dbcontext","scaffold","Name=ConnectionStrings:TP07","W.EntityFrameworkCore.Dameng",
                "--project",project,"--startup-project",project,"--context","TP07GeneratedContext","--namespace","TP07.Generated",
                "--context-namespace","TP07.Generated","--output-dir",Path.Combine(generated,"Six"),"--no-onconfiguring","--table",six.Table,"--no-build"],receipt,requireCatalog:true);
            string metadataReceipt=Path.Combine(state.Output,"metadata-scaffold-process.json");
            await Program.Child(replay,["tool","run","dotnet-ef","--","dbcontext","scaffold","Name=ConnectionStrings:TP07","W.EntityFrameworkCore.Dameng",
                "--project",project,"--startup-project",project,"--context","TP07MetadataContext","--namespace","TP07.Generated.Metadata",
                "--context-namespace","TP07.Generated.Metadata","--output-dir",Path.Combine(generated,"Metadata"),"--no-onconfiguring","--table",metadata.Table,"--no-build"],metadataReceipt,requireCatalog:true);
            string[] files=Directory.GetFiles(generated,"*.cs",SearchOption.AllDirectories);
            Program.Require(files.Length==4&&Directory.GetFiles(Path.Combine(generated,"Six"),"*.cs").Length==2
                &&Directory.GetFiles(Path.Combine(generated,"Metadata"),"*.cs").Length==2
                &&File.Exists(Path.Combine(generated,"Six","TP07GeneratedContext.cs"))
                &&File.Exists(Path.Combine(generated,"Metadata","TP07MetadataContext.cs")),DesignCode.GeneratedSource);
            string raw=state.Guard.BuildConnectionString();
            foreach(string file in files)
            {
                string source=File.ReadAllText(file);Program.Require(!source.Contains(raw,StringComparison.Ordinal)&&!source.Contains("OnConfiguring",StringComparison.Ordinal),DesignCode.GeneratedConnectionLeak);
            }
            record["source_files"]=files.Select(file=>new{path=Path.GetRelativePath(generated,file),sha256=SafeEvidence.HashFile(file)}).ToArray();
            record["process_receipt_sha256"]=SafeEvidence.HashFile(receipt);
            record["metadata_process_receipt_sha256"]=SafeEvidence.HashFile(metadataReceipt);
            record["original_string_factory"]=true;
        });

        string generatedProject=Path.Combine(generated,"TP07Generated.csproj");
        File.WriteAllText(generatedProject,GeneratedProject(project));
        File.WriteAllText(Path.Combine(generated,"DesignStartup.cs"),GeneratedStartup);
        Program.Require(SourceHashes(generated,replay).Count==6,DesignCode.GeneratedSource);
        FreezeGeneration(generated,generatedProject,replay,tools);
        state.PreserveOwnership=true;state.Report["status"]="generated_pending_external_build";
    }

    internal static async Task Consume(IReadOnlyDictionary<string,string> options)
    {
        var state=Program.Current;var input=ContinuationInputs(options,"generated_pending_external_build");
        string generated=input.Directory;var six=state.Owned!.Entries.Single(e=>e.Shape=="ScaffoldSix");
        string generatedProject=input.Project;
        await Program.Case("R05-COMPILE-GENERATED",async record=>
        {
            await Task.CompletedTask;
            record["compiled_assembly_sha256"]=SafeEvidence.HashFile(Path.Combine(generated,"bin/Debug/net10.0/TP07GeneratedConsumer.dll"));
            record["external_build_manifest_sha256"]=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256");
            record["generated_source_manifest"]=Manifest(generated);
            var metadataType=Assembly.LoadFrom(Path.Combine(generated,"bin/Debug/net10.0/TP07GeneratedConsumer.dll")).GetType("TP07.Generated.Metadata.TP07MetadataContext",true)!;
            await using var metadataContext=GeneratedContext(metadataType);
            var metadataEntity=metadataContext.GetService<IDesignTimeModel>().Model.GetEntityTypes().Single(e=>e.GetTableName()==state.Owned!.Entries.Single(entry=>entry.Shape=="ScaffoldMetadata").Table);
            AssertMetadataModel(metadataEntity);record["metadata_model_facets"]=Facets(metadataEntity);record["metadata_context_SaveChanges_invoked"]=false;
        });
        var assembly=Assembly.LoadFrom(Path.Combine(generated,"bin/Debug/net10.0/TP07GeneratedConsumer.dll"));
        Type contextType=assembly.GetType("TP07.Generated.TP07GeneratedContext",throwOnError:true)!;
        await Program.Case("R05-SCAFFOLD6-MICRO",async record=>
        {
            await using(var context=GeneratedContext(contextType))
            {
                var entity=context.Model.GetEntityTypes().Single(e=>e.GetTableName()==six.Table);
                AssertSixModel(entity);object row=Activator.CreateInstance(entity.ClrType)!;
                Set(entity,row,"ID",1);Set(entity,row,"D",new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).AddTicks(1_234_560));
                Set(entity,row,"Z",new DateTimeOffset(new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).AddTicks(1_234_560),TimeSpan.FromMinutes(330)));
                context.Add(row);Program.Require(await context.SaveChangesAsync(Program.Token)==1,DesignCode.GeneratedModel);
                record["generated_model_facets"]=Facets(entity);
            }
            await using var fresh=GeneratedContext(contextType);var model=fresh.Model.GetEntityTypes().Single(e=>e.GetTableName()==six.Table);
            object fetched=(await Query(fresh,model.ClrType)).Single(row=>Convert.ToInt32(Get(model,row,"ID"),CultureInfo.InvariantCulture)==1);
            var dt=(DateTime)Get(model,fetched,"D")!;var dto=(DateTimeOffset)Get(model,fetched,"Z")!;
            long expectedTicks=new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).AddTicks(1_234_560).Ticks;
            Program.Require(dt.Ticks==expectedTicks&&dto.Ticks==dt.Ticks&&dto.Offset.TotalMinutes==330,DesignCode.GeneratedModel);
            record["query_result"]=new{dt=SafeEvidence.Value(dt),dto=SafeEvidence.Value(dto)};
            record["oracle_dt"]=LiteralOracle.ReadFresh(state.Guard,six.Table,1,false,6,"D");
            record["oracle_dto"]=LiteralOracle.ReadFresh(state.Guard,six.Table,1,true,6,"Z");
            Program.Require(LiteralOracle.Exact((TemporalOracleReceipt)record["oracle_dt"]!,dt)&&LiteralOracle.Exact((TemporalOracleReceipt)record["oracle_dto"]!,dto),DesignCode.GeneratedModel);
        });
        await Program.Case("R05-SCAFFOLD6-NULL",async record=>
        {
            await using(var context=GeneratedContext(contextType))
            {
                var entity=context.Model.GetEntityTypes().Single(e=>e.GetTableName()==six.Table);object row=Activator.CreateInstance(entity.ClrType)!;Set(entity,row,"ID",2);Set(entity,row,"D",null);Set(entity,row,"Z",null);context.Add(row);
                Program.Require(await context.SaveChangesAsync(Program.Token)==1,DesignCode.GeneratedModel);
            }
            await using var fresh=GeneratedContext(contextType);var entityType=fresh.Model.GetEntityTypes().Single(e=>e.GetTableName()==six.Table);
            object row2=(await Query(fresh,entityType.ClrType)).Single(row=>Convert.ToInt32(Get(entityType,row,"ID"),CultureInfo.InvariantCulture)==2);
            Program.Require(Get(entityType,row2,"D")==null&&Get(entityType,row2,"Z")==null,DesignCode.GeneratedModel);
            record["oracle_dt"]=LiteralOracle.ReadFresh(state.Guard,six.Table,2,false,6,"D");record["oracle_dto"]=LiteralOracle.ReadFresh(state.Guard,six.Table,2,true,6,"Z");
            Program.Require(((TemporalOracleReceipt)record["oracle_dt"]!).IsNull&&((TemporalOracleReceipt)record["oracle_dto"]!).IsNull,DesignCode.GeneratedModel);
            record["fresh_catalog_six"]=state.Owned!.Preflight(six);
        });
        string snapshotReceipt=Path.Combine(state.Output,"snapshot-process.json");
        await Program.Child(input.Replay,["tool","run","dotnet-ef","--","migrations","add","TP07ScaffoldSnapshot","--context","TP07GeneratedContext",
            "--project",generatedProject,"--startup-project",generatedProject,"--output-dir","Migrations/Six","--no-build"],snapshotReceipt,requireProcess:true);
        string metadataSnapshotReceipt=Path.Combine(state.Output,"metadata-snapshot-process.json");
        await Program.Child(input.Replay,["tool","run","dotnet-ef","--","migrations","add","TP07MetadataSnapshot","--context","TP07MetadataContext",
            "--project",generatedProject,"--startup-project",generatedProject,"--output-dir","Migrations/Metadata","--no-build"],metadataSnapshotReceipt,requireProcess:true);
        state.Report["snapshot_CLI_process_receipt_sha256"]=SafeEvidence.HashFile(snapshotReceipt);
        state.Report["metadata_snapshot_CLI_process_receipt_sha256"]=SafeEvidence.HashFile(metadataSnapshotReceipt);
        foreach(string folder in new[]{"Six","Metadata"})
            Program.Require(Directory.GetFiles(Path.Combine(generated,"Migrations",folder),"*.cs").Length==3,DesignCode.GeneratedSource);
        Program.Require(SourceHashes(generated,input.Replay).Count==12,DesignCode.GeneratedSource);
        FreezeGeneration(generated,generatedProject,input.Replay,ToolManifest(input.Replay));
        state.PreserveOwnership=true;state.Report["status"]="snapshot_pending_external_build";
    }

    internal static async Task Finalize(IReadOnlyDictionary<string,string> options)
    {
        var state=Program.Current;var input=ContinuationInputs(options,"snapshot_pending_external_build");
        string generated=input.Directory,generatedProject=input.Project;var six=state.Owned!.Entries.Single(e=>e.Shape=="ScaffoldSix");
        await Program.Case("R05-SNAPSHOT-MIGRATION",async record=>
        {
            string script=Path.Combine(generated,"migration.sql");
            Program.Require(!File.Exists(script),DesignCode.FreshOutput);
            await Program.Child(input.Replay,["tool","run","dotnet-ef","--","migrations","script","--context","TP07GeneratedContext","--project",generatedProject,
                "--startup-project",generatedProject,"--output",script,"--no-build"],Path.Combine(state.Output,"migration-script-process.json"),requireProcess:true);
            string metadataScript=Path.Combine(generated,"metadata-migration.sql");Program.Require(!File.Exists(metadataScript),DesignCode.FreshOutput);
            await Program.Child(input.Replay,["tool","run","dotnet-ef","--","migrations","script","--context","TP07MetadataContext","--project",generatedProject,
                "--startup-project",generatedProject,"--output",metadataScript,"--no-build"],Path.Combine(state.Output,"metadata-migration-script-process.json"),requireProcess:true);
            string proof=Path.Combine(state.Output,"compiled-snapshot-proof.json");
            await Program.Child(generated,["exec",Path.Combine(generated,"bin/Debug/net10.0/TP07GeneratedConsumer.dll"),
                "--snapshot-proof",proof,"--six-table",six.Table,"--metadata-table",state.Owned!.Entries.Single(entry=>entry.Shape=="ScaffoldMetadata").Table],Path.Combine(state.Output,"compiled-snapshot-process.json"),requireProcess:true);
            var actual=SafeEvidence.Load(proof);Program.Require(actual.GetProperty("table").GetString()==six.Table
                &&actual.GetProperty("snapshot_six").GetBoolean()&&actual.GetProperty("migration_six").GetBoolean()
                &&actual.GetProperty("metadata_precision_preserved").GetBoolean(),DesignCode.ScaffoldFacet);
            string sql=File.ReadAllText(script);Program.Require(sql.Contains(six.Table,StringComparison.Ordinal)&&sql.Contains("TIMESTAMP(6)",StringComparison.Ordinal)&&sql.Contains("DATETIME(6) WITH TIME ZONE",StringComparison.Ordinal),DesignCode.ScaffoldFacet);
            string metadataSql=File.ReadAllText(metadataScript);
            var metadataEntry=state.Owned!.Entries.Single(entry=>entry.Shape=="ScaffoldMetadata");
            Program.Require(metadataSql.Contains(metadataEntry.Table,StringComparison.Ordinal),DesignCode.ScaffoldFacet);
            foreach(var column in MetadataStores())
                Program.Require(Regex.IsMatch(metadataSql,"\""+Regex.Escape(column.Key)+"\"\\s+"+Regex.Escape(column.Value).Replace("\\ ","\\s+")+"(?=\\s|,|\\))",RegexOptions.CultureInvariant),DesignCode.ScaffoldFacet);
            record["fresh_catalog_six"]=state.Owned.Preflight(six);
            record["fresh_catalog_metadata"]=state.Owned.Preflight(metadataEntry);
            using(var fresh=state.Guard.OpenTestConnection())
                Program.Require(Convert.ToInt64(await Program.Scalar(fresh,$"SELECT COUNT(*) FROM {Program.Quote(metadataEntry.Table)}"),CultureInfo.InvariantCulture)==0,DesignCode.UnexpectedWrite);
            record["metadata_rows"]=0;record["metadata_context_SaveChanges_invoked"]=false;
            record["generated_source_manifest"]=Manifest(generated);record["sql_sha256"]=SafeEvidence.HashFile(script);
            record["metadata_sql_sha256"]=SafeEvidence.HashFile(metadataScript);
            record["compiled_snapshot_proof"]=actual;record["not_applied"]=true;
        });
    }
    private static async Task Create(OwnedObjects.Entry entry)
    {
        Program.Current.Owned!.BeginExternalCreate(entry);
        string columns=string.Join(",",entry.Columns.Select(c=>Program.Quote(c.Name)+" "+Store(c)+(c.Name=="ID"?" NOT NULL":"")));
        using var connection=Program.Current.Guard.OpenTestConnection();
        try{await Program.Execute(connection,$"CREATE TABLE {Program.Quote(entry.Table)}({columns},CONSTRAINT {Program.Quote(entry.PrimaryKey)} PRIMARY KEY(\"ID\"))");}
        catch(Exception error){Program.Current.Owned!.FailExternalCreate(entry,error);throw;}
        Program.Current.Owned!.ConfirmExternalCreate(entry);
    }
    private static string Store(OwnedObjects.Column column)=>column.Kind switch
    {"INT"=>"INT","DT"=>$"TIMESTAMP({column.Scale})","TZ"=>$"DATETIME({column.Scale}) WITH TIME ZONE","INTERVAL"=>$"INTERVAL DAY({column.Leading}) TO SECOND({column.Scale})","TLTZ"=>$"DATETIME({column.Scale}) WITH LOCAL TIME ZONE",_=>throw new DesignFailure(DesignCode.ScaffoldFacet)};
    private sealed record Inputs(string Replay,string Directory,string Project);
    internal static void LoadContinuation(IReadOnlyDictionary<string,string> options)
    {
        string path=Path.GetFullPath(Program.Get(options,"generation-report"));
        Program.Require(SafeEvidence.HashFile(path)==Program.Get(options,"generation-report-sha256"),DesignCode.CliIdentity);
        var parent=SafeEvidence.Load(path);string status=Program.Get(options,"group")=="scaffold-consume"?"generated_pending_external_build":"snapshot_pending_external_build";
        Program.Require(parent.GetProperty("task").GetString()=="TP07-D"&&parent.GetProperty("profile").GetString()==Program.Current.Guard.Profile
            &&parent.GetProperty("status").GetString()==status&&parent.GetProperty("exit_code").GetInt32()==0
            &&!parent.GetProperty("candidate_acceptance").GetBoolean()&&!parent.GetProperty("observer_failed").GetBoolean()
            &&!parent.GetProperty("cleanup_verified").GetBoolean(),DesignCode.CliIdentity);
        string ledger=Path.GetFullPath(parent.GetProperty("ownership_ledger_active").GetString()!);
        Program.Require(SafeEvidence.HashFile(ledger)==parent.GetProperty("ownership_ledger_sha256").GetString()
            &&SafeEvidence.HashFile(Path.Combine(Path.GetDirectoryName(path)!,"ownership-snapshot.json"))==parent.GetProperty("ownership_snapshot_sha256").GetString()
            &&parent.GetProperty("ownership_ledger_sha256").GetString()==parent.GetProperty("ownership_snapshot_sha256").GetString(),DesignCode.CliIdentity);
        Program.Current.LedgerPath=ledger;Program.Current.Owned=OwnedObjects.LoadForCleanup(Program.Current.Guard,ledger);
        Program.Require(Program.Current.Owned!.Entries.Count==2&&Program.Current.Owned!.Entries.Count(e=>e.Shape=="ScaffoldSix")==1
            &&Program.Current.Owned!.Entries.Count(e=>e.Shape=="ScaffoldMetadata")==1,DesignCode.MissingOwnedObject);
        foreach(var entry in Program.Current.Owned!.Entries)Program.Current.Owned!.Preflight(entry);
        Program.Current.Report["parent_report_sha256"]=SafeEvidence.HashFile(path);Program.Current.Report["parent_report"]=path;
    }
    private static Inputs ContinuationInputs(IReadOnlyDictionary<string,string> options,string status)
    {
        var parent=SafeEvidence.Load(Program.Get(options,"generation-report"));Program.Require(parent.GetProperty("status").GetString()==status,DesignCode.CliIdentity);
        var generation=parent.GetProperty("generation");string replay=Replay(options);
        Program.PreparedCli(replay);
        Program.Require(parent.GetProperty("cli_tool_manifest_sha256").GetString()==Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST_SHA256"),DesignCode.CliIdentity);
        Program.Require(generation.GetProperty("replay_root").GetString()==replay,DesignCode.CliIdentity);
        Program.Require(SafeEvidence.HashFile(ToolManifest(replay))==generation.GetProperty("tool_manifest_sha256").GetString(),DesignCode.CliIdentity);
        string directory=Path.Combine(replay,generation.GetProperty("directory").GetString()!);
        string expected=Path.Combine(replay,"test","TP07Generated",Program.Current.Owned!.Entries.Single(e=>e.Shape=="ScaffoldSix").Nonce);
        Program.Require(Path.GetFullPath(directory)==expected,DesignCode.GeneratedSource);
        string project=Path.Combine(directory,"TP07Generated.csproj");
        var frozen=generation.GetProperty("source_hashes");var actual=SourceHashes(directory,replay);
        Program.Require(actual.Count==(status=="generated_pending_external_build"?6:12)
            &&frozen.EnumerateObject().Count()==actual.Count&&frozen.EnumerateObject().All(item=>actual.TryGetValue(item.Name,out string? hash)&&hash==item.Value.GetString()),DesignCode.GeneratedSource);
        string manifest=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST")!;var approved=SafeEvidence.Load(manifest);
        Program.Require(approved.GetProperty("parent_build_manifest_sha256").GetString()==generation.GetProperty("parent_build_manifest_sha256").GetString()
            &&approved.GetProperty("parent_report_sha256").GetString()==(Program.Current.Report["parent_report_sha256"] as string)
            &&Path.GetFullPath(approved.GetProperty("replay_root").GetString()!)==replay,DesignCode.CliIdentity);
        var closure=approved.GetProperty("generated_closure");
        Program.Require(closure.GetProperty("directory").GetString()==generation.GetProperty("directory").GetString()
            &&closure.GetProperty("project").GetString()==generation.GetProperty("project").GetString()
            &&EqualJson(closure.GetProperty("source_hashes"),frozen),DesignCode.GeneratedSource);
        string priorManifest=parent.GetProperty("build_manifest_path").GetString()!;Program.Require(SafeEvidence.HashFile(priorManifest)==parent.GetProperty("build_manifest_sha256").GetString(),DesignCode.CliIdentity);
        var prior=SafeEvidence.Load(priorManifest);
        Program.Require(Path.GetFullPath(prior.GetProperty("replay_root").GetString()!)==replay,DesignCode.CliIdentity);
        foreach(var field in prior.EnumerateObject().Where(field=>field.Name is not ("source_hashes" or "locks" or "assets" or "startup_assemblies" or "source_manifest_path" or "source_manifest_sha256" or "lock_manifest_path" or "lock_manifest_sha256" or "parent_build_manifest_sha256" or "parent_report_sha256" or "generated_closure")))
            Program.Require(approved.TryGetProperty(field.Name,out var retained)&&EqualJson(field.Value,retained),DesignCode.CliIdentity);
        foreach(string key in new[]{"lineage","ef_provider"})Program.Require(EqualJson(prior.GetProperty(key),approved.GetProperty(key)),DesignCode.CliIdentity);
        foreach(string group in new[]{"source_hashes","locks","assets"})
            foreach(var item in prior.GetProperty(group).EnumerateObject())Program.Require(approved.GetProperty(group).GetProperty(item.Name).GetString()==item.Value.GetString(),DesignCode.CliIdentity);
        foreach(var item in actual)Program.Require(approved.GetProperty("source_hashes").GetProperty(item.Key).GetString()==item.Value,DesignCode.GeneratedSource);
        foreach(var item in approved.GetProperty("source_hashes").EnumerateObject())
            Program.Require(prior.GetProperty("source_hashes").TryGetProperty(item.Name,out _)||actual.ContainsKey(item.Name)
                ||(item.Name==Path.GetRelativePath(replay,Path.Combine(directory,"packages.lock.json"))
                    &&approved.GetProperty("locks").TryGetProperty(item.Name,out var generatedLock)&&generatedLock.GetString()==item.Value.GetString()),DesignCode.GeneratedSource);
        Program.Require(approved.GetProperty("locks").TryGetProperty(Path.GetRelativePath(replay,Path.Combine(directory,"packages.lock.json")),out _)
            &&approved.GetProperty("assets").TryGetProperty(Path.GetRelativePath(replay,Path.Combine(directory,"obj/project.assets.json")),out _),DesignCode.CliIdentity);
        foreach(string group in new[]{"locks","assets"})
            foreach(var item in approved.GetProperty(group).EnumerateObject())
                Program.Require(prior.GetProperty(group).TryGetProperty(item.Name,out _)||item.Name.StartsWith(generation.GetProperty("directory").GetString()+"/",StringComparison.Ordinal),DesignCode.GeneratedSource);
        var approvedAssemblies=approved.GetProperty("startup_assemblies").EnumerateArray().ToArray();
        Program.Require(approvedAssemblies.Select(item=>item.GetProperty("name").GetString()).Distinct(StringComparer.Ordinal).Count()==approvedAssemblies.Length
            &&approvedAssemblies.All(item=>item.GetProperty("name").GetString()=="TP07GeneratedConsumer"||prior.GetProperty("startup_assemblies").EnumerateArray().Any(old=>old.GetProperty("name").GetString()==item.GetProperty("name").GetString())),DesignCode.CliIdentity);
        foreach(var old in prior.GetProperty("startup_assemblies").EnumerateArray().Where(item=>item.GetProperty("name").GetString()!="TP07GeneratedConsumer"))
            Program.Require(EqualJson(approved.GetProperty("startup_assemblies").EnumerateArray().Single(item=>item.GetProperty("name").GetString()==old.GetProperty("name").GetString()),old),DesignCode.CliIdentity);
        string dll=Path.Combine(directory,"bin/Debug/net10.0/TP07GeneratedConsumer.dll");Program.VerifyGeneratedHost(Assembly.LoadFrom(dll));
        return new(replay,directory,project);
    }
    private static string Replay(IReadOnlyDictionary<string,string> options)
    {
        string replay=Path.GetFullPath(Program.Get(options,"replay-root"));var manifest=SafeEvidence.Load(Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST")!);
        Program.Require(replay==Path.GetFullPath(manifest.GetProperty("replay_root").GetString()!)&&Directory.Exists(replay)
            &&!replay.Contains("/base-source",StringComparison.Ordinal)&&!replay.Contains("/mapping-v1/work",StringComparison.Ordinal)
            &&!replay.Contains("/snapshots/",StringComparison.Ordinal)&&!replay.Contains("/source-freeze",StringComparison.Ordinal),DesignCode.GeneratedSource);return replay;
    }
    private static string ToolManifest(string replay)
    {
        string path=Path.Combine(replay,".config/dotnet-tools.json");var tool=SafeEvidence.Load(path).GetProperty("tools").GetProperty("dotnet-ef");
        Program.Require(tool.GetProperty("version").GetString()=="10.0.12"&&tool.GetProperty("commands").GetArrayLength()==1
            &&tool.GetProperty("commands")[0].GetString()=="dotnet-ef"&&!tool.GetProperty("rollForward").GetBoolean(),DesignCode.CliIdentity);return path;
    }
    private static bool EqualJson(JsonElement a,JsonElement b)
    {
        if(a.ValueKind!=b.ValueKind)return false;
        if(a.ValueKind==JsonValueKind.Object){var left=a.EnumerateObject().ToArray();return left.Length==b.EnumerateObject().Count()&&left.All(p=>b.TryGetProperty(p.Name,out var value)&&EqualJson(p.Value,value));}
        if(a.ValueKind==JsonValueKind.Array)return a.GetArrayLength()==b.GetArrayLength()&&a.EnumerateArray().Zip(b.EnumerateArray()).All(pair=>EqualJson(pair.First,pair.Second));
        return a.ValueKind==JsonValueKind.String?a.GetString()==b.GetString():a.GetRawText()==b.GetRawText();
    }
    private static Dictionary<string,string> SourceHashes(string directory,string replay)=>Directory.GetFiles(directory,"*",SearchOption.AllDirectories)
        .Where(path=>(Path.GetExtension(path) is ".cs" or ".csproj")&&!path.Contains("/obj/",StringComparison.Ordinal)&&!path.Contains("/bin/",StringComparison.Ordinal))
        .Order(StringComparer.Ordinal).ToDictionary(path=>Path.GetRelativePath(replay,path),SafeEvidence.HashFile,StringComparer.Ordinal);
    private static void FreezeGeneration(string directory,string project,string replay,string tools)
    {
        Program.Current.Report["generation"]=new{replay_root=replay,directory=Path.GetRelativePath(replay,directory),project=Path.GetRelativePath(replay,project),
            source_hashes=SourceHashes(directory,replay),tool_manifest_sha256=SafeEvidence.HashFile(tools),
            parent_build_manifest_sha256=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256"),requires_external_pinned_build=true,
            continuation_manifest_fields=new[]{"parent_build_manifest_sha256","parent_report_sha256","generated_closure"},
            externally_prepared_cli_tool_manifest_sha256=Environment.GetEnvironmentVariable("TP07_CLI_TOOL_MANIFEST_SHA256"),
            continuation_approval="Low_external_locked_build_and_root_exact_source_manifest_approval_required",
            independent_runner_extension="pending_L0_runner_does_not_implement_D_checkpoints"};
    }
    private static object Manifest(string path)=>Directory.GetFiles(path,"*.cs",SearchOption.AllDirectories).Where(p=>!p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!p.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar,StringComparison.Ordinal)).Order().Select(p=>new{path=Path.GetRelativePath(path,p),sha256=SafeEvidence.HashFile(p)}).ToArray();
    private static DbContext GeneratedContext(Type type)
    {
        MethodInfo method=typeof(Program).GetMethod(nameof(Program.GeneratedOptions))!.MakeGenericMethod(type);
        return (DbContext)Activator.CreateInstance(type,method.Invoke(null,null))!;
    }
    private static void AssertSixModel(IEntityType entity)
    {
        var store=StoreObjectIdentifier.Table(entity.GetTableName()!,entity.GetSchema());
        Program.Require(entity.GetSchema()==CandidateGuard.User&&entity.GetProperties().Count()==3
            &&entity.GetProperties().Single(p=>p.GetColumnName(store)=="D").GetColumnType()=="TIMESTAMP(6)"
            &&entity.GetProperties().Single(p=>p.GetColumnName(store)=="Z").GetColumnType()=="DATETIME(6) WITH TIME ZONE"
            &&entity.GetProperties().All(p=>p.IsNullable==(p.GetColumnName(store)!="ID")),DesignCode.ScaffoldFacet);
    }
    private static Dictionary<string,string> MetadataStores()
    {
        var stores=new Dictionary<string,string>{{"ID","INT"},{"I9","INTERVAL DAY(9) TO SECOND(6)"},{"I2","INTERVAL DAY(2) TO SECOND(6)"},{"L","TIMESTAMP(6) WITH LOCAL TIME ZONE"}};
        foreach(int scale in new[]{0,6,7,8,9}){stores["D"+scale]=$"TIMESTAMP({scale})";stores["Z"+scale]=$"DATETIME({scale}) WITH TIME ZONE";}return stores;
    }
    private static void AssertMetadataModel(IEntityType entity)
    {
        var store=StoreObjectIdentifier.Table(entity.GetTableName()!,entity.GetSchema());var expected=MetadataStores();
        Program.Require(entity.GetSchema()==CandidateGuard.User&&entity.GetProperties().Count()==expected.Count
            &&entity.GetProperties().All(property=>expected.TryGetValue(property.GetColumnName(store)!,out string? type)&&property.GetColumnType()==type
                &&property.IsNullable==(property.GetColumnName(store)!="ID")),DesignCode.ScaffoldFacet);
    }
    internal static void WriteSnapshotProof(DbContext six,DbContext metadata,string sixTable,string metadataTable,string path)
    {
        MigrationGuardSql.RequireIdentifier(sixTable);MigrationGuardSql.RequireIdentifier(metadataTable);Program.Require(!File.Exists(path)&&!Directory.Exists(path),DesignCode.FreshOutput);
        object sixProof=InspectSnapshot(six,sixTable,true),metadataProof=InspectSnapshot(metadata,metadataTable,false);
        SafeEvidence.Write(path,new{schema_version=1,table=sixTable,metadata_table=metadataTable,snapshot_six=true,migration_six=true,metadata_precision_preserved=true,
            source="actual_compiled_CLI_snapshots_and_migrations",assembly_sha256=SafeEvidence.HashFile(six.GetType().Assembly.Location),six=sixProof,metadata=metadataProof});
    }
    private static object InspectSnapshot(DbContext context,string table,bool isSix)
    {
        var migrations=context.GetService<IMigrationsAssembly>();var snapshot=migrations.ModelSnapshot??throw new DesignFailure(DesignCode.GeneratedModel);
        var snapshotEntity=snapshot.Model.GetEntityTypes().Single(e=>e.GetTableName()==table);var actualEntity=context.GetService<IDesignTimeModel>().Model.GetEntityTypes().Single(e=>e.GetTableName()==table);
        if(isSix){AssertSixModel(snapshotEntity);AssertSixModel(actualEntity);}else{AssertMetadataModel(snapshotEntity);AssertMetadataModel(actualEntity);}
        Program.Require(migrations.Migrations.Count==1,DesignCode.GeneratedModel);
        var migration=migrations.CreateMigration(migrations.Migrations.Single().Value,context.Database.ProviderName!);
        var create=migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>().Single(o=>o.Name==table);
        if(isSix)Program.Require(create.Columns.Single(c=>c.Name=="D").ColumnType=="TIMESTAMP(6)"&&create.Columns.Single(c=>c.Name=="Z").ColumnType=="DATETIME(6) WITH TIME ZONE",DesignCode.ScaffoldFacet);
        else Program.Require(create.Columns.Count==MetadataStores().Count&&create.Columns.All(column=>MetadataStores().GetValueOrDefault(column.Name)==column.ColumnType),DesignCode.ScaffoldFacet);
        Program.Require(!migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AlterColumnOperation>().Any(),DesignCode.ScaffoldFacet);
        return new{snapshot_type=snapshot.GetType().FullName,snapshot_facets=Facets(snapshotEntity),migration_id=migrations.Migrations.Single().Key,
            actual_migration_columns=create.Columns.Select(column=>new{column.Name,column.ColumnType,column.Precision,column.Scale,column.IsNullable})};
    }
    private static object Facets(IEntityType entity)=>entity.GetProperties().Select(p=>new{p.Name,store_type=p.GetColumnType(),precision=p.GetPrecision(),scale=p.GetScale()}).ToArray();
    private static IProperty Property(IEntityType entity,string column)=>entity.GetProperties().Single(p=>p.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!,entity.GetSchema()))==column);
    private static void Set(IEntityType entity,object row,string column,object? value)=>Property(entity,column).PropertyInfo!.SetValue(row,value);
    private static object? Get(IEntityType entity,object row,string column)=>Property(entity,column).PropertyInfo!.GetValue(row);
    private static async Task<object[]> Query(DbContext context,Type type)
    {
        var set=typeof(DbContext).GetMethods().Single(m=>m.Name=="Set"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(type).Invoke(context,null)!;
        var noTracking=typeof(EntityFrameworkQueryableExtensions).GetMethods().Single(m=>m.Name=="AsNoTracking"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==1).MakeGenericMethod(type).Invoke(null,[set])!;
        var task=(Task)typeof(EntityFrameworkQueryableExtensions).GetMethods().Single(m=>m.Name=="ToListAsync"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2).MakeGenericMethod(type).Invoke(null,[noTracking,Program.Token])!;
        await task;return ((IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!).Cast<object>().ToArray();
    }
    private static string GeneratedProject(string original)=>$$"""
        <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>TP07GeneratedConsumer</AssemblyName><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup><ItemGroup><ProjectReference Include="{{SecurityElement.Escape(original)}}"/><FrameworkReference Include="Microsoft.AspNetCore.App"/><PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12"><IncludeAssets>runtime;build;native;contentfiles;analyzers;buildtransitive;compile</IncludeAssets><PrivateAssets>all</PrivateAssets></PackageReference><PackageReference Include="W.DmProvider" Version="0.1.0-temporal.20261008144915"/></ItemGroup></Project>
        """;
    private const string GeneratedStartup="""
        using System.Runtime.CompilerServices;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Design;
        using Microsoft.Extensions.Hosting;
        namespace TP07.Generated;
        internal static class GeneratedProgram
        {
            [ModuleInitializer] internal static void Init()=>TP07.TemporalDesign.Program.VerifyGeneratedHost(typeof(TP07GeneratedContext).Assembly);
            public static IHostBuilder CreateHostBuilder(string[] args)=>TP07.TemporalDesign.Program.NamedHost(args);
            public static void Main(string[] args)
            {
                if(args.Length!=6||args[0]!="--snapshot-proof"||args[2]!="--six-table"||args[4]!="--metadata-table")throw new InvalidOperationException("TP07_GENERATED_USAGE");
                using var context=new TP07GeneratedContext(TP07.TemporalDesign.Program.GeneratedOptions<TP07GeneratedContext>());
                using var metadata=new Metadata.TP07MetadataContext(TP07.TemporalDesign.Program.GeneratedOptions<Metadata.TP07MetadataContext>());
                TP07.TemporalDesign.Program.WriteGeneratedSnapshotProof(context,metadata,args[3],args[5],args[1]);
            }
        }
        public sealed class GeneratedFactory:IDesignTimeDbContextFactory<TP07GeneratedContext>
        {public TP07GeneratedContext CreateDbContext(string[] args)=>new(TP07.TemporalDesign.Program.GeneratedOptions<TP07GeneratedContext>());}
        public sealed class MetadataFactory:IDesignTimeDbContextFactory<Metadata.TP07MetadataContext>
        {public Metadata.TP07MetadataContext CreateDbContext(string[] args)=>new(TP07.TemporalDesign.Program.GeneratedOptions<Metadata.TP07MetadataContext>());}
        """;
}
