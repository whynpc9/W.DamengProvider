using System.Data;
using W.Dm;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace W.DmProvider.SchemaTests;

public sealed class SchemaContractTests
{
    [Fact]
    public void CollectionsAndRestrictionsDescribeExactlyTheDispatchContract()
    {
        DataTable collections = DmSchema.GetCollections();
        DataTable restrictions = DmSchema.GetRestrictions();
        Assert.Equal(16, collections.Rows.Count);
        foreach (DataRow row in collections.Rows)
        {
            string name = (string)row["CollectionName"];
            var request = DmSchema.CreateRequest(name.ToLowerInvariant(), null!);
            Assert.Equal(name, request.Definition.Name);
            DataRow[] entries = restrictions.Select($"CollectionName='{name}'");
            Assert.Equal((int)row["NumberOfRestrictions"], entries.Length);
            Assert.Equal(Enumerable.Range(1, entries.Length), entries.Select(entry => (int)entry["RestrictionNumber"]));
            Assert.All(entries, entry => Assert.Equal(DBNull.Value, entry["RestrictionDefault"]));
            Assert.Throws<ArgumentException>(() => DmSchema.CreateRequest(name, new string[entries.Length + 1]));
            if (request.Definition.Sql is not null)
            {
                Assert.NotEmpty(request.Definition.Columns);
                Assert.Equal(entries.Length, request.Definition.RestrictionExpressions.Length);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mEtAdAtAcOlLeCtIoNs")]
    public void EmptyAndCaseInsensitiveCollectionNamesUseTheDefault(string? name)
        => Assert.Equal("MetaDataCollections", DmSchema.CreateRequest(name!, null!).Definition.Name);

    [Fact]
    public async Task InvalidArgumentsAreRejectedBeforeConnectionOwnershipOrNetworkAccess()
    {
        await using var connection = new DmConnection();
        Assert.Throws<ArgumentException>(() => connection.GetSchema("nonexistent"));
        Assert.Throws<ArgumentException>(() => connection.GetSchema("Tables", new string[4]));
        await Assert.ThrowsAsync<ArgumentException>(() => connection.GetSchemaAsync("nonexistent"));
        await Assert.ThrowsAsync<ArgumentException>(() => connection.GetSchemaAsync("Tables", new string[4]));
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Null(connection.Session);
    }

    [Theory]
    [InlineData("MiXed")]
    [InlineData("O'Brien%_table")]
    [InlineData("`literal`")]
    [InlineData("\"quoted\"")]
    [InlineData("")]
    public void RestrictionValuesAreCopiedBoundAndNeverNormalized(string value)
    {
        string[] caller = { "Owner", value, null! };
        var request = DmSchema.CreateRequest("Tables", caller);
        caller[1] = "changed";
        Assert.Equal(value, request.Restrictions[1]);
        Assert.Contains("OWNER = :r0", request.CommandText);
        Assert.Contains("OBJECT_NAME = :r1", request.CommandText);
        Assert.DoesNotContain(" = :r2", request.CommandText);
        Assert.DoesNotContain(" LIKE ", request.CommandText);
        if (value.Length != 0) Assert.DoesNotContain(value, request.CommandText);
    }

    [Fact]
    public void EveryRestrictionUsesAParameterAndEmptyValuesAreNotUnrestricted()
    {
        foreach (var definition in DmSchema.Definitions.Where(d => d.RestrictionNames.Length > 0))
        {
            var values = Enumerable.Repeat("", definition.RestrictionNames.Length).ToArray();
            var request = DmSchema.CreateRequest(definition.Name, values);
            for (int i = 0; i < values.Length; i++) Assert.Contains($" = :r{i}", request.CommandText);
            Assert.DoesNotContain("CREATE TABLE", request.CommandText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("EXECUTE IMMEDIATE", request.CommandText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DatabasesIsExplicitlyASchemasAliasAndKeyDiscoveryIsUnrestrictedByDefault()
    {
        var schemas = DmSchema.CreateRequest("Schemas", new[] { "MiXeD" });
        var databases = DmSchema.CreateRequest("Databases", new[] { "MiXeD" });
        Assert.Equal(schemas.CommandText, databases.CommandText);
        Assert.Equal("Schemas", databases.Definition.CreateTable().TableName);
        Assert.NotNull(DmSchema.CreateRequest("PrimaryKeys", null!).CommandText);
        Assert.NotNull(DmSchema.CreateRequest("IndexColumns", null!).CommandText);
        var foreign = DmSchema.CreateRequest("ForeignKeys", new[] { "child_schema", "child_table", "fk" });
        Assert.Contains("C.OWNER = :r0", foreign.CommandText);
        Assert.Contains("RCC.POSITION=CC.POSITION", foreign.CommandText);
        Assert.Contains("ORDER BY C.OWNER, C.TABLE_NAME, C.CONSTRAINT_NAME, CC.POSITION", foreign.CommandText);
    }

    [Fact]
    public void ProjectionPreservesUnknownMetadataAndConvertsKnownNumericValues()
    {
        var table = DmSchema.CreateRequest("Columns", null!).Definition.CreateTable();
        DmSchema.AddRow(table, new object[] { "Mixed", "O'Brien%_", "v", (short)3, "DECIMAL", null!,
            DBNull.Value, "Y", DBNull.Value, (short)18, DBNull.Value, DBNull.Value });
        DataRow row = Assert.Single(table.Rows.Cast<DataRow>());
        Assert.Equal("O'Brien%_", row["TABLE_NAME"]);
        Assert.Equal(3, row["COLUMN_ID"]);
        Assert.Equal(18, row["NUMERIC_PRECISION"]);
        Assert.Equal(DBNull.Value, row["COLUMN_SIZE"]);
        Assert.Equal(DBNull.Value, row["COLUMN_SCALE"]);
        Assert.Equal(DBNull.Value, row["DEFAULT_VALUE"]);
        Assert.Equal(DBNull.Value, row["DATETIME_PRECISION"]);
    }
}
