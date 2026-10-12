using Microsoft.EntityFrameworkCore.Design;
using TP07.Runtime;

namespace TP07.TemporalDesign;

public sealed class DesignContextFactory : IDesignTimeDbContextFactory<DesignContext>
{
    public DesignContext CreateDbContext(string[] args)
    {
        // CLI never receives a connection string. The owning coordinator supplies a public plan path.
        string plan=Environment.GetEnvironmentVariable("TP07_DESIGN_PLAN")??"";
        var value=SafeEvidence.Load(plan);
        var spec=new DesignSpec(value.GetProperty("kind").GetString()!,value.GetProperty("table").GetString()!,
            value.GetProperty("history").GetString()!,value.GetProperty("primary_key").GetString()!,value.GetProperty("index").GetString()!,
            value.GetProperty("check").GetString()!,value.GetProperty("scale").GetInt32(),value.GetProperty("default_value_sql").GetString()!);
        Program.Require(spec.Kind is "dt" or "dto"&&spec.Scale is 6 or 7,DesignCode.SpecMissing);
        MigrationGuardSql.RequireIdentifier(spec.Table);MigrationGuardSql.RequireIdentifier(spec.History);
        var guard=CandidateGuard.VerifyProcessEnvironment(typeof(Program).Assembly);
        string ledger=value.GetProperty("ownership_ledger").GetString()!;
        Program.Require(SafeEvidence.HashFile(ledger)==value.GetProperty("ownership_ledger_sha256").GetString(),DesignCode.SpecMissing);
        var owned=OwnedObjects.LoadForCleanup(guard,ledger);
        var table=owned.Entries.Single(entry=>entry.Table==spec.Table);
        Program.Require(table.Shape==(spec.Kind=="dt"?"GuardedDt":"GuardedDto")&&table.PrimaryKey==spec.PrimaryKey&&table.Index==spec.Index&&table.Check==spec.Check
            &&owned.Entries.Single(entry=>entry.Table==spec.History).Shape=="History",DesignCode.OwnedSqlScope);
        Program.BindDesignFactory(spec,owned);
        Program.Require(spec.DefaultValueSql==GuardedMigrationCases.GenerateDefault(spec.Kind),DesignCode.SpecMissing);
        return Program.Context();
    }
}
