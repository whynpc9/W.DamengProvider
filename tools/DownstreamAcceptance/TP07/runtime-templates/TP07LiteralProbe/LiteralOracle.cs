using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using TP07.Runtime;
using W.Dm;

namespace TP07LiteralProbe;

// Ordinary temporal cache positions and integer bit layouts copied as a minimal accepted TP06 oracle subset.
// Expected values never call driver codecs/formatters. This decoder has no encoder.
// Frozen provenance: TemporalCandidateAcceptance/Observations.cs SHA256 1385627c9a2ec3c5d147e43e40c9d01935fa9415bec2d743c4f490fd615666fb;
// Safety.cs metadata/cache subset SHA256 81166c64488dda125bf0d7e92adeefdb6e97249767c5a5251451fcbe6ec7e98f;
// TemporalBoundaryProbe/packed_oracle.py integer layout SHA256 bc9ed2f5e72960d88065086d26f3c670edeb14a1ed5e1ca1f48b45140db7aae9.
public sealed record TemporalOracleReceipt(long Rows,bool IsNull,long? LocalTicks,int? OffsetMinutes,object? Raw,string? FF9,object? Typed,object? Metadata,object Identity,object? ReaderRoute);
public static class LiteralOracle
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    public sealed record Fields(int Year,int Month,int Day,int Hour,int Minute,int Second,int Nanoseconds,int? OffsetMinutes);
    public static TemporalOracleReceipt ReadFresh(CandidateGuard guard,string table,int rowId,bool isOffset,int? expectedScale=null,string column="V",Dictionary<string,object?>? partialSink=null)
    {
        SafeEvidence.Identifier(table);SafeEvidence.Require(column is "V" or "D" or "Z",EvidenceCode.OwnedName);
        var attempt=new Dictionary<string,object?>{["table"]=table,["row_id"]=rowId,["column"]=column,["completed"]=false};
        if(partialSink is not null){if(!partialSink.TryGetValue("oracle_read_attempts",out var old))partialSink["oracle_read_attempts"]=old=new List<object>();((List<object>)old!).Add(attempt);}
        try
        {
        using var c=guard.OpenTestConnection();var identity=guard.VerifyOpenedConnection(c);long count=Convert.ToInt64(CandidateGuard.Scalar(c,$"SELECT COUNT(*) FROM {table} WHERE ID={rowId}"),CultureInfo.InvariantCulture);
        attempt["rows"]=count;attempt["identity"]=identity;if(count==0){attempt["completed"]=true;return new(0,false,null,null,null,null,null,null,identity,null);}SafeEvidence.Require(count==1,EvidenceCode.ExtraRow);
        string format=isOffset?"YYYY-MM-DD HH24:MI:SS.FF9 TZH:TZM":"YYYY-MM-DD HH24:MI:SS.FF9";
        using var cmd=CandidateGuard.Command(c,$"SELECT {column},TO_CHAR({column},'{format}') FROM {table} WHERE ID={rowId}");guard.VerifyObject(cmd,"command");using var r=cmd.ExecuteReader();var route=guard.VerifyObject(r,"reader");SafeEvidence.Require(r.Read(),EvidenceCode.MissingRow);
        object metadata=CaptureMetadata(r);attempt["metadata"]=metadata;var fieldsJson=System.Text.Json.JsonSerializer.SerializeToElement(metadata,SafeEvidence.Json);int type=fieldsJson.GetProperty("c_type").GetInt32(),width=fieldsJson.GetProperty("width").GetInt32(),scale=fieldsJson.GetProperty("scale").GetInt32();
        SafeEvidence.Require(fieldsJson.GetProperty("mask").GetInt32()==0&&fieldsJson.GetProperty("is_bdta").ValueKind==System.Text.Json.JsonValueKind.False&&type is 16 or 23 or 26 or 27,EvidenceCode.RawMetadata);
        object cache=Field(r,"m_RsCache");var data=Field(cache,"datas") as byte[][][];int index=(int)Field(cache,"datasOffset");bool bdta=(bool)Field(cache,"isRsBdta");SafeEvidence.Require(!bdta&&data is not null&&index>=0&&index<data.Length&&data[index].Length>1,EvidenceCode.RawScope);
        byte[]? bytes=data![index][1];attempt["cell"]=new{payload_length=bytes?.Length,payload_sha256=bytes is null?null:SafeEvidence.Hash(bytes),is_null=bytes is null,result_format="ordinary_non_BDTA"};bool nil=r.IsDBNull(0);
        if(nil){SafeEvidence.Require(bytes is null&&r.IsDBNull(1),EvidenceCode.RawMetadata);object value=r.GetValue(0);SafeEvidence.Require(value is DBNull,EvidenceCode.OracleMismatch);SafeEvidence.Require(!r.Read(),EvidenceCode.ExtraRow);attempt["completed"]=true;return new(1,true,null,null,null,null,SafeEvidence.Value(value),metadata,identity,route);}
        int length=type switch{16=>8,23=>10,26=>9,27=>11,_=>0};SafeEvidence.Require(bytes is not null&&bytes.Length==length&&width==length&&length<=11,EvidenceCode.RawLength);
        attempt["raw"]=new{source="ordinary_result_cache_before_temporal_projection",capture_stage="before_any_temporal_getter",c_type=type,width,scale,is_bdta=false,hex=Convert.ToHexString(bytes!),payload_length=bytes!.Length,payload_sha256=SafeEvidence.Hash(bytes!)};
        var decoded=Decode(bytes!,type,scale);long ticks=Ticks(decoded);
        var raw=new{source="ordinary_result_cache_before_temporal_projection",capture_stage="before_any_temporal_getter",c_type=type,width,scale,is_bdta=false,hex=Convert.ToHexString(bytes!),payload_length=bytes!.Length,payload_sha256=SafeEvidence.Hash(bytes!),decoded};attempt["raw"]=raw;
        string server=r.GetString(1);attempt["ff9_shape"]=new{length=server.Length,sha256=server.Length<=80?SafeEvidence.Hash(System.Text.Encoding.UTF8.GetBytes(server)):null};var serverFields=ParseFF9(server,isOffset);attempt["ff9"]=server;SafeEvidence.Require(decoded==serverFields,EvidenceCode.OracleMismatch);
        object typed=isOffset?(object)((DmDataReader)r).GetDateTimeOffset(0):r.GetDateTime(0);attempt["typed"]=SafeEvidence.Value(typed);long typedTicks=typed is DateTimeOffset dto?dto.Ticks:((DateTime)typed).Ticks;int? offset=typed is DateTimeOffset offsetValue?(int)offsetValue.Offset.TotalMinutes:null;
        SafeEvidence.Require(typedTicks==ticks&&offset==decoded.OffsetMinutes,EvidenceCode.OracleMismatch);if(expectedScale.HasValue)SafeEvidence.Require(scale==expectedScale.Value,EvidenceCode.RawMetadata);SafeEvidence.Require(!r.Read(),EvidenceCode.ExtraRow);attempt["completed"]=true;
        return new(1,false,ticks,offset,raw,server,SafeEvidence.Value(typed),metadata,identity,route);
        }
        catch(Exception error){attempt["error"]=SafeEvidence.Safe(error);throw;}
    }
    public static bool Exact(TemporalOracleReceipt receipt,object expected)
    {
        long ticks=expected is DateTimeOffset dto?dto.Ticks:((DateTime)expected).Ticks;int? offset=expected is DateTimeOffset value?(int)value.Offset.TotalMinutes:null;
        return receipt.Rows==1&&!receipt.IsNull&&receipt.LocalTicks==ticks&&receipt.OffsetMinutes==offset;
    }
    static object CaptureMetadata(DbDataReader reader)
    {
        var columns=Field(reader,"m_ColInfo") as Array;SafeEvidence.Require(columns is not null&&columns.Length>=1,EvidenceCode.RawMetadata);object column=columns!.GetValue(0)!;
        int Call(string name)=>Convert.ToInt32(column.GetType().GetMethod(name,Flags,null,Type.EmptyTypes,null)!.Invoke(column,null),CultureInfo.InvariantCulture);
        object? raw=column.GetType().GetProperty("TemporalServerMetadata",Flags)?.GetValue(column);bool source=raw?.GetType().GetProperty("HasServerSource",Flags)?.GetValue(raw) is true;
        int? RawInt(string name)=>source&&raw!.GetType().GetProperty(name,Flags)?.GetValue(raw) is int n?n:null;
        string? origin=source?raw!.GetType().GetProperty("Source",Flags)?.GetValue(raw)?.ToString():null;SafeEvidence.Require(origin=="ResultColumn",EvidenceCode.RawMetadata);
        return new{c_type=Call("GetCType"),width=Call("GetPrecision"),scale=Call("GetScale"),mask=Convert.ToInt32(Field(column,"mask"),CultureInfo.InvariantCulture),is_bdta=(bool)Field(column,"isBdta"),metadata_role="result_column",raw_source=origin,raw_c_type=RawInt("CType"),raw_scale=RawInt("Scale"),raw_precision=RawInt("Precision"),raw_mask_before=RawInt("MaskBeforeNormalization"),raw_derived_mask=RawInt("DerivedMask"),raw_scale_status="captured_server_source",schema_scale=reader.GetSchemaTable()?.Rows[0]["NumericScale"]};
    }
    static object Field(object owner,string name)=>owner.GetType().GetField(name,Flags)?.GetValue(owner)??throw new EvidenceException(EvidenceCode.RawScope);
    static ulong Word(byte[] bytes,int start,int length){ulong value=0;for(int i=0;i<length;i++)value|=(ulong)bytes[start+i]<<(8*i);return value;}
    public static Fields Decode(byte[] bytes,int type,int scale)
    {
        int length=type switch{16=>8,23=>10,26=>9,27=>11,_=>0};SafeEvidence.Require(bytes.Length==length&&scale is >=0 and <=9,EvidenceCode.RawDecode);
        int date=(int)Word(bytes,0,3),year=date&0x7fff;if((year&0x4000)!=0)year-=0x8000;int month=(date>>15)&15,day=(date>>19)&31;
        bool extended=type is 26 or 27;int bits=extended?30:20;ulong time=Word(bytes,3,extended?6:5);int hour=(int)(time&31),minute=(int)((time>>5)&63),second=(int)((time>>11)&63);int fraction=(int)((time>>17)&((1UL<<bits)-1));int ns=extended?fraction:checked(fraction*1000);
        SafeEvidence.Require((time>>(17+bits))==0&&hour<=23&&minute<=59&&second<=59&&ns<=999999999&&(!extended?scale<=6:true),EvidenceCode.RawDecode);int divisor=1;for(int i=scale;i<9;i++)divisor*=10;SafeEvidence.Require(ns%divisor==0,EvidenceCode.RawDecode);
        int? offset=type is 23 or 27?(short)(bytes[^2]|bytes[^1]<<8):null;SafeEvidence.Require(offset is null or >=-840 and <=840,EvidenceCode.RawDecode);
        var fields=new Fields(year,month,day,hour,minute,second,ns,offset);_=Ticks(fields);return fields;
    }
    public static long Ticks(Fields f)
    {
        SafeEvidence.Require(f.Year is >=1 and <=9999&&f.Month is >=1 and <=12&&f.Nanoseconds%100==0,EvidenceCode.RawDecode);bool leap=f.Year%4==0&&(f.Year%100!=0||f.Year%400==0);int[] days=[31,leap?29:28,31,30,31,30,31,31,30,31,30,31];SafeEvidence.Require(f.Day>=1&&f.Day<=days[f.Month-1],EvidenceCode.RawDecode);
        long year=f.Year-1,total=365*year+year/4-year/100+year/400;for(int i=0;i<f.Month-1;i++)total+=days[i];total+=f.Day-1;
        return checked(total*864000000000L+(f.Hour*3600L+f.Minute*60L+f.Second)*10000000L+f.Nanoseconds/100);
    }
    public static Fields ParseFF9(string text,bool offset)
    {
        var match=Regex.Match(text,offset?@"\A([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{9}) ([+-])([0-9]{2}):([0-9]{2})\z":@"\A([0-9]{4})-([0-9]{2})-([0-9]{2}) ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{9})\z",RegexOptions.CultureInvariant);SafeEvidence.Require(match.Success&&text.Length<=40,EvidenceCode.ServerText);
        int N(int i)=>int.Parse(match.Groups[i].Value,CultureInfo.InvariantCulture);SafeEvidence.Require(N(4)<=23&&N(5)<=59&&N(6)<=59&&(!offset||N(9)<=14&&N(10)<=59&&N(9)*60+N(10)<=840),EvidenceCode.ServerText);
        int? minutes=offset?(N(9)*60+N(10))*(match.Groups[8].Value=="-"?-1:1):null;var fields=new Fields(N(1),N(2),N(3),N(4),N(5),N(6),N(7),minutes);_=Ticks(fields);return fields;
    }
}
