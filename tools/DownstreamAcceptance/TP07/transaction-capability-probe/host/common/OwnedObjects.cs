using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TP07.Runtime;

public enum EfObjectShape { Basic,Native,Generated }
public enum DesignObjectShape { ScaffoldMetadata,ScaffoldSix,GuardedDt,GuardedDto,History }
public sealed class OwnedObjects
{
    public sealed record Column(string Name,string Kind,int? Scale=null,int? Leading=null);
    public sealed class Entry
    {
        public string CaseId{get;set;}="";public string Nonce{get;set;}="";public string Table{get;set;}="";public string OwnerMarker{get;set;}="";
        public string PrimaryKey{get;set;}="";public string? Index{get;set;}public string? Check{get;set;}public bool IndexAcknowledged{get;set;}public bool CheckAcknowledged{get;set;}public string Shape{get;set;}="";public int Scale{get;set;}
        public int? AlterBeforeScale{get;set;}public int? AlterTargetScale{get;set;}public object? AlterBeforeCatalog{get;set;}public object? AlterAfterCatalog{get;set;}
        public string CreationState{get;set;}="reserved";public bool AbsentBeforeCreate{get;set;}public bool CreateAcknowledged{get;set;}public bool CommentAcknowledged{get;set;}
        public CreateCompletion? CreateEvidence{get;set;}public CreateCompletion? CommentEvidence{get;set;}public object? CreateFailure{get;set;}
        public List<Column> Columns{get;set;}=[];public string? LiteralStoreType{get;set;}
    }
    sealed class Ledger
    {public int SchemaVersion{get;set;}=1;public string Profile{get;set;}="";public string PackageSha256{get;set;}=CandidateGuard.PackageSha;public string IdentitySha256{get;set;}=CandidateGuard.IdentitySha;public List<Entry> Objects{get;set;}=[];}
    readonly CandidateGuard guard;readonly string path;readonly Ledger ledger;
    public IReadOnlyList<Entry> Entries=>ledger.Objects;
    public OwnedObjects(CandidateGuard guard,string ledgerPath){this.guard=guard;path=ledgerPath;SafeEvidence.Require(!File.Exists(path),EvidenceCode.LedgerIdentity);ledger=new(){Profile=guard.Profile};using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);JsonSerializer.Serialize(file,ledger,SafeEvidence.Json);}
    OwnedObjects(CandidateGuard guard,string path,Ledger ledger){this.guard=guard;this.path=path;this.ledger=ledger;ValidateLedger();}
    public static OwnedObjects LoadForCleanup(CandidateGuard guard,string path)=>new(guard,path,JsonSerializer.Deserialize<Ledger>(SafeEvidence.Load(path).GetRawText(),SafeEvidence.Json)??throw new EvidenceException(EvidenceCode.LedgerIdentity));
    void Save()=>SafeEvidence.Write(path,ledger);
    Entry Register(string caseId,string shape,int scale,List<Column> columns,string? literalStoreType=null)
    {
        SafeEvidence.Require(Regex.IsMatch(caseId,"\\A[A-Za-z0-9_-]{1,96}\\z",RegexOptions.CultureInvariant),EvidenceCode.Usage);
        string nonce=Guid.NewGuid().ToString("N").ToUpperInvariant();var entry=new Entry{CaseId=caseId,Nonce=nonce,Table="TPL_"+nonce[..24],OwnerMarker="OWN_"+nonce[..24],PrimaryKey="TP07PK_"+nonce[..24],Shape=shape,Scale=scale,Columns=columns,LiteralStoreType=literalStoreType};if(shape is "GuardedDt" or "GuardedDto"){entry.Index="TP07IX_"+nonce[..24];entry.Check="TP07CK_"+nonce[..24];}
        using(var c=guard.OpenTestConnection()){SafeEvidence.Require(TableCount(c,entry.Table)==0,EvidenceCode.OwnershipCollision);}entry.AbsentBeforeCreate=true;ledger.Objects.Add(entry);Save();return entry;
    }
    public Entry RegisterLiteral(string caseId,string storeType)
    {
        SafeEvidence.Require(storeType is "TIMESTAMP(7)" or "DATETIME(7) WITH TIME ZONE" or "TIMESTAMP(6)" or "DATETIME(6) WITH TIME ZONE" or "TIMESTAMP" or "DATETIME" or "DATETIME WITH TIME ZONE",EvidenceCode.SchemaMismatch);
        var entry=Register(caseId,"Literal",storeType.Contains("(7)",StringComparison.Ordinal)?7:6,[new("ID","INT"),new("V",storeType.Contains("TIME ZONE",StringComparison.Ordinal)?"TZ":"DT",storeType.Contains('(')?(storeType.Contains("(7)",StringComparison.Ordinal)?7:6):null)],storeType);return entry;
    }
    public Entry RegisterEfTable(string caseId,EfObjectShape shape,int scale=6)
    {
        SafeEvidence.Require(scale is 6 or 7,EvidenceCode.SchemaMismatch);
        List<Column> columns=shape==EfObjectShape.Native?[new("ID","INT"),new("A","DATE",0),new("T","TIME",6),new("I","INTERVAL",6,9)]:[new("ID","INT"),new("D","DT",scale),new("Z","TZ",scale),new("N","VARCHAR32")];
        return Register(caseId,shape.ToString(),scale,columns);
    }
    public Entry RegisterDesignTable(string caseId,DesignObjectShape shape,int scale=6)
    {
        SafeEvidence.Require(scale is 6 or 7,EvidenceCode.SchemaMismatch);List<Column> columns;
        if(shape==DesignObjectShape.History)columns=[new("MigrationId","VARCHAR150"),new("ProductVersion","VARCHAR32")];
        else if(shape==DesignObjectShape.ScaffoldSix)columns=[new("ID","INT"),new("D","DT",6),new("Z","TZ",6)];
        else if(shape is DesignObjectShape.GuardedDt or DesignObjectShape.GuardedDto)columns=[new("ID","INT"),new("V",shape==DesignObjectShape.GuardedDto?"TZ":"DT",scale)];
        else {columns=[new("ID","INT")];foreach(int n in new[]{0,6,7,8,9}){columns.Add(new("D"+n,"DT",n));columns.Add(new("Z"+n,"TZ",n));}columns.Add(new("I9","INTERVAL",6,9));columns.Add(new("I2","INTERVAL",6,2));columns.Add(new("L","TLTZ",6));}
        var entry=Register(caseId,shape.ToString(),scale,columns);if(shape is DesignObjectShape.GuardedDt or DesignObjectShape.GuardedDto){entry.Index="TP07IX_"+entry.Nonce[..24];entry.Check="TP07CK_"+entry.Nonce[..24];Save();}return entry;
    }
    public void BeginExternalCreate(Entry entry){ValidateEntry(entry);SafeEvidence.Require(entry.CreationState=="reserved",EvidenceCode.LedgerIdentity);using(var c=guard.OpenTestConnection())SafeEvidence.Require(TableCount(c,entry.Table)==0,EvidenceCode.OwnershipCollision);entry.CreationState="create_inflight";Save();}
    public void FailExternalCreate(Entry entry,Exception error)
    {
        ValidateEntry(entry);entry.CreateFailure=SafeEvidence.Safe(error);if(!entry.CreateAcknowledged&&error.GetType().GetProperty("HasVerifiedServerResponse")?.GetValue(error) is true)entry.CreationState="known_create_failure";Save();
    }
    public sealed record CreateCompletion(bool CallReturned,string Api,string SqlSha256);
    public void RecordExternalCreateAcknowledged(Entry entry,CreateCompletion completion)
    {
        ValidateEntry(entry);SafeEvidence.Require(entry.CreationState=="create_inflight"&&completion.CallReturned&&completion.Api is "ExecuteNonQuery" or "ExecuteNonQueryAsync" or "ExecuteSqlRaw" or "ExecuteSqlRawAsync"&&Regex.IsMatch(completion.SqlSha256,"\\A[a-f0-9]{64}\\z"),EvidenceCode.LedgerIdentity);
        entry.CreateEvidence=completion;entry.CreateAcknowledged=true;entry.CreationState="create_acknowledged";Save();
    }
    public void RecordExternalChildAcknowledged(Entry entry,string name)
    {
        ValidateEntry(entry);SafeEvidence.Require(entry.CreateAcknowledged,EvidenceCode.LedgerIdentity);if(name==entry.Index)entry.IndexAcknowledged=true;else if(name==entry.Check)entry.CheckAcknowledged=true;else throw new EvidenceException(EvidenceCode.OwnedName);Save();
    }
    public void ConfirmExternalCreate(Entry entry,CreateCompletion? completion=null)
    {
        if(!entry.CreateAcknowledged){SafeEvidence.Require(completion is not null,EvidenceCode.LedgerIdentity);RecordExternalCreateAcknowledged(entry,completion!);}ValidateEntry(entry);
        using var c=guard.OpenTestConnection();VerifyStructure(c,entry);string sql=$"COMMENT ON TABLE {entry.Table} IS 'TP07_{entry.Nonce}'";using(var cmd=CandidateGuard.Command(c,sql)){cmd.ExecuteNonQuery();}
        entry.CommentEvidence=new(true,"ExecuteNonQuery",SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(sql)));entry.CommentAcknowledged=true;entry.CreationState="confirmed";Save();SafeEvidence.Require(CommentMatches(c,entry),EvidenceCode.MarkerMismatch);
    }
    public object Preflight(Entry entry)
    {
        ValidateEntry(entry);SafeEvidence.Require(entry.CreationState=="confirmed",EvidenceCode.LedgerIdentity);using var c=guard.OpenTestConnection();VerifyStructure(c,entry);SafeEvidence.Require(entry.Shape=="Literal"?DefaultMarkerMatches(c,entry):CommentMatches(c,entry),EvidenceCode.MarkerMismatch);return CatalogColumns(c,entry.Table);
    }
    public void Create(Entry entry)
    {
        SafeEvidence.Require(entry.Shape=="Literal"&&entry.LiteralStoreType is not null,EvidenceCode.SchemaMismatch);BeginExternalCreate(entry);
        string sql=$"CREATE TABLE {entry.Table}(ID INT NOT NULL,V {entry.LiteralStoreType},{entry.OwnerMarker} VARCHAR(32) DEFAULT '{entry.Nonce}',CONSTRAINT {entry.PrimaryKey} PRIMARY KEY(ID))";
        try{using var c=guard.OpenTestConnection();using var cmd=CandidateGuard.Command(c,sql);cmd.ExecuteNonQuery();RecordExternalCreateAcknowledged(entry,new(true,"ExecuteNonQuery",SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(sql))));}catch(Exception error){FailExternalCreate(entry,error);throw;}
        entry.CreationState="confirmed";Save();
    }
    public void BeginExternalAlter(Entry entry,int newScale)
    {
        ValidateEntry(entry);SafeEvidence.Require(entry.Shape is "GuardedDt" or "GuardedDto"&&entry.CreationState=="confirmed"&&(newScale is 6 or 7)&&newScale!=entry.Scale,EvidenceCode.SchemaMismatch);
        using var c=guard.OpenTestConnection();VerifyStructure(c,entry);SafeEvidence.Require(CommentMatches(c,entry),EvidenceCode.MarkerMismatch);entry.AlterBeforeScale=entry.Scale;entry.AlterTargetScale=newScale;entry.AlterBeforeCatalog=CatalogColumns(c,entry.Table);entry.CreationState="alter_inflight";Save();
    }
    public void ConfirmExternalAlter(Entry entry,int newScale)
    {
        SafeEvidence.Require(entry.CreationState=="alter_inflight"&&entry.AlterTargetScale==newScale,EvidenceCode.LedgerIdentity);using var c=guard.OpenTestConnection();SafeEvidence.Require(CommentMatches(c,entry),EvidenceCode.MarkerMismatch);
        int original=entry.Scale;var originalColumn=entry.Columns.Single(v=>v.Name=="V");entry.Scale=newScale;entry.Columns[entry.Columns.IndexOf(originalColumn)]=originalColumn with{Scale=newScale};
        try{VerifyStructure(c,entry);entry.AlterAfterCatalog=CatalogColumns(c,entry.Table);entry.CreationState="confirmed";Save();}catch{entry.Scale=original;entry.Columns[entry.Columns.FindIndex(v=>v.Name=="V")]=originalColumn;throw;}
    }
    public object CatalogContract(Entry entry){ValidateEntry(entry);using var c=guard.OpenTestConnection();return CatalogColumns(c,entry.Table);}
    public object Cleanup()
    {
        ValidateLedger();var results=new List<object>();foreach(var entry in ledger.Objects.AsEnumerable().Reverse())
        {
            using(var c=guard.OpenTestConnection())
            {
                bool present=TableCount(c,entry.Table)!=0;bool dropped=false;if(present&&entry.CreationState=="alter_inflight")ReconcileAlter(c,entry);
                if(present)
                {
                    SafeEvidence.Require(entry.CreationState is "confirmed" or "create_acknowledged" or "create_inflight",EvidenceCode.MarkerMismatch);VerifyStructure(c,entry,allowUncreatedChildren:entry.CreationState=="create_acknowledged");
                    if(entry.Shape=="Literal")SafeEvidence.Require(DefaultMarkerMatches(c,entry),EvidenceCode.MarkerMismatch);
                    else if(!CommentMatches(c,entry))SafeEvidence.Require(entry.CreateAcknowledged&&entry.CreationState=="create_acknowledged"&&!entry.CommentAcknowledged,EvidenceCode.MarkerMismatch);
                    using var drop=CandidateGuard.Command(c,$"DROP TABLE {entry.Table}");drop.ExecuteNonQuery();dropped=true;
                }
                using var fresh=guard.OpenTestConnection();long count=TableCount(fresh,entry.Table);SafeEvidence.Require(count==0,EvidenceCode.CleanupFailed);results.Add(new{entry.Table,entry.Nonce,dropped,fresh_absent=true,fresh_table_count=count,identity=guard.VerifyOpenedConnection(fresh)});
            }
        }
        return new{cleanup_verified=true,objects=results,ledger_sha256=SafeEvidence.HashFile(path)};
    }
    void ReconcileAlter(DbConnection c,Entry entry)
    {
        SafeEvidence.Require(entry.Shape is "GuardedDt" or "GuardedDto"&&entry.AlterBeforeScale is 6 or 7&&entry.AlterTargetScale is 6 or 7,EvidenceCode.LedgerIdentity);SafeEvidence.Require(CommentMatches(c,entry),EvidenceCode.MarkerMismatch);
        try{VerifyStructure(c,entry);}catch(EvidenceException e)when(e.Code==EvidenceCode.SchemaMismatch)
        {
            int original=entry.Scale;var before=entry.Columns.Single(v=>v.Name=="V");entry.Scale=entry.AlterTargetScale!.Value;entry.Columns[entry.Columns.IndexOf(before)]=before with{Scale=entry.Scale};
            try{VerifyStructure(c,entry);}catch{entry.Scale=original;entry.Columns[entry.Columns.FindIndex(v=>v.Name=="V")]=before;throw;}
        }
        entry.AlterAfterCatalog=CatalogColumns(c,entry.Table);entry.CreationState="confirmed";Save();
    }
    void ValidateLedger(){SafeEvidence.Require(ledger.SchemaVersion==1&&ledger.Profile==guard.Profile&&ledger.PackageSha256==CandidateGuard.PackageSha&&ledger.IdentitySha256==CandidateGuard.IdentitySha,EvidenceCode.LedgerIdentity);foreach(var entry in ledger.Objects)ValidateEntry(entry);}
    void ValidateEntry(Entry entry)
    {
        SafeEvidence.Require(ledger.Objects.Contains(entry)&&Regex.IsMatch(entry.Nonce,"\\A[A-F0-9]{32}\\z",RegexOptions.CultureInvariant)&&entry.Table=="TPL_"+entry.Nonce[..24]&&entry.OwnerMarker=="OWN_"+entry.Nonce[..24]&&entry.PrimaryKey=="TP07PK_"+entry.Nonce[..24]&&entry.AbsentBeforeCreate,EvidenceCode.LedgerIdentity);
        SafeEvidence.Require(entry.CreationState is "reserved" or "create_inflight" or "create_acknowledged" or "confirmed" or "alter_inflight" or "known_create_failure",EvidenceCode.LedgerIdentity);
        var expected=ExpectedColumns(entry);SafeEvidence.Require(entry.Columns.SequenceEqual(expected),EvidenceCode.SchemaMismatch);
        bool guarded=entry.Shape is "GuardedDt" or "GuardedDto";SafeEvidence.Require(entry.Index==(guarded?"TP07IX_"+entry.Nonce[..24]:null)&&entry.Check==(guarded?"TP07CK_"+entry.Nonce[..24]:null),EvidenceCode.LedgerIdentity);
    }
    static List<Column> ExpectedColumns(Entry entry)
    {
        if(entry.Shape=="Literal")
        {
            SafeEvidence.Require(entry.LiteralStoreType is "TIMESTAMP(7)" or "DATETIME(7) WITH TIME ZONE" or "TIMESTAMP(6)" or "DATETIME(6) WITH TIME ZONE" or "TIMESTAMP" or "DATETIME" or "DATETIME WITH TIME ZONE",EvidenceCode.SchemaMismatch);
            string store=entry.LiteralStoreType!;int? scale=store.Contains('(')?(store.Contains("(7)",StringComparison.Ordinal)?7:6):null;return [new("ID","INT"),new("V",store.Contains("TIME ZONE",StringComparison.Ordinal)?"TZ":"DT",scale)];
        }
        SafeEvidence.Require(entry.Scale is 6 or 7,EvidenceCode.SchemaMismatch);
        if(entry.Shape=="Native")return [new("ID","INT"),new("A","DATE",0),new("T","TIME",6),new("I","INTERVAL",6,9)];
        if(entry.Shape is "Basic" or "Generated")return [new("ID","INT"),new("D","DT",entry.Scale),new("Z","TZ",entry.Scale),new("N","VARCHAR32")];
        if(entry.Shape=="ScaffoldSix")return [new("ID","INT"),new("D","DT",6),new("Z","TZ",6)];
        if(entry.Shape is "GuardedDt" or "GuardedDto")return [new("ID","INT"),new("V",entry.Shape=="GuardedDto"?"TZ":"DT",entry.Scale)];
        if(entry.Shape=="History")return [new("MigrationId","VARCHAR150"),new("ProductVersion","VARCHAR32")];
        SafeEvidence.Require(entry.Shape=="ScaffoldMetadata",EvidenceCode.SchemaMismatch);var columns=new List<Column>{new("ID","INT")};foreach(int n in new[]{0,6,7,8,9}){columns.Add(new("D"+n,"DT",n));columns.Add(new("Z"+n,"TZ",n));}columns.Add(new("I9","INTERVAL",6,9));columns.Add(new("I2","INTERVAL",6,2));columns.Add(new("L","TLTZ",6));return columns;
    }
    public static long TableCount(DbConnection c,string table){using var cmd=CandidateGuard.Command(c,"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME=:t");Add(cmd,"t",table);return Convert.ToInt64(cmd.ExecuteScalar(),CultureInfo.InvariantCulture);}
    static void Add(DbCommand cmd,string name,string value){var p=cmd.CreateParameter();p.ParameterName=name;p.DbType=DbType.String;p.Value=value;cmd.Parameters.Add(p);}
    static bool DefaultMarkerMatches(DbConnection c,Entry entry){using var cmd=CandidateGuard.Command(c,"SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t AND COLUMN_NAME=:m");Add(cmd,"t",entry.Table);Add(cmd,"m",entry.OwnerMarker);using var r=cmd.ExecuteReader();if(!r.Read()||r.IsDBNull(0))return false;string text=r.GetString(0).Trim();while(text.Length>=2&&text[0]=='('&&text[^1]==')')text=text[1..^1].Trim();return text=="'"+entry.Nonce+"'"&&!r.Read();}
    static bool CommentMatches(DbConnection c,Entry entry){using var cmd=CandidateGuard.Command(c,"SELECT COMMENTS FROM ALL_TAB_COMMENTS WHERE OWNER=:s AND TABLE_NAME=:t AND TABLE_TYPE='TABLE'");Add(cmd,"s",CandidateGuard.User);Add(cmd,"t",entry.Table);using var r=cmd.ExecuteReader();return r.Read()&&!r.IsDBNull(0)&&r.GetString(0)=="TP07_"+entry.Nonce&&!r.Read();}
    static List<Dictionary<string,object?>> CatalogColumns(DbConnection c,string table)
    {
        using var cmd=CandidateGuard.Command(c,"SELECT COLUMN_NAME,DATA_TYPE,DATA_PRECISION,DATA_SCALE,NULLABLE,DATA_LENGTH FROM USER_TAB_COLUMNS WHERE TABLE_NAME=:t ORDER BY COLUMN_ID");Add(cmd,"t",table);using var r=cmd.ExecuteReader();var result=new List<Dictionary<string,object?>>();while(r.Read()){SafeEvidence.Require(result.Count<32,EvidenceCode.SchemaMismatch);result.Add(new(){["name"]=r.GetString(0),["data_type"]=r.GetString(1),["precision"]=r.IsDBNull(2)?null:Convert.ToInt32(r.GetValue(2),CultureInfo.InvariantCulture),["scale"]=r.IsDBNull(3)?null:Convert.ToInt32(r.GetValue(3),CultureInfo.InvariantCulture),["nullable"]=r.GetString(4),["data_length"]=r.IsDBNull(5)?null:Convert.ToInt32(r.GetValue(5),CultureInfo.InvariantCulture)});}return result;
    }
    static void VerifyStructure(DbConnection c,Entry entry,bool allowUncreatedChildren=false)
    {
        var actual=CatalogColumns(c,entry.Table);var expected=entry.Columns.Select(v=>v.Name.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);if(entry.Shape=="Literal")expected.Add(entry.OwnerMarker);
        SafeEvidence.Require(actual.Count==expected.Count&&actual.Select(v=>((string)v["name"]!).ToUpperInvariant()).ToHashSet(StringComparer.Ordinal).SetEquals(expected),EvidenceCode.SchemaMismatch);
        foreach(var column in entry.Columns)
        {
            var observed=actual.Single(v=>string.Equals((string)v["name"]!,column.Name,StringComparison.OrdinalIgnoreCase));string type=((string)observed["data_type"]!).ToUpperInvariant();
            bool valid=column.Kind switch{"INT"=>type is "INT" or "INTEGER","DT"=>type is "TIMESTAMP" or "DATETIME","TZ"=>type.Contains("TIME ZONE",StringComparison.Ordinal)&&!type.Contains("LOCAL",StringComparison.Ordinal),"DATE"=>type=="DATE","TIME"=>type=="TIME","INTERVAL"=>type.StartsWith("INTERVAL DAY",StringComparison.Ordinal),"TLTZ"=>type.Contains("LOCAL TIME ZONE",StringComparison.Ordinal),"VARCHAR32" or "VARCHAR150"=>type.StartsWith("VARCHAR",StringComparison.Ordinal),_=>false};
            SafeEvidence.Require(valid,EvidenceCode.SchemaMismatch);if(column.Kind is "VARCHAR32" or "VARCHAR150")SafeEvidence.Require(observed["data_length"] is int size&&size==(column.Kind=="VARCHAR32"?32:150),EvidenceCode.SchemaMismatch);if(column.Scale.HasValue){int? rawScale=observed["scale"] as int?;int? decodedScale=column.Kind=="TLTZ"&&rawScale is >=4096?rawScale-4096:rawScale;SafeEvidence.Require(decodedScale==column.Scale.Value,EvidenceCode.SchemaMismatch);}if(column.Leading.HasValue)SafeEvidence.Require(observed["precision"] is int n&&n==column.Leading.Value,EvidenceCode.SchemaMismatch);
        }
        using var keys=CandidateGuard.Command(c,"SELECT CONSTRAINT_NAME FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t AND CONSTRAINT_TYPE='P'");Add(keys,"t",entry.Table);using var r=keys.ExecuteReader();SafeEvidence.Require(r.Read()&&r.GetString(0)==entry.PrimaryKey&&!r.Read(),EvidenceCode.SchemaMismatch);
        foreach(var child in new[]{(entry.Index,"SELECT INDEX_NAME FROM USER_INDEXES WHERE TABLE_NAME=:t AND INDEX_NAME=:n"),(entry.Check,"SELECT CONSTRAINT_NAME FROM USER_CONSTRAINTS WHERE TABLE_NAME=:t AND CONSTRAINT_NAME=:n AND CONSTRAINT_TYPE='C'")})if(child.Item1 is not null&&(!allowUncreatedChildren||(child.Item1==entry.Index?entry.IndexAcknowledged:entry.CheckAcknowledged))){using var cmd=CandidateGuard.Command(c,child.Item2);Add(cmd,"t",entry.Table);Add(cmd,"n",child.Item1);SafeEvidence.Require(Convert.ToString(cmd.ExecuteScalar(),CultureInfo.InvariantCulture)==child.Item1,EvidenceCode.SchemaMismatch);}
    }
}
