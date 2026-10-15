using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Execution;
using Xunit;

namespace W.DmProvider.CommandTests;

public sealed class ArrayBindingAndBatchTests
{
    private static DmCommandPlan Capture(string sql, int count, params DmParameter[] parameters)
    {
        using var command = new DmCommand(sql);
        foreach (DmParameter parameter in parameters) command.Parameters.Add(parameter);
        return DmCommandPlan.Capture(sql, CommandType.Text, 30, null, null, command.Parameters, count);
    }

    [Fact]
    public void ArrayPlanSnapshotsRowsAndSharesInferredTypeWithNullRows()
    {
        int[] ids = [1, 2, 3];
        byte[][] blobs = [[1], [2, 2], [3, 3, 3]];
        object[] names = [DBNull.Value, "b", null!];
        using DmCommandPlan plan = Capture("INSERT INTO T VALUES (?, ?, ?)", 3,
            new DmParameter("ID", ids), new DmParameter("B", blobs), new DmParameter("N", names));
        ids[0] = 99;
        blobs[1][0] = 99;
        Assert.Equal(3, plan.ArrayBindCount);
        DmParameter idTemplate = plan.Parameters.do_GetParameter(0);
        DmParameter blobTemplate = plan.Parameters.do_GetParameter(1);
        DmParameter nameTemplate = plan.Parameters.do_GetParameter(2);
        Assert.Same(idTemplate, plan.RowParameters(idTemplate)(0));
        Assert.Equal(1, plan.RowParameters(idTemplate)(0).Value);
        Assert.Equal(new byte[] { 2, 2 }, (byte[])plan.RowParameters(blobTemplate)(1).Value);
        Assert.Equal(DbType.Int32, plan.RowParameters(idTemplate)(2).DbType);
        // NULL rows adopt the type of the non-NULL row instead of staying unresolved.
        Assert.All(Enumerable.Range(0, 3), row => Assert.Equal(DbType.String, plan.RowParameters(nameTemplate)(row).DbType));
        Assert.Equal(DBNull.Value, plan.RowParameters(nameTemplate)(2).Value);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("scalar")]
    [InlineData("rank")]
    [InlineData("mixed")]
    [InlineData("empty_collection")]
    public void ArrayPlanRejectsShapesItCannotBindExactly(string shape)
    {
        DmParameter[] parameters = shape switch
        {
            "length" => [new DmParameter("A", new[] { 1, 2 })],
            "scalar" => [new DmParameter("A", (object)1)],
            "rank" => [new DmParameter("A", new int[3, 1])],
            "mixed" => [new DmParameter("A", new object[] { 1, 2, "3" })],
            _ => []
        };
        string sql = parameters.Length == 0 ? "INSERT INTO T VALUES (1)" : "INSERT INTO T VALUES (?)";
        Assert.Throws<InvalidOperationException>(() => Capture(sql, 3, parameters));
    }

    [Fact]
    public void ArrayPlanRejectsStreamsAndProviderObjects()
    {
        Assert.Throws<NotSupportedException>(() => Capture("INSERT INTO T VALUES (?)", 1,
            new DmParameter { ParameterName = "B", DmSqlType = DmDbType.Blob, Value = new object[] { new MemoryStream() } }));
        Assert.Throws<NotSupportedException>(() => Capture("INSERT INTO T VALUES (?)", 1,
            new DmParameter("B", new object[] { new object() })));
    }

