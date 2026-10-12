using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using W.Dm;
using W.Dm.Config;

namespace TP07.Runtime;

// Minimal identity/physical-profile subset adapted from accepted TP06 Safety.cs.
// No secret/file is read during Load/VerifyRuntime. Archive checks verify bytes, not tar contents.
public sealed class CandidateGuard
{
    public const string User="WDM_PROVIDER_TEST",WVersion="0.1.0-temporal.20261008144915";
    public const string PackageSha="da8349a2c3f49a97ae71baa5e1b9a694ca820dd21dea4faa7cf4006b0007142d",DllSha="843710bec351b65aa075cd917bb352a525fdbc6e1a6e1396eeaae46dc8946980",Mvid="aa23e673-957c-4b1e-bf0f-291ff60077e6";
    public const string IdentitySha="e14eb1baca9a3c07b04f988dd3bcdb652048fd1a61211ad8c2cb117a9e0955a3",CapsSha="f10acfd86b5e7158eac0a0aa8cfb972418881837b4bfe63205245d82a08f54fa";
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public string Profile{get;}public string OriginalRepo{get;}public string IdentityManifestPath{get;}public string CapabilitiesPath{get;}
    System.Text.Json.JsonElement BuildManifest;public object? RuntimeIdentity{get;private set;}public string ExpectedVersion=>Profile=="shared"?"8.1.5.60":"8.1.4.6";
    CandidateGuard(string profile,string identity,string caps,string repo){Profile=profile;IdentityManifestPath=Path.GetFullPath(identity);CapabilitiesPath=Path.GetFullPath(caps);OriginalRepo=Path.GetFullPath(repo);}
    public static CandidateGuard Load(string profile,string candidateManifest,string? capabilities=null,string originalRepo="/Users/wanghongyi/Projects/W.DmProvider")
    {
        SafeEvidence.Require(profile is "shared" or "tls",EvidenceCode.Usage);string parent=Path.GetDirectoryName(Path.GetFullPath(candidateManifest))!;
        var guard=new CandidateGuard(profile,candidateManifest,capabilities??Path.Combine(parent,"capabilities.root-approved.json"),originalRepo);
        SafeEvidence.Require(SafeEvidence.HashFile(guard.IdentityManifestPath)==IdentitySha,EvidenceCode.CandidateManifestPin);
        SafeEvidence.Require(SafeEvidence.HashFile(guard.CapabilitiesPath)==CapsSha,EvidenceCode.CapabilitiesPin);
        var identity=SafeEvidence.Load(guard.IdentityManifestPath);var w=identity.GetProperty("W");var producer=identity.GetProperty("producer");
        SafeEvidence.Require(w.GetProperty("version").GetString()==WVersion&&w.GetProperty("package_sha256").GetString()==PackageSha&&w.GetProperty("dll_sha256").GetString()==DllSha&&w.GetProperty("mvid").GetString()==Mvid,EvidenceCode.PackagePin);
        SafeEvidence.Require(SafeEvidence.HashFile(Path.Combine(parent,"product-source-manifest.json"))==producer.GetProperty("source_manifest_sha256").GetString()&&SafeEvidence.HashFile(Path.Combine(parent,"product-source.tar"))==producer.GetProperty("source_archive_sha256").GetString(),EvidenceCode.SourceBinding);
        var caps=SafeEvidence.Load(guard.CapabilitiesPath);var selected=caps.GetProperty("profiles").GetProperty(profile);
        SafeEvidence.Require(caps.GetProperty("w_package_sha256").GetString()==PackageSha&&selected.GetProperty("root_approved").GetBoolean()&&selected.GetProperty("server_version").GetString()==guard.ExpectedVersion&&selected.GetProperty("seven_write").GetBoolean()==(profile=="shared"),EvidenceCode.CapabilitiesPin);
        string package=Directory.GetFiles(Path.Combine(parent,"feed"),"*.nupkg").Single(p=>Path.GetFileName(p).Equals("W.DmProvider."+WVersion+".nupkg",StringComparison.OrdinalIgnoreCase));
        SafeEvidence.Require(SafeEvidence.HashFile(package)==PackageSha,EvidenceCode.PackagePin);using var zip=ZipFile.OpenRead(package);using var asset=zip.GetEntry("lib/net10.0/W.DmProvider.dll")!.Open();using var memory=new MemoryStream();asset.CopyTo(memory);SafeEvidence.Require(SafeEvidence.Hash(memory.ToArray())==DllSha,EvidenceCode.DllPin);
        return guard;
    }
    public static CandidateGuard VerifyProcessEnvironment(Assembly? startupAssembly=null)
    {
        string manifest=Environment.GetEnvironmentVariable("TP07_CANDIDATE_MANIFEST")??"",profile=Environment.GetEnvironmentVariable("TP07_PROFILE")??"";
        var guard=Load(profile,manifest,Environment.GetEnvironmentVariable("TP07_CAPABILITIES"),Environment.GetEnvironmentVariable("TP07_ORIGINAL_REPO")??"/Users/wanghongyi/Projects/W.DmProvider");guard.VerifyRuntime(startupAssembly);return guard;
    }
    public object VerifyRuntime(Assembly? startupAssembly=null)
    {
        Assembly w=typeof(DmConnection).Assembly;SafeEvidence.Require(SafeEvidence.HashFile(w.Location)==DllSha,EvidenceCode.DllPin);SafeEvidence.Require(w.ManifestModule.ModuleVersionId.ToString("D")==Mvid,EvidenceCode.MvidPin);
        Assembly startup=startupAssembly??Assembly.GetEntryAssembly()!;
        SafeEvidence.Require(startup.GetName().Name =="TP07TransactionCapabilityProbe",EvidenceCode.SourceBinding);
        string buildPath=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST")??"",buildSha=Environment.GetEnvironmentVariable("TP07_STARTUP_MANIFEST_SHA256")??"";
        SafeEvidence.Require(System.Text.RegularExpressions.Regex.IsMatch(buildSha,"\\A[a-f0-9]{64}\\z")&&SafeEvidence.HashFile(buildPath)==buildSha,EvidenceCode.SourceBinding);BuildManifest=SafeEvidence.Load(buildPath);
        SafeEvidence.Require(BuildManifest.GetProperty("schema_version").GetInt32()==1,EvidenceCode.SourceBinding);
        var lineage=BuildManifest.GetProperty("lineage");SafeEvidence.Require(lineage.GetProperty("ef_commit").GetString()=="5863d3755dbd2d333ac359bf1c0a789762adbaed"&&lineage.GetProperty("base_archive_sha256").GetString()=="668b5a077db8a0730a251cd639288130526fc51e619dac8574102267b4f13cc8",EvidenceCode.SourceBinding);
        SafeEvidence.Require(BuildManifest.GetProperty("task").GetString()=="TP07-T1-startup",EvidenceCode.SourceBinding);
        foreach(string name in new[]{"source","native"})VerifyRef(BuildManifest.GetProperty(name));
        foreach(string name in new[]{"bridge_patch_sha256","mapping_manifest_sha256","mapping_patch_sha256","approved_ef_source_manifest_sha256","transaction_patch_sha256","parent_overlay_sha256","probe_source_manifest_sha256","toolpack_manifest_sha256"})SafeEvidence.Require(System.Text.RegularExpressions.Regex.IsMatch(lineage.GetProperty(name).GetString()??"","\\A[a-f0-9]{64}\\z"),EvidenceCode.SourceBinding);
        string replay=Path.GetFullPath(BuildManifest.GetProperty("replay_root").GetString()!);
        foreach(string group in new[]{"source_hashes","locks","assets"})
        {
            var files=BuildManifest.GetProperty(group);SafeEvidence.Require(files.ValueKind==System.Text.Json.JsonValueKind.Object&&files.EnumerateObject().Any(),EvidenceCode.SourceBinding);
            foreach(var item in files.EnumerateObject()){SafeEvidence.Require(!Path.IsPathRooted(item.Name)&&!item.Name.Contains('\\')&&!item.Name.Split('/').Any(part=>part is ".." or ".local" or "secrets"),EvidenceCode.SourceBinding);SafeEvidence.Require(SafeEvidence.HashFile(Path.Combine(replay,item.Name))==item.Value.GetString(),EvidenceCode.SourceBinding);}
        }
        var expected=BuildManifest.GetProperty("host");
        SafeEvidence.Require(Path.GetFullPath(startup.Location)==expected.GetProperty("path").GetString()&&SafeEvidence.HashFile(startup.Location)==expected.GetProperty("sha256").GetString()&&startup.ManifestModule.ModuleVersionId.ToString("D")==expected.GetProperty("mvid").GetString()&&SafeEvidence.HashFile(Path.ChangeExtension(startup.Location,".deps.json"))==expected.GetProperty("deps").GetProperty("sha256").GetString(),EvidenceCode.SourceBinding);
        foreach(var dependency in BuildManifest.GetProperty("ef_dependencies").EnumerateArray()){var loaded=Assembly.Load(dependency.GetProperty("name").GetString()!);SafeEvidence.Require(SafeEvidence.HashFile(loaded.Location)==dependency.GetProperty("sha256").GetString()&&loaded.ManifestModule.ModuleVersionId.ToString("D")==dependency.GetProperty("mvid").GetString(),EvidenceCode.SourceBinding);}
        object actualProvider=VerifyProviderAssembly(Assembly.Load("W.EntityFrameworkCore.Dameng"));
        string executable=startup.Location;var deps=SafeEvidence.Load(Path.ChangeExtension(executable,".deps.json"));var libraries=deps.GetProperty("libraries");
        SafeEvidence.Require(libraries.GetProperty("W.DmProvider/"+WVersion).GetProperty("type").GetString()=="package",EvidenceCode.Dependencies);
        SafeEvidence.Require(!libraries.EnumerateObject().Any(p=>p.Name.StartsWith("DM.DmProvider/",StringComparison.OrdinalIgnoreCase)),EvidenceCode.OfficialFallback);
        foreach(string id in new[]{"Microsoft.EntityFrameworkCore","Microsoft.EntityFrameworkCore.Relational","Microsoft.EntityFrameworkCore.Abstractions"})SafeEvidence.Require(libraries.GetProperty(id+"/10.0.12").GetProperty("type").GetString()=="package",EvidenceCode.EfVersion);
        SafeEvidence.Require(deps.GetProperty("targets").EnumerateObject().Any(target=>target.Value.TryGetProperty("W.DmProvider/"+WVersion,out var lib)&&lib.TryGetProperty("runtime",out var assets)&&assets.TryGetProperty("lib/net10.0/W.DmProvider.dll",out _)),EvidenceCode.AssetSelection);
        SafeEvidence.Require(Environment.Version.Major==10,EvidenceCode.Dependencies);
        RuntimeIdentity=new{version=WVersion,package_sha256=PackageSha,dll_sha256=SafeEvidence.HashFile(w.Location),mvid=w.ManifestModule.ModuleVersionId.ToString("D"),deps_sha256=SafeEvidence.HashFile(Path.ChangeExtension(executable,".deps.json")),runtime=Environment.Version.ToString(),ef_package_version="10.0.12",ef_provider=actualProvider,source_manifest_sha256=SafeEvidence.Load(IdentityManifestPath).GetProperty("producer").GetProperty("source_manifest_sha256").GetString(),archive_sha256=SafeEvidence.Load(IdentityManifestPath).GetProperty("producer").GetProperty("source_archive_sha256").GetString(),archive_check="byte_hash_only_no_archive_introspection",build_manifest_sha256=buildSha,source_manifest_sha256_binding=BuildManifest.GetProperty("source").GetProperty("sha256").GetString(),lock_manifest_sha256=BuildManifest.GetProperty("native").GetProperty("sha256").GetString(),startup_assembly=startup.GetName().Name,startup_sha256=SafeEvidence.HashFile(startup.Location),startup_mvid=startup.ManifestModule.ModuleVersionId.ToString("D"),entry_host_type=Assembly.GetEntryAssembly()?.GetName().Name,entry_host_sha256=Assembly.GetEntryAssembly() is {Location.Length:>0} host?SafeEvidence.HashFile(host.Location):null};return RuntimeIdentity;
    }
    static void VerifyRef(System.Text.Json.JsonElement reference){string path=reference.GetProperty("path").GetString()!;SafeEvidence.Require(Path.IsPathFullyQualified(path)&&SafeEvidence.HashFile(path)==reference.GetProperty("sha256").GetString(),EvidenceCode.SourceBinding);}
    public static object Identity(object value){var type=value.GetType();var assembly=type.Assembly;return new{type=type.FullName,name=assembly.GetName().Name,path=assembly.Location,sha256=SafeEvidence.HashFile(assembly.Location),mvid=assembly.ManifestModule.ModuleVersionId.ToString("D")};}
    public void VerifyTransactionService(object value,string role){string expected=role=="factory"?"W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransactionFactory":"W.EntityFrameworkCore.Dameng.Storage.Internal.DamengRelationalTransaction";SafeEvidence.Require(value.GetType().FullName==expected,EvidenceCode.TypeRoute);VerifyProviderAssembly(value.GetType().Assembly);}
    object VerifyProviderAssembly(Assembly assembly)
    {
        SafeEvidence.Require(BuildManifest.ValueKind==System.Text.Json.JsonValueKind.Object,EvidenceCode.SourceBinding);var expected=BuildManifest.GetProperty("ef_provider");
        SafeEvidence.Require(assembly.GetName().Name=="W.EntityFrameworkCore.Dameng"&&expected.GetProperty("name").GetString()==assembly.GetName().Name&&Path.GetFullPath(assembly.Location)==expected.GetProperty("path").GetString()&&SafeEvidence.HashFile(assembly.Location)==expected.GetProperty("sha256").GetString()&&assembly.ManifestModule.ModuleVersionId.ToString("D")==expected.GetProperty("mvid").GetString(),EvidenceCode.SourceBinding);
        return new{dll_sha256=SafeEvidence.HashFile(assembly.Location),mvid=assembly.ManifestModule.ModuleVersionId.ToString("D"),source="isolated_provider_ProjectReference_build_manifest_verified"};
    }
    public object VerifyProvider(object mappingSource)=>new{type=mappingSource.GetType().FullName,assembly=VerifyProviderAssembly(mappingSource.GetType().Assembly)};
    public object VerifyObject(object value,string role)
    {
        Type expected=role switch{"connection"=>typeof(DmConnection),"command"=>typeof(DmCommand),"parameter"=>typeof(DmParameter),"reader"=>typeof(DmDataReader),"transaction"=>typeof(DmTransaction),_=>throw new EvidenceException(EvidenceCode.TypeRoute)};
        SafeEvidence.Require(value.GetType()==expected&&value.GetType().Assembly==typeof(DmConnection).Assembly,EvidenceCode.TypeRoute);return new{role,type=value.GetType().FullName,dll_sha256=DllSha,mvid=Mvid};
    }
    public string BuildConnectionString()
    {
        string raw=Environment.GetEnvironmentVariable("DAMENG_TEST_CONNECTION_STRING")??"";SafeEvidence.Require(!string.IsNullOrWhiteSpace(raw),EvidenceCode.TestIdentity);
        var b=new DmConnectionStringBuilder(raw);SafeEvidence.Require(b.User.Equals(User,StringComparison.OrdinalIgnoreCase),EvidenceCode.TestIdentity);
        b.Schema=User;b.PersistSecurityInfo=false;b.ConnPooling=false;b.StmtPooling=false;b.PreparePooling=false;b.LogLevel=LogLevel.OFF;b.ConnectTimeout=TimeSpan.FromSeconds(15);b.CommandTimeout=12;
        b.TransportSecurity=Profile=="tls"?DmTransportSecurity.RequireTls:DmTransportSecurity.PlaintextAllowed;
        if(Profile=="tls")
        {SafeEvidence.Require(b.Server=="127.0.0.1"&&b.Port==15236,EvidenceCode.TransportTarget);string certs=Path.Combine(OriginalRepo,".local/t07/certs/client_ssl",User);b.TlsCaCertificatePath=Path.Combine(certs,"ca-cert.pem");b.TlsClientCertificatePath=Path.Combine(certs,"client-cert.pem");b.TlsClientPrivateKeyPath=Path.Combine(certs,"client-key.pem");b.TlsRevocationMode=DmTlsRevocationMode.NoCheck;}
        return b.ConnectionString;
    }
    public DmConnection OpenTestConnection(){var c=new DmConnection(BuildConnectionString());try{c.Open();VerifyOpenedConnection(c);return c;}catch{c.Dispose();throw;}}
    public async Task<DmConnection> OpenTestConnectionAsync(CancellationToken token=default){var c=new DmConnection(BuildConnectionString());try{await c.OpenAsync(token);VerifyOpenedConnection(c);return c;}catch{await c.DisposeAsync();throw;}}
    public object VerifyOpenedConnection(DbConnection c,DbTransaction? transaction=null)
    {
        VerifyObject(c,"connection");SafeEvidence.Require(c.State==ConnectionState.Open,EvidenceCode.TestIdentity);
        if(transaction is not null){VerifyObject(transaction,"transaction");SafeEvidence.Require(ReferenceEquals(transaction.Connection,c),EvidenceCode.TypeRoute);}
        string user=Convert.ToString(Scalar(c,"SELECT USER FROM DUAL",transaction),CultureInfo.InvariantCulture)?.Trim()??"",schema=Convert.ToString(Scalar(c,"SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) FROM DUAL",transaction),CultureInfo.InvariantCulture)?.Trim()??"";
        SafeEvidence.Require(user==User&&schema==User,EvidenceCode.TestIdentity);
        object instance=c.GetType().GetMethod("GetConnInstance",Flags,null,Type.EmptyTypes,null)?.Invoke(c,null)??throw new EvidenceException(EvidenceCode.PhysicalProfile);
        object props=Property(instance,"ConnProperty");string version=(string)Property(props,"ServerVersion");int message=Number(Field(props,"msgVersion"));string encoding=((string)Property(props,"ServerEncoding")).ToLowerInvariant(),negotiated=((string)Field(props,"serverEncoding")).ToLowerInvariant();
        var options=Field(props,"property") as IDictionary;bool empty=options is not null&&options.Contains("encoding")&&options["encoding"] is string text&&text.Length==0;
        SafeEvidence.Require(version==ExpectedVersion&&Convert.ToString(c.ServerVersion,CultureInfo.InvariantCulture)==version&&message==(Profile=="shared"?21:11)&&encoding==(Profile=="shared"?"utf-8":"gb18030")&&negotiated==encoding&&empty,EvidenceCode.PhysicalProfile);
        return new{profile=Profile,server_version=version,message_version=message,encoding,negotiated_encoding=negotiated,negotiated_encoding_origin="physical_conn_property_serverEncoding_field",encoding_override_empty=empty,profile_origin="physical_get_conn_instance_conn_property",server_user=user,server_schema=schema,result_format="ordinary_non_BDTA",timezone=Number(Property(props,"TimeZone"))};
    }
    static object Property(object value,string name)=>value.GetType().GetProperty(name,Flags)?.GetValue(value)??throw new EvidenceException(EvidenceCode.PhysicalProfile);
    static object Field(object value,string name)=>value.GetType().GetField(name,Flags)?.GetValue(value)??throw new EvidenceException(EvidenceCode.PhysicalProfile);
    static int Number(object value){SafeEvidence.Require(value is int or short,EvidenceCode.PhysicalProfile);return Convert.ToInt32(value,CultureInfo.InvariantCulture);}
    public static DbCommand Command(DbConnection c,string sql,DbTransaction? transaction=null){var cmd=c.CreateCommand();cmd.CommandTimeout=12;cmd.CommandText=sql;if(transaction is not null)cmd.Transaction=transaction;return cmd;}
    public static object? Scalar(DbConnection c,string sql,DbTransaction? transaction=null){using var cmd=Command(c,sql,transaction);return cmd.ExecuteScalar();}
}
