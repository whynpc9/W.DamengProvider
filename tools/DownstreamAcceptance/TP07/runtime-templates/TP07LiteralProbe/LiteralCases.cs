using Microsoft.EntityFrameworkCore;
using TP07.Runtime;

namespace TP07LiteralProbe;

public sealed record LiteralCase(string Id,string Property,string StoreType,object Value,int ParameterScale,bool ExpectedLocalOverflow=false,bool Bare=false);
public static class LiteralCases
{
    static DateTime Date(long fraction)=>new DateTime(2026,7,23,14,15,16,DateTimeKind.Unspecified).AddTicks(fraction);
    static DateTimeOffset Offset(long fraction)=>new(Date(fraction),TimeSpan.FromMinutes(330));
    public static IReadOnlyList<LiteralCase> ForProfile(string profile)
    {
        var cases=new List<LiteralCase>{new("LIT-DT6-micro",nameof(LiteralRow.DT6),"TIMESTAMP(6)",Date(1234560),6),new("LIT-DTO6-micro",nameof(LiteralRow.DTO6),"DATETIME(6) WITH TIME ZONE",Offset(1234560),6)};
        if(profile=="tls")return cases;
        cases.InsertRange(0,[new("LIT-DT7-tick1",nameof(LiteralRow.DT7),"TIMESTAMP(7)",Date(1),7),new("LIT-DT7-1234567",nameof(LiteralRow.DT7),"TIMESTAMP(7)",Date(1234567),7),new("LIT-DTO7-tick1",nameof(LiteralRow.DTO7),"DATETIME(7) WITH TIME ZONE",Offset(1),7),new("LIT-DTO7-1234567",nameof(LiteralRow.DTO7),"DATETIME(7) WITH TIME ZONE",Offset(1234567),7)]);
        foreach(var bare in new[]{(Id:"timestamp",Property:nameof(LiteralRow.BareTimestamp),Store:"TIMESTAMP",Tz:false),(Id:"datetime",Property:nameof(LiteralRow.BareDatetime),Store:"DATETIME",Tz:false),(Id:"tz",Property:nameof(LiteralRow.BareTz),Store:"DATETIME WITH TIME ZONE",Tz:true)})
        {cases.Add(new("LIT-BARE-"+bare.Id+"-micro",bare.Property,bare.Store,bare.Tz?(object)Offset(1234560):Date(1234560),6,Bare:true));cases.Add(new("LIT-BARE-"+bare.Id+"-tick1",bare.Property,bare.Store,bare.Tz?(object)Offset(1):Date(1),6,true,true));}
        return cases;
    }
}
public sealed class LiteralContext(string connectionString):DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)=>options.UseDameng(connectionString);
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<LiteralRow>().HasKey(x=>x.Id);model.Entity<LiteralRow>().Property(x=>x.Id).ValueGeneratedNever();
        model.Entity<LiteralRow>().Property(x=>x.DT6).HasPrecision(6);model.Entity<LiteralRow>().Property(x=>x.DTO6).HasColumnType("DATETIME(6) WITH TIME ZONE");
        model.Entity<LiteralRow>().Property(x=>x.BareTimestamp).HasColumnType("TIMESTAMP");model.Entity<LiteralRow>().Property(x=>x.BareDatetime).HasColumnType("DATETIME");model.Entity<LiteralRow>().Property(x=>x.BareTz).HasColumnType("DATETIME WITH TIME ZONE");
    }
}
public sealed class LiteralRow
{public int Id{get;set;}public DateTime DT7{get;set;}public DateTimeOffset DTO7{get;set;}public DateTime DT6{get;set;}public DateTimeOffset DTO6{get;set;}public DateTime BareTimestamp{get;set;}public DateTime BareDatetime{get;set;}public DateTimeOffset BareTz{get;set;}}
