using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace TP07.TemporalDesign;

internal static class MigrationGuardSql
{
    internal const int ScopeError = -20070;
    internal const int NonMicroError = -20071;
    internal static string ForOwnedTable(string table, IReadOnlySet<string> allowlist)
    {
        RequireIdentifier(table);
        if (!allowlist.Contains(table)) throw new InvalidOperationException("TP07_GUARD_OBJECT_NOT_OWNED");
        return $$"""
            DECLARE
                v_user VARCHAR(128);
                v_schema VARCHAR(128);
                v_bad BIGINT;
            BEGIN
                SELECT USER, SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID())
                  INTO v_user, v_schema FROM DUAL;
                IF v_user IS NULL OR v_schema IS NULL
                   OR v_user <> 'WDM_PROVIDER_TEST' OR v_schema <> 'WDM_PROVIDER_TEST' THEN
                    RAISE_APPLICATION_ERROR(-20070, 'TP07_DOWN_SCOPE');
                END IF;
                SELECT COUNT(*) INTO v_bad
                  FROM "WDM_PROVIDER_TEST"."{{table}}"
                 WHERE "V" IS NOT NULL
                   AND (TO_CHAR("V", 'FF9') IS NULL
                        OR LENGTH(TO_CHAR("V", 'FF9')) <> 9
                        OR MOD(TO_NUMBER(TO_CHAR("V", 'FF9')), 1000) <> 0);
                IF v_bad > 0 THEN
                    RAISE_APPLICATION_ERROR(-20071, 'TP07_DOWN_NONMICRO');
                END IF;
            END;
            """;
    }

    internal static void RequireIdentifier(string name)
    {
        if (!Regex.IsMatch(name, "\\ATPL_[A-F0-9]{24}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("TP07_INVALID_OWNED_IDENTIFIER");
    }

    internal static void VerifyCommands(IReadOnlyList<MigrationCommand> commands, string table,
        IReadOnlySet<string> allowlist)
    {
        string guard = ForOwnedTable(table, allowlist);
        if (commands.Count != 2 || !commands[0].TransactionSuppressed
            || commands[0].CommandText.Trim() != guard.Trim())
            throw new InvalidOperationException("TP07_DOWN_GUARD_NOT_FIRST");
        foreach (var command in commands.Skip(1))
            if (!Regex.IsMatch(command.CommandText.Trim(),
                    "\\AALTER\\s+TABLE\\s+\"WDM_PROVIDER_TEST\"\\.\""+Regex.Escape(table)
                    +"\"\\s+MODIFY\\s+\"V\"\\s+(?:TIMESTAMP\\(6\\)|DATETIME\\(6\\)\\s+WITH\\s+TIME\\s+ZONE)(?=\\s|;)",RegexOptions.CultureInvariant)
                || command.CommandText.Count(c=>c==';')!=1||!command.CommandText.TrimEnd().EndsWith(';')
                || command.CommandText.Contains("DROP ", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("TP07_DOWN_COMMAND_SCOPE");
    }
}
