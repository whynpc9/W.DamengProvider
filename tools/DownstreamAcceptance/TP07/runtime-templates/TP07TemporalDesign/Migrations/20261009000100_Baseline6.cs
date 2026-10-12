using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TP07.TemporalDesign.Migrations;

[DbContext(typeof(DesignContext))]
[Migration("20261009000100_Baseline6")]
public sealed class Baseline6:Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        var spec=Program.Current.Spec!;
        if(spec.Kind=="dt")
            migration.CreateTable(spec.Table,columns=>new
            {
                ID=columns.Column<int>(type:"INT",nullable:false),
                V=columns.Column<DateTime>(type:spec.StoreType(6),nullable:true,defaultValueSql:spec.DefaultValueSql)
            },schema:TP07.Runtime.CandidateGuard.User,constraints:table=>
            {table.PrimaryKey(spec.PrimaryKey,e=>e.ID);table.CheckConstraint(spec.Check,"\"ID\" > 0");});
        else
            migration.CreateTable(spec.Table,columns=>new
            {
                ID=columns.Column<int>(type:"INT",nullable:false),
                V=columns.Column<DateTimeOffset>(type:spec.StoreType(6),nullable:true,defaultValueSql:spec.DefaultValueSql)
            },schema:TP07.Runtime.CandidateGuard.User,constraints:table=>
            {table.PrimaryKey(spec.PrimaryKey,e=>e.ID);table.CheckConstraint(spec.Check,"\"ID\" > 0");});
        migration.CreateIndex(spec.Index,spec.Table,"V",schema:TP07.Runtime.CandidateGuard.User);
    }
    protected override void Down(MigrationBuilder migration)
        =>throw new InvalidOperationException("TP07_BASELINE_DROP_NOT_A_DOWN_ROUTE");
    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder model)
        =>DesignContext.ConfigureModel(model,Program.Current.Spec!,6);
}
