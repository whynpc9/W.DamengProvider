using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TP07.Runtime;

namespace TP07.ConstructorScreen;

internal sealed record ValueInput(string Kind,long? Ticks,int? OffsetMinutes)
{
    internal object Value
    {
        get
        {
            SafeEvidence.Require(Kind is "dt" or "dto",EvidenceCode.Usage);
            if(!Ticks.HasValue){SafeEvidence.Require(OffsetMinutes is null,EvidenceCode.Usage);return DBNull.Value;}
            var clock=new DateTime(Ticks.Value,DateTimeKind.Unspecified);
            if(Kind=="dt"){SafeEvidence.Require(OffsetMinutes is null,EvidenceCode.Usage);return clock;}
            SafeEvidence.Require(OffsetMinutes.HasValue,EvidenceCode.Usage);
            var value=new DateTimeOffset(clock,TimeSpan.FromMinutes(OffsetMinutes!.Value));
            SafeEvidence.Require(value.Ticks==Ticks.Value&&value.Offset.TotalMinutes==OffsetMinutes.Value
                &&value.UtcTicks>=DateTime.MinValue.Ticks&&value.UtcTicks<=DateTime.MaxValue.Ticks,EvidenceCode.Usage);return value;
        }
    }
    internal bool IsOffset=>Kind=="dto";
    internal string StoreType=>IsOffset?"DATETIME(7) WITH TIME ZONE":"TIMESTAMP(7)";
}
internal sealed record ExpansionCase(string Id,int Ordinal,string Group,string Kind,string Suffix,ValueInput Input,
    int TypedRow,int Row,ValueInput? Adjacent=null)
{
    internal string Gate=>Group=="dateadd"?"R04":"literal";
}
internal static class Cases
{
    internal static readonly long Ordinary=new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).Ticks;
    internal static readonly long Micro=Ordinary+1_234_560;
    internal static readonly IReadOnlyList<ExpansionCase> Expansion=Create();
    internal static ValueInput Input(string kind,long? ticks,int? offset=null)=>new(kind,ticks,kind=="dto"&&ticks.HasValue?(offset??330):null);
    private static IReadOnlyList<ExpansionCase> Create()
    {
        var result=new List<ExpansionCase>();
        foreach(string kind in new[]{"dt","dto"})
        {
            var values=new List<(string Suffix,long? Ticks,int? Offset)>();
            for(int n=0;n<10;n++)values.Add(("tail"+n,Cases.Micro+n,kind=="dto"?330:null));
            values.AddRange([("min",0,kind=="dto"?0:null),("min_tick",1,kind=="dto"?0:null),
                ("max",DateTime.MaxValue.Ticks,kind=="dto"?0:null),("max_tick",DateTime.MaxValue.Ticks-1,kind=="dto"?0:null),
                ("max_9ticks",DateTime.MaxValue.Ticks-9,kind=="dto"?0:null),
                ("carry_last",Ordinary+9_999_999,kind=="dto"?330:null),("carry_next",Ordinary+10_000_000,kind=="dto"?330:null),("NULL",null,null)]);
            if(kind=="dto")values.AddRange([("offset_0",Micro+7,0),("offset_m330",Micro+7,-330),("offset_m779",Micro+7,-779),("offset_p840",Micro+7,840)]);
            for(int i=0;i<values.Count;i++)
            {
                var value=values[i];var input=new ValueInput(kind,value.Ticks,value.Offset);_ =input.Value;
                result.Add(new("F7-V-"+kind.ToUpperInvariant()+"-"+value.Suffix,result.Count+1,"domain",kind,value.Suffix,input,2*i+1,2*i+2));
            }
        }
        foreach(string kind in new[]{"dt","dto"})foreach(string suffix in new[]{"min_tick","max"})
            result.Add(new("F7-DEF-"+kind.ToUpperInvariant()+"-"+suffix,result.Count+1,"default",kind,suffix,
                Input(kind,suffix=="min_tick"?1:DateTime.MaxValue.Ticks,0),1,2));
        foreach(string kind in new[]{"dt","dto"})foreach(string suffix in new[]{"tail7","min_tick","max"})
        {
            var target=result.Single(spec=>spec.Group=="domain"&&spec.Kind==kind&&spec.Suffix==suffix);
            result.Add(new("F7-P-"+kind.ToUpperInvariant()+"-"+suffix,result.Count+1,"predicate",kind,suffix,target.Input,target.TypedRow,target.Row,
                Input(kind,target.Input.Ticks!.Value-1,target.Input.OffsetMinutes)));
        }
        foreach(string kind in new[]{"dt","dto"})result.Add(new("F7-U-"+kind.ToUpperInvariant(),result.Count+1,"assignment",kind,"tail7",Input(kind,Micro+7),15,kind=="dt"?37:45));
        result.Add(new("F7-A-DT-second1",result.Count+1,"dateadd","dt","second1",Input("dt",Micro+7),39,38));
        SafeEvidence.Require(result.Count==53&&result.Count(spec=>spec.Group=="domain")==40&&result.Take(52).All(spec=>spec.Gate=="literal"),EvidenceCode.Usage);return result;
    }
    internal static string Expression(ValueInput input)
    {
        if(!input.Ticks.HasValue)return "NULL";
        _ =input.Value;
        string clock=new DateTime(input.Ticks.Value,DateTimeKind.Unspecified).ToString("yyyy-MM-dd HH:mm:ss.fffffff",CultureInfo.InvariantCulture);
        if(!input.IsOffset)return $"TO_TIMESTAMP('{clock}','YYYY-MM-DD HH24:MI:SS.FF7')";
        int offset=input.OffsetMinutes!.Value;
        string zone=(offset<0?"-":"+")+(Math.Abs(offset)/60).ToString("D2",CultureInfo.InvariantCulture)+":"+(Math.Abs(offset)%60).ToString("D2",CultureInfo.InvariantCulture);
        return $"TO_TIMESTAMP_TZ('{clock} {zone}','YYYY-MM-DD HH24:MI:SS.FF7 TZH:TZM')";
    }
    internal static ExpansionCase Domain(string kind,string suffix)=>Expansion.Single(spec=>spec.Group=="domain"&&spec.Kind==kind&&spec.Suffix==suffix);
}
internal sealed class MappingContext(string connectionString):DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)=>options.UseDameng(connectionString).EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
    protected override void OnModelCreating(ModelBuilder model){model.Entity<MappingRow>().HasKey(row=>row.Id);model.Entity<MappingRow>().Property(row=>row.Id).ValueGeneratedNever();}
}
internal sealed class MappingRow { public int Id{get;set;} public DateTime Date{get;set;} public DateTimeOffset Offset{get;set;} }
