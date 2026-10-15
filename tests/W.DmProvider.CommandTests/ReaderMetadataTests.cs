using System.Data;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class ReaderMetadataTests
{
    [Theory]
    [InlineData("SELECT 1 FROM DUAL")]
    [InlineData("/* SELECT ; */ SELECT ';' FROM DUAL; -- trailing comment")]
    [InlineData("SELECT q'[; :ignored]' FROM DUAL")]
    [InlineData("SELECT \"semi;colon\" FROM \"Mixed Table\"")]
    [InlineData("SELECT COALESCE((SELECT 1 FROM DUAL), 0) FROM DUAL")]
    public void DescribeAcceptsOnlyOneQueryWithoutInterpretingQuotedContents(string sql)
    {
        using var plan = Plan(sql);
        DmReaderMetadata.Validate(plan);
    }

    [Theory]
    [InlineData("SELECT 1 FROM DUAL; DELETE FROM T")]
    [InlineData("SELECT 1 FROM DUAL; SELECT 2 FROM DUAL")]
    [InlineData("INSERT INTO T VALUES(1)")]
    [InlineData("BEGIN SELECT 1 FROM DUAL; END")]
    [InlineData("WITH Q AS (SELECT 1 FROM DUAL) SELECT * FROM Q")]
    [InlineData("SELECT A INTO T2 FROM T")]
    [InlineData("SELECT (1 FROM DUAL")]
    [InlineData("SELECT 1) FROM DUAL")]
    [InlineData("SELECT (1; DELETE FROM T) FROM DUAL")]
    public void DescribeRejectsUnsafeOrUnclassifiedStatements(string sql)
    {
        using var plan = Plan(sql);
        Assert.Throws<NotSupportedException>(() => DmReaderMetadata.Validate(plan));
    }

    [Fact]
    public void DescribeRejectsProceduresAndUnboundMarkers()
    {
        using var procedure = Plan("P", type: CommandType.StoredProcedure);
        Assert.Throws<NotSupportedException>(() => DmReaderMetadata.Validate(procedure));
        using var unbound = Plan("SELECT :value FROM DUAL");
        Assert.Throws<InvalidOperationException>(() => DmReaderMetadata.Validate(unbound));
        using var bound = Plan("SELECT :value, :value FROM DUAL", new DmParameter("value", 3));
        DmReaderMetadata.Validate(bound);
    }

    [Theory]
    [InlineData("SELECT ID, SECOND FROM S.T")]
    [InlineData("SELECT t.ID AS alias, t.SECOND FROM S.T t WHERE t.ID=:id ORDER BY t.SECOND DESC")]
    [InlineData("SELECT * FROM S.T WHERE ID >= 1 AND SECOND IS NOT NULL;")]
    [InlineData("SELECT * FROM S.T WHERE ID='JOIN ; UNION' /* FROM Q */")]
    [InlineData("SELECT * FROM S.T WHERE ID=q'[; JOIN Q]'")]
    public void SimpleSingleTableQueriesHaveReliableSource(string sql)
    {
        Assert.True(DmReaderMetadata.TryGetSource([Column("ID"), Column("SECOND")], sql, out var schema, out var table));
        Assert.Equal("S", schema);
        Assert.Equal("T", table);
    }

    [Theory]
    [InlineData("SELECT t.ID FROM S.T t JOIN S.OTHER o ON o.ID=t.ID")]
    [InlineData("SELECT t.ID FROM S.T t, S.OTHER o")]
    [InlineData("SELECT ID FROM S.T UNION ALL SELECT ID FROM S.T")]
    [InlineData("SELECT DISTINCT ID FROM S.T")]
    [InlineData("SELECT MAX(ID) FROM S.T")]
    [InlineData("SELECT ID+1 FROM S.T")]
    [InlineData("SELECT ID FROM (SELECT ID FROM S.T) T")]
    [InlineData("SELECT ID FROM S.T WHERE EXISTS (SELECT 1 FROM S.OTHER)")]
    [InlineData("SELECT ID FROM S.T CONNECT BY PRIOR ID=SECOND")]
    [InlineData("SELECT ID FROM S.T; SELECT ID FROM S.T")]
    [InlineData("SELECT ID FROM S.OTHER")]
    public void ComplexQueriesCannotAcquireKeyClaimsFromCoincidentalColumnOrigin(string sql)
    {
        Assert.False(DmReaderMetadata.TryGetSource([Column("ID")], sql, out _, out _));
    }

    [Fact]
    public void QuotedSourceNamesRetainCaseAndEscapes()
    {
        var column = Column("Id", "Mixed Schema", "T\"_%'");
        Assert.True(DmReaderMetadata.TryGetSource([column], "SELECT \"Id\" FROM \"Mixed Schema\".\"T\"\"_%'\"", out var schema, out var table));
        Assert.Equal("Mixed Schema", schema);
        Assert.Equal("T\"_%'", table);
        Assert.False(DmReaderMetadata.TryGetSource([column], "SELECT \"Id\" FROM \"mixed schema\".\"T\"\"_%'\"", out _, out _));
    }

    [Fact]
    public void MissingOrMixedOriginsRemainUnknown()
    {
        Assert.False(DmReaderMetadata.TryGetSource([Column("")], "SELECT ID FROM S.T", out _, out _));
        Assert.False(DmReaderMetadata.TryGetSource([Column("ID", "", "T")], "SELECT ID FROM S.T", out _, out _));
        Assert.False(DmReaderMetadata.TryGetSource([Column("ID"), Column("SECOND", "S", "OTHER")], "SELECT * FROM S.T", out _, out _));
    }

    [Fact]
    public void CompositeConstraintsKeepMembershipWithoutInventingIndividualUniquenessOrNullability()
    {
        var nullableUnique = Column("NULLABLE_UNIQUE"); nullableUnique.nullable = true;
        DmColumn[] columns = [Column("PK1"), Column("PK2"), Column("U1"), Column("U2"), nullableUnique, Column("OTHER"), Column("")];
        var result = DmReaderMetadata.Project(columns,
        [
            new("PK", "P", "PK2", 2), new("PK", "P", "PK1", 1),
            new("U_COMPOSITE", "U", "U1", 1), new("U_COMPOSITE", "U", "U2", 2),
            new("U_SINGLE", "U", "NULLABLE_UNIQUE", 1)
        ], "TABLE");
        Assert.Equal(new(true, false), result[0]); Assert.Equal(new(true, false), result[1]);
        Assert.Equal(new(false, false), result[2]); Assert.Equal(new(false, false), result[3]);
        Assert.Equal(new(false, true), result[4]); Assert.True(nullableUnique.GetNullable());
        Assert.Equal(new(false, false), result[5]); Assert.Equal(default, result[6]);
    }

    [Fact]
    public void SinglePrimaryKeyIsUniqueAndCatalogNamesAreCaseSensitive()
    {
        var result = DmReaderMetadata.Project([Column("Id"), Column("ID")], [new("PK", "P", "Id", 1)], "TABLE");
        Assert.Equal(new(true, true), result[0]);
        Assert.Equal(new(false, false), result[1]);
    }

    [Fact]
    public void IncompleteCatalogMembershipCannotBecomeSingleColumnUnique()
    {
        Assert.Throws<InvalidDataException>(() => DmReaderMetadata.Project([Column("ID")], [new("U", "U", "ID", 2)], "TABLE"));
        Assert.Throws<InvalidDataException>(() => DmReaderMetadata.Project([Column("ID")], [new("U", "U", "ID", 1), new("U", "U", "OTHER", 1)], "TABLE"));
    }

    [Fact]
    public async Task ComplexKeyInfoNeedsNoCatalogConnectionAndStillHonorsPreCancellation()
    {
        var columns = new[] { Column("ID") };
        const string query = "SELECT ID FROM S.T UNION ALL SELECT ID FROM S.T";
        Assert.Equal(default, Assert.Single(DmReaderMetadata.Load(null!, null!, columns, query)));
        Assert.Equal(default, Assert.Single(await DmReaderMetadata.LoadAsync(null!, null!, columns, query, default)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DmReaderMetadata.LoadAsync(null!, null!, columns, query, cancellation.Token));
    }

    [Theory]
    [InlineData("VIEW")]
    [InlineData("SYNONYM")]
    [InlineData(null)]
    public void NonTableSourcesNeverAcquireKnownKeyClaims(string? sourceObjectType)
    {
        DmColumn[] columns = [Column("ID")];
        Assert.True(DmReaderMetadata.TryGetSource(columns, "SELECT ID FROM S.T", out _, out _));
        Assert.Equal(default, Assert.Single(DmReaderMetadata.Project(columns, [], sourceObjectType!)));
        Assert.Equal(default, Assert.Single(DmReaderMetadata.Project(columns, [new("PK", "P", "ID", 1)], sourceObjectType!)));
    }

    [Fact]
    public void ConfirmedBaseTableWithoutConstraintsHasKnownNegativeKeyMetadata()
    {
        Assert.Equal(new(false, false), Assert.Single(DmReaderMetadata.Project([Column("ID")], [], "TABLE")));
    }

    private static DmCommandPlan Plan(string sql, DmParameter? parameter = null, CommandType type = CommandType.Text)
    {
        var parameters = new DmParameterCollection();
        if (parameter != null) parameters.Add(parameter);
        return DmCommandPlan.Capture(sql, type, 30, null!, null!, parameters);
    }

    private static DmColumn Column(string name, string schema = "S", string table = "T")
    {
        var column = (DmColumn)RuntimeHelpers.GetUninitializedObject(typeof(DmColumn));
        column.name = column.baseName = name; column.schemaName = schema; column.tableName = table;
        return column;
    }
}
