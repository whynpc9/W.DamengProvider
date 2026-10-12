using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
namespace TP07.Runtime;
internal sealed class ProbeRow {public int Id{get;set;} public DateTime D{get;set;} public DateTimeOffset Z{get;set;} public string N{get;set;}="";}
internal sealed class ProbeContext(OwnedObjects.Entry entry,CandidateGuard? guard,TransactionEvidence evidence,DbConnection? offlineConnection=null):DbContext
{
    internal OwnedObjects.Entry OwnedEntry=>entry;
    internal DbSet<ProbeRow> Rows=>Set<ProbeRow>();
    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        if(offlineConnection is null)builder.UseDameng(guard!.BuildConnectionString());else builder.UseDameng(offlineConnection);
        builder.ReplaceService<IModelCacheKeyFactory,ProbeModelCacheKeyFactory>().EnableSensitiveDataLogging(false).EnableDetailedErrors(false).AddInterceptors(evidence,evidence.Commands);
    }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        var row=builder.Entity<ProbeRow>();row.ToTable(entry.Table);row.HasKey(x=>x.Id).HasName(entry.PrimaryKey);
        row.Property(x=>x.Id).HasColumnName("ID").ValueGeneratedNever();
        row.Property(x=>x.D).HasColumnName("D").HasPrecision(entry.Scale).IsRequired();
        row.Property(x=>x.Z).HasColumnName("Z").HasPrecision(entry.Scale).IsRequired();
        row.Property(x=>x.N).HasColumnName("N").HasColumnType("VARCHAR(32)").HasMaxLength(32).IsUnicode(false).IsRequired();
    }
    internal string CreateSql()
    {
        var model=this.GetService<IDesignTimeModel>().Model;
        var operations=this.GetService<IMigrationsModelDiffer>().GetDifferences(null,model.GetRelationalModel());
        SafeEvidence.Require(operations.Count==1&&operations[0] is CreateTableOperation table&&table.Name==entry.Table&&table.Schema is null&&table.ForeignKeys.Count==0&&table.CheckConstraints.Count==0&&table.PrimaryKey?.Name==entry.PrimaryKey&&table.Columns.Count==4,EvidenceCode.SchemaMismatch);
        var commands=this.GetService<IMigrationsSqlGenerator>().Generate(operations,model);
        SafeEvidence.Require(commands.Count==1,EvidenceCode.SchemaMismatch);return commands[0].CommandText;
    }
}
internal sealed class ProbeModelCacheKeyFactory:IModelCacheKeyFactory
{public object Create(DbContext context,bool designTime)=>context is ProbeContext p?(context.GetType(),p.OwnedEntry.Table,p.OwnedEntry.PrimaryKey,p.OwnedEntry.Scale,designTime):(object)(context.GetType(),designTime);}
