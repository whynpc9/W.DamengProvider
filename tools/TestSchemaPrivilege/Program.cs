using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using W.Dm;
using W.Dm.Config;

// Temporary, exactly scoped CREATE SCHEMA privilege for WDM_PROVIDER_TEST.
//   grant   (admin env)  : GRANT CREATE SCHEMA TO "WDM_PROVIDER_TEST"
//   revoke  (admin env)  : REVOKE CREATE SCHEMA FROM "WDM_PROVIDER_TEST"
//   inspect (test env)   : fresh TEST connection reads its own privilege and EF10_ schemas
// Output is numbers/booleans only; no connection data or diagnostic text.
const string TestUser = "WDM_PROVIDER_TEST";
var result = new Dictionary<string, object?> { ["action"] = args.FirstOrDefault() };
int exit = 1;
try
{
    if (args.Length != 1 || args[0] is not ("grant" or "revoke" or "inspect")) { result["code"] = "arguments_rejected"; return Finish(64); }
    if (args[0] == "inspect")
    {
        using var test = Open("DAMENG_TEST_CONNECTION_STRING");
        result["identity_test"] = Scalar(test, "SELECT USER FROM DUAL") == TestUser;
        if (!(bool)result["identity_test"]!) return Finish(2);
        result["create_schema_privileges"] = Count(test, "SELECT COUNT(*) FROM USER_SYS_PRIVS WHERE PRIVILEGE='CREATE SCHEMA'");
        result["admin_option_rows"] = Count(test, "SELECT COUNT(*) FROM USER_SYS_PRIVS WHERE PRIVILEGE='CREATE SCHEMA' AND ADMIN_OPTION<>'NO' AND ADMIN_OPTION<>'N'");
        result["owned_ef10_schemas"] = Count(test, "SELECT COUNT(*) FROM SYS.SYSOBJECTS S WHERE S.TYPE$='SCH' AND S.PID=UID() AND S.NAME LIKE 'EF10\\_%' ESCAPE '\\'");
        result["code"] = "inspected";
        return Finish(0);
    }
    using var admin = Open("DAMENG_ADMIN_CONNECTION_STRING");
    result["identity_sa"] = Scalar(admin, "SELECT USER FROM DUAL") == "SYSDBA" || Scalar(admin, "SELECT USER FROM DUAL") == "SA";
    result["before"] = Count(admin, $"SELECT COUNT(*) FROM DBA_SYS_PRIVS WHERE GRANTEE='{TestUser}' AND PRIVILEGE='CREATE SCHEMA'");
    using (var command = admin.CreateCommand())
    {
        command.CommandText = args[0] == "grant" ? $"GRANT CREATE SCHEMA TO \"{TestUser}\"" : $"REVOKE CREATE SCHEMA FROM \"{TestUser}\"";
        command.ExecuteNonQuery();
        result["acknowledged"] = true;
    }
    result["after"] = Count(admin, $"SELECT COUNT(*) FROM DBA_SYS_PRIVS WHERE GRANTEE='{TestUser}' AND PRIVILEGE='CREATE SCHEMA'");
    result["code"] = "done";
    exit = 0;
}
catch (Exception error)
{
    result["code"] = "failed";
    result["error_type"] = error.GetType().Name;
    result["server_number"] = (error as DmException)?.Number;
}
return Finish(exit);

int Finish(int code) { Console.WriteLine(JsonSerializer.Serialize(result)); return code; }
DmConnection Open(string variable)
{
    var builder = new DmConnectionStringBuilder(Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException("connection variable missing"))
    { ConnPooling = false, TransportSecurity = DmTransportSecurity.PlaintextAllowed };
    var connection = new DmConnection(builder.ConnectionString);
    connection.Open();
    return connection;
}
string? Scalar(DbConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
}
long Count(DbConnection connection, string sql) => long.Parse(Scalar(connection, sql)!, CultureInfo.InvariantCulture);
