using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TP07.Runtime;

public enum EvidenceCode
{
    Usage, FreshOutput, CandidateManifestPin, CapabilitiesPin, PackagePin, DllPin, MvidPin,
    Dependencies, EfVersion, OfficialFallback, AssetSelection, TypeRoute, PhysicalProfile,
    TestIdentity, TransportTarget, LedgerIdentity, OwnedName, OwnershipCollision,
    MarkerMismatch, SchemaMismatch, CleanupFailed, MissingRow, ExtraRow, RawScope,
    RawLength, RawMetadata, RawDecode, ServerText, OracleMismatch, LiteralSource,
    LiteralPrecision, FacetPolicy, ExpectedRejection, LiteralSafety, SourceBinding
}
public sealed class EvidenceException(EvidenceCode code) : InvalidOperationException("TP07_evidence_failure")
{ public EvidenceCode Code { get; } = code; }
public static class SafeEvidence
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented=true, PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
    public static void Require(bool value,EvidenceCode code){if(!value)throw new EvidenceException(code);}
    public static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    public static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
    public static JsonElement Load(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllBytes(path));
        void Check(JsonElement value)
        {
            if(value.ValueKind==JsonValueKind.Object){var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var property in value.EnumerateObject()){Require(seen.Add(property.Name),EvidenceCode.SourceBinding);Check(property.Value);}}
            else if(value.ValueKind==JsonValueKind.Array)foreach(var child in value.EnumerateArray())Check(child);
        }
        Check(document.RootElement);return document.RootElement.Clone();
    }
    public static object Safe(Exception exception)
    {
        while(exception is TargetInvocationException && exception.InnerException is not null)exception=exception.InnerException;
        int? number=exception.GetType().GetProperty("Number")?.GetValue(exception) is int n?n:null;
        string? code=exception is EvidenceException own&&Enum.IsDefined(own.Code)?own.Code.ToString():null;
        return new {type=exception.GetType().FullName,code,number};
    }
    public static object Value(object? value)=>value switch
    {
        DateTime date=>new {type="System.DateTime",ticks=date.Ticks,kind=date.Kind.ToString()},
        DateTimeOffset offset=>new {type="System.DateTimeOffset",ticks=offset.Ticks,offset_minutes=(int)offset.Offset.TotalMinutes,utc_ticks=offset.UtcTicks},
        DBNull=>new {type="System.DBNull",is_null=true},
        _=>new {type=value?.GetType().FullName}
    };
    public static void Write(string path,object value)
    {
        string temporary=path+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(value,Json));File.Move(temporary,path,true);
    }
    public static Dictionary<string,object?> StartStep(Dictionary<string,object?> owner,string id)
    {
        if(!owner.TryGetValue("steps",out var value))owner["steps"]=value=new List<Dictionary<string,object?>>();
        var step=new Dictionary<string,object?>{["step_id"]=id,["status"]="attempted"};((List<Dictionary<string,object?>>)value!).Add(step);return step;
    }
    public static void CompleteStep(Dictionary<string,object?> step,object? result=null){step["status"]="completed";if(result is not null)step["result"]=result;}
    public static void FailStep(Dictionary<string,object?> step,Exception error){step["status"]="failed";step["error"]=Safe(error);}
    public static string Identifier(string value)
    {Require(Regex.IsMatch(value,"^[A-Z][A-Z0-9_]{0,127}$",RegexOptions.CultureInvariant),EvidenceCode.OwnedName);return value;}
}
