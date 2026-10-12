using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TP07.TemporalDesign.Migrations;

[DbContext(typeof(DesignContext))]
[Migration("20261009000200_Upgrade7")]
public sealed class Upgrade7:Migration
{
    protected override void Up(MigrationBuilder migration)=>Alter(migration,7,6);
    protected override void Down(MigrationBuilder migration)
    {
        var spec=Program.Current.Spec!;
        if(!GuardedMigrationCases.GuardControlsAccepted(spec.Kind))
            throw new InvalidOperationException("TP07_DOWN_CONTROLS_NOT_ACCEPTED");
        migration.Sql(MigrationGuardSql.ForOwnedTable(spec.Table,Program.Current.Tables),suppressTransaction:true);
        Alter(migration,6,7);
    }
    private static void Alter(MigrationBuilder migration,int scale,int oldScale)
    {
        var spec=Program.Current.Spec!;
        if(spec.Kind=="dt")migration.AlterColumn<DateTime>("V",spec.Table,spec.StoreType(scale),schema:TP07.Runtime.CandidateGuard.User,
            nullable:true,defaultValueSql:spec.DefaultValueSql,oldClrType:typeof(DateTime),oldType:spec.StoreType(oldScale),oldNullable:true,oldDefaultValueSql:spec.DefaultValueSql);
        else migration.AlterColumn<DateTimeOffset>("V",spec.Table,spec.StoreType(scale),schema:TP07.Runtime.CandidateGuard.User,
            nullable:true,defaultValueSql:spec.DefaultValueSql,oldClrType:typeof(DateTimeOffset),oldType:spec.StoreType(oldScale),oldNullable:true,oldDefaultValueSql:spec.DefaultValueSql);
    }
    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder model)
        =>DesignContext.ConfigureModel(model,Program.Current.Spec!,7);
}