    [Fact]
    public void ArrayBindingRejectsReaderScalarAndNegativeCountsBeforeConnectionUse()
    {
        using var command = new DmCommand("INSERT INTO T VALUES (?)") { ArrayBindCount = 2 };
        command.Parameters.Add(new DmParameter("A", new[] { 1, 2 }));
        Assert.Throws<NotSupportedException>(() => command.ExecuteReader());
        Assert.Throws<NotSupportedException>(() => command.ExecuteScalar());
        Assert.ThrowsAsync<NotSupportedException>(() => command.ExecuteScalarAsync()).GetAwaiter().GetResult();
        command.ArrayBindCount = -1;
        Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery());
        // Without ArrayBindCount an array value is still not a row set.
        command.ArrayBindCount = 0;
        Assert.Throws<NotSupportedException>(() => command.ExecuteNonQuery());
    }

    private static DmParameterInternal Described(byte typeFlag, int cType, int precision, int scale, params DmParamValue[] rows)
    {
        var parameter = (DmParameterInternal)RuntimeHelpers.GetUninitializedObject(typeof(DmParameterInternal));
        typeof(DmParameterInternal).GetField("m_Val", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(parameter, rows.ToList());
        parameter.SetTypeFlag(typeFlag);
        parameter.SetCType(cType);
        parameter.SetPrecision(precision);
        parameter.SetScale(scale);
        return parameter;
    }

    private static DmParamValue Row(int sqlType, int precision, int scale, byte[]? value)
    {
        var row = new DmParamValue();
        row.SetInValue(value);
        row.SetSqlType(sqlType);
        row.SetPrec(precision);
        row.SetScale(scale);
        return row;
    }

    [Fact]
    public void ArrayDescriptorRequiresOneEncodingPerAdvisoryParameter()
    {
        // Advisory decimal: two scales cannot share the single request descriptor.
        var mixed = Described(2, 9, 38, 0, Row(9, 38, 1, [1]), Row(9, 38, 2, [2]));
        Assert.Throws<InvalidOperationException>(() => DmCommand.ValidateArrayDescriptor(mixed, "D", 8000));
        // NULL rows never carry value attributes.
        var withNull = Described(2, 9, 38, 0, Row(9, 38, 2, [1]), Row(9, 38, 0, null), Row(9, 38, 2, [2]));
        DmCommand.ValidateArrayDescriptor(withNull, "D", 8000);
        // An advisory NULL first row sends the server description; later rows must match it.
        var nullFirst = Described(2, 9, 38, 0, Row(9, 38, 0, null), Row(9, 38, 2, [2]));
        Assert.Throws<InvalidOperationException>(() => DmCommand.ValidateArrayDescriptor(nullFirst, "D", 8000));
        // Fixed server metadata encodes every row against the column.
        var fixedColumn = Described(1, 9, 18, 4, Row(9, 38, 1, [1]), Row(9, 38, 2, [2]));
        DmCommand.ValidateArrayDescriptor(fixedColumn, "D", 8000);
    }

    [Fact]
    public void ArrayDescriptorRejectsOutOfRowLobValues()
    {
        var clob = Described(1, 19, int.MaxValue, 0, Row(19, 0, 0, [1]), Row(19, 0, 0, new byte[8000]));
        Assert.Throws<NotSupportedException>(() => DmCommand.ValidateArrayDescriptor(clob, "C", 8000));
        var inRow = Described(1, 12, int.MaxValue, 0, Row(12, 0, 0, new byte[7999]), Row(12, 0, 0, null));
        DmCommand.ValidateArrayDescriptor(inRow, "B", 8000);
    }

    [Fact]
    public void MergeCountIsAProvenDmlCount()
    {
        var cursor = new DmResultCursor();
        var merge = new DmInfo();
        merge.SetRetStmtType(DmConst.RET_DML_MERGE);
        merge.SetRecordsAffected(2);
        merge.SetHasResultSet(false);
        cursor.Observe(merge);
        Assert.Equal(2, cursor.RecordsAffected);
    }

    [Fact]
    public void ProviderEntryPointsCreateBatchTypes()
    {
        DbProviderFactory factory = DmClientFactory.Instance;
        Assert.True(factory.CanCreateBatch);
        Assert.IsType<DmBatch>(factory.CreateBatch());
        DbBatchCommand command = factory.CreateBatchCommand();
        Assert.IsType<DmBatchCommand>(command);
        Assert.True(command.CanCreateParameter);
        Assert.IsType<DmParameter>(command.CreateParameter());
        using var connection = new DmConnection();
        Assert.True(connection.CanCreateBatch);
        DbBatch batch = ((DbConnection)connection).CreateBatch();
        Assert.Same(connection, batch.Connection);
        Assert.Equal(-1, command.RecordsAffected);
    }

    [Fact]
    public void BatchCollectionAcceptsOnlyProviderCommands()
    {
        var batch = new DmBatch();
        Assert.Throws<ArgumentNullException>(() => batch.BatchCommands.Add(null!));
        Assert.Throws<ArgumentException>(() => batch.BatchCommands.Add(new ForeignCommand()));
        var first = new DmBatchCommand("SELECT 1 FROM DUAL");
        batch.BatchCommands.Add(first);
        batch.BatchCommands.Insert(0, new DmBatchCommand("SELECT 0 FROM DUAL"));
        Assert.Equal(1, batch.BatchCommands.IndexOf(first));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.CommandType = (CommandType)99);
        Assert.True(batch.BatchCommands.Remove(first));
        Assert.Single(batch.BatchCommands);
    }

    [Fact]
    public void BatchValidatesEveryCommandBeforeUsingTheConnection()
    {
        using var batch = new DmBatch();
        Assert.Throws<InvalidOperationException>(() => batch.ExecuteNonQuery()); // no commands
        batch.BatchCommands.Add(new DmBatchCommand("INSERT INTO T VALUES (1)"));
        Assert.Throws<InvalidOperationException>(() => batch.ExecuteNonQuery()); // no connection
        batch.BatchCommands.Add(new DmBatchCommand("P") { CommandType = CommandType.StoredProcedure });
        Assert.Throws<NotSupportedException>(() => batch.Prepare());
        batch.BatchCommands.RemoveAt(1);
        var markers = new DmBatchCommand("INSERT INTO T VALUES (? + ?)");
        markers.Parameters.Add(new DmParameter("A", 1));
        batch.BatchCommands.Add(markers);
        Assert.Throws<InvalidOperationException>(() => batch.Prepare());
        markers.Parameters.Add(new DmParameter("B", 2));
        batch.Prepare();
        // A failed validation leaves the batch and its commands mutable.
        batch.Timeout = 5;
        markers.CommandText = "INSERT INTO T VALUES (?, ?)";
        Assert.Equal(-1, markers.RecordsAffected);
        Assert.Throws<NotSupportedException>(() => batch.ExecuteReader(CommandBehavior.SingleRow));
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Timeout = -1);
    }

    [Fact]
    public void BatchFailureIdentifiesItsCommand()
    {
        var command = new DmBatchCommand("SELECT 1 FROM DUAL");
        var error = new DmException("failed");
        error.SetBatchCommand(command);
        error.SetBatchCommand(new DmBatchCommand("SELECT 2 FROM DUAL")); // first attribution wins
        Assert.Same(command, ((DbException)error).BatchCommand);
    }

    private sealed class ForeignCommand : DbBatchCommand
    {
        public override string CommandText { get; set; } = string.Empty;
        public override CommandType CommandType { get; set; }
        public override int RecordsAffected => 0;
        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
    }
}
