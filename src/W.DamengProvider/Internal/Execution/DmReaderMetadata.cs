using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;

namespace W.Dm.Internal.Execution;

internal readonly record struct DmReaderColumnMetadata(bool? IsKey, bool? IsUnique);
internal readonly record struct DmReaderConstraintColumn(string Constraint, string Kind, string Column, int Position);

/// <summary>Describe validation and conservative, eagerly loaded reader key metadata.</summary>
internal static class DmReaderMetadata
{
    internal static void Validate(DmCommandPlan plan)
    {
        var shapes = DmParameterBinding.ScanTopLevelStatementShapes(plan.Sql);
        if (plan.CommandType != CommandType.Text || shapes.Count != 1 || shapes[0].HasPrefixToken ||
            shapes[0].StartsWithLabel || shapes[0].Head.First != "SELECT")
            throw new NotSupportedException("Reader metadata requires a single SELECT text command.");
        var tokens = Tokenize(plan.Sql);
        int depth = 0, markers = 0;
        for (int index = 0; index < tokens.Count; index++)
        {
            Token token = tokens[index];
            if (token.Text == "(" && token.Kind == TokenKind.Symbol) depth++;
            if (token.Text == ")" && token.Kind == TokenKind.Symbol && --depth < 0)
                throw new NotSupportedException("Reader metadata requires balanced query parentheses.");
            if (token.Text == ";" && token.Kind == TokenKind.Symbol && (depth != 0 || index != tokens.Count - 1))
                throw new NotSupportedException("Reader metadata does not support batches.");
            if (token.Is("INTO")) throw new NotSupportedException("SELECT INTO is not a supported metadata query.");
            if (token.Kind == TokenKind.Marker) markers++;
        }
        if (depth != 0) throw new NotSupportedException("Reader metadata requires balanced query parentheses.");
        if (markers != plan.Binding.MarkerCount)
            throw new InvalidOperationException("SQL marker has no matching parameter.");
    }

    internal static DmReaderColumnMetadata[] Load(DmConnection connection, DmExecutionLease lease,
        DmColumn[] columns, string sql)
    {
        if (!TryGetSource(columns, sql, out string schema, out string table)) return Unknown(columns);
        using var command = CreateCommand(connection, schema, table);
        using var reader = command.ExecuteInternalReader(lease, CommandBehavior.Default);
        var constraints = new List<DmReaderConstraintColumn>();
        while (reader.do_Read()) constraints.Add(ReadConstraint(reader));
        return Project(columns, constraints);
    }

    internal static async Task<DmReaderColumnMetadata[]> LoadAsync(DmConnection connection, DmExecutionLease lease,
        DmColumn[] columns, string sql, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetSource(columns, sql, out string schema, out string table)) return Unknown(columns);
        await using var command = CreateCommand(connection, schema, table);
        await using var reader = await command.ExecuteInternalReaderAsync(lease, CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        var constraints = new List<DmReaderConstraintColumn>();
        while (await reader.ReadInternalAsync(cancellationToken).ConfigureAwait(false)) constraints.Add(ReadConstraint(reader));
        return Project(columns, constraints);
    }

    private static DmCommand CreateCommand(DmConnection connection, string schema, string table)
    {
        var command = connection.CreateCommand("SELECT C.CONSTRAINT_NAME,C.CONSTRAINT_TYPE,CC.COLUMN_NAME,CC.POSITION " +
            "FROM ALL_CONSTRAINTS C JOIN ALL_CONS_COLUMNS CC ON CC.OWNER=C.OWNER " +
            "AND CC.CONSTRAINT_NAME=C.CONSTRAINT_NAME AND CC.TABLE_NAME=C.TABLE_NAME " +
            "WHERE C.OWNER=:schema AND C.TABLE_NAME=:table AND C.CONSTRAINT_TYPE IN ('P','U') " +
            "AND C.STATUS='ENABLED' AND C.VALIDATED='VALIDATED' ORDER BY C.CONSTRAINT_NAME,CC.POSITION");
        command.Parameters.Add(new DmParameter("schema", schema));
        command.Parameters.Add(new DmParameter("table", table));
        return command;
    }

    private static DmReaderConstraintColumn ReadConstraint(DmDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));

    private static DmReaderColumnMetadata[] Unknown(DmColumn[] columns) => new DmReaderColumnMetadata[columns?.Length ?? 0];

    // Called only after the query's single base-table origin has been established.
    internal static DmReaderColumnMetadata[] Project(DmColumn[] columns, IEnumerable<DmReaderConstraintColumn> constraints)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in constraints.GroupBy(column => column.Constraint, StringComparer.Ordinal))
        {
            var members = group.OrderBy(column => column.Position).ToArray();
            for (int index = 0; index < members.Length; index++)
                if (members[index].Position != index + 1 || string.IsNullOrEmpty(members[index].Column) ||
                    members[index].Kind != members[0].Kind || members[index].Kind is not ("P" or "U"))
                    throw new InvalidDataException("Constraint catalog returned inconsistent column membership.");
            if (members[0].Kind == "P") foreach (var member in members) keys.Add(member.Column);
            if (members.Length == 1) unique.Add(members[0].Column);
        }
        var result = Unknown(columns);
        for (int index = 0; index < result.Length; index++)
        {
            string name = columns[index].GetBaseColumn();
            if (!string.IsNullOrEmpty(name)) result[index] = new(keys.Contains(name), unique.Contains(name));
        }
        return result;
    }

    internal static bool TryGetSource(DmColumn[] columns, string sql, out string schema, out string table)
    {
        schema = table = null;
        if (columns == null || columns.Length == 0) return false;
        List<Token> tokens;
        try { tokens = Tokenize(sql); }
        catch (NotSupportedException) { return false; }
        var parser = new SimpleQuery(tokens);
        if (!parser.Parse(out var source)) return false;
        foreach (var column in columns)
        {
            string currentSchema = column.GetSchema(), currentTable = column.GetTable();
            if (string.IsNullOrEmpty(currentSchema) || string.IsNullOrEmpty(currentTable) || string.IsNullOrEmpty(column.GetBaseColumn()))
                return false;
            if (schema != null && (schema != currentSchema || table != currentTable)) return false;
            schema = currentSchema; table = currentTable;
        }
        return source[^1].Matches(table) && (source.Count == 1 || source[0].Matches(schema));
    }

    private enum TokenKind { Word, Identifier, Value, Marker, Symbol }
    private readonly record struct Token(TokenKind Kind, string Text)
    {
        internal bool Is(string value) => Kind == TokenKind.Word && Text.Equals(value, StringComparison.OrdinalIgnoreCase);
        internal bool Matches(string value) => Text.Equals(value, Kind == TokenKind.Identifier ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    // This lexer preserves quoted identifiers and never treats literal contents as SQL.
    // Its whitelist parser below is deliberately narrower than the describe validator.
    private static List<Token> Tokenize(string sql)
    {
        var result = new List<Token>();
        for (int index = 0; index < sql.Length;)
        {
            char current = sql[index];
            if (char.IsWhiteSpace(current)) { index++; continue; }
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            { index += 2; while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++; continue; }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                index += 2; int depth = 1;
                while (index + 1 < sql.Length && depth != 0)
                {
                    if (sql[index] == '/' && sql[index + 1] == '*') { depth++; index += 2; }
                    else if (sql[index] == '*' && sql[index + 1] == '/') { depth--; index += 2; }
                    else index++;
                }
                if (depth != 0) throw new NotSupportedException("Unterminated SQL comment.");
                continue;
            }
            if (current is 'q' or 'Q' && index + 2 < sql.Length && sql[index + 1] == '\'')
            {
                char closing = sql[index + 2] switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', var delimiter => delimiter };
                index += 3;
                while (index + 1 < sql.Length && !(sql[index] == closing && sql[index + 1] == '\'')) index++;
                if (index + 1 >= sql.Length) throw new NotSupportedException("Unterminated SQL literal.");
                index += 2; result.Add(new(TokenKind.Value, "")); continue;
            }
            if (current is '\'' or '"' or '[')
            {
                char closing = current == '[' ? ']' : current;
                var value = new StringBuilder(); index++; bool ended = false;
                while (index < sql.Length)
                {
                    char next = sql[index++];
                    if (next != closing) { value.Append(next); continue; }
                    if (index < sql.Length && sql[index] == closing) { value.Append(closing); index++; continue; }
                    ended = true; break;
                }
                if (!ended) throw new NotSupportedException("Unterminated SQL quoted token.");
                result.Add(new(current == '\'' ? TokenKind.Value : TokenKind.Identifier, value.ToString())); continue;
            }
            if (current == '?' || (current is ':' or '@' && index + 1 < sql.Length && IsWordStart(sql[index + 1])))
            {
                int start = index++;
                if (current != '?') while (index < sql.Length && IsWordPart(sql[index])) index++;
                result.Add(new(TokenKind.Marker, sql[start..index])); continue;
            }
            if (IsWordStart(current))
            {
                int start = index++; while (index < sql.Length && IsWordPart(sql[index])) index++;
                result.Add(new(TokenKind.Word, sql[start..index])); continue;
            }
            if (char.IsDigit(current))
            {
                int start = index++; while (index < sql.Length && (char.IsDigit(sql[index]) || sql[index] == '.')) index++;
                result.Add(new(TokenKind.Value, sql[start..index])); continue;
            }
            result.Add(new(TokenKind.Symbol, current.ToString())); index++;
        }
        return result;
    }

    private static bool IsWordStart(char value) => char.IsLetter(value) || value is '_' or '$' or '#';
    private static bool IsWordPart(char value) => IsWordStart(value) || char.IsDigit(value);

    private sealed class SimpleQuery
    {
        private readonly List<Token> tokens;
        private int position;
        internal SimpleQuery(List<Token> tokens) => this.tokens = tokens;
        private bool End => position == tokens.Count;
        private bool Word(string value) => !End && tokens[position].Is(value);
        private bool TakeWord(string value) { if (!Word(value)) return false; position++; return true; }
        private bool Take(string value)
        { if (End || tokens[position].Kind != TokenKind.Symbol || tokens[position].Text != value) return false; position++; return true; }
        private bool Identifier(out Token value)
        {
            value = default;
            if (End || tokens[position].Kind is not (TokenKind.Word or TokenKind.Identifier)) return false;
            value = tokens[position++]; return true;
        }
        private bool Name(out List<Token> name, bool star = false)
        {
            name = new();
            if (star && Take("*")) return true;
            if (!Identifier(out var first)) return false;
            name.Add(first);
            while (Take("."))
            {
                if (star && Take("*")) return true;
                if (!Identifier(out var next)) return false;
                name.Add(next); if (name.Count > 2) return false;
            }
            return true;
        }
        private bool Alias()
        {
            if (TakeWord("AS")) return Identifier(out _);
            if (!End && tokens[position].Kind == TokenKind.Identifier) { position++; return true; }
            if (!End && tokens[position].Kind == TokenKind.Word && !Word("FROM") && !Word("WHERE") && !Word("ORDER")) position++;
            return true;
        }
        internal bool Parse(out List<Token> source)
        {
            source = null;
            if (!TakeWord("SELECT") || Word("DISTINCT") || Word("ALL")) return false;
            do { if (!Name(out _, star: true) || !Alias()) return false; } while (Take(","));
            if (!TakeWord("FROM") || !Name(out source) || !Alias()) return false;
            if (TakeWord("WHERE"))
            {
                do { if (!Predicate()) return false; } while (TakeWord("AND") || TakeWord("OR"));
            }
            if (TakeWord("ORDER"))
            {
                if (!TakeWord("BY")) return false;
                do { if (!Name(out _)) return false; if (!TakeWord("ASC")) TakeWord("DESC"); } while (Take(","));
            }
            Take(";"); return End;
        }
        private bool Operand()
        {
            if (End) return false;
            if (tokens[position].Kind is TokenKind.Value or TokenKind.Marker) { position++; return true; }
            if (Take("-") || Take("+"))
            { if (End || tokens[position].Kind != TokenKind.Value) return false; position++; return true; }
            return Name(out _);
        }
        private bool Predicate()
        {
            if (!Operand()) return false;
            if (TakeWord("IS")) { TakeWord("NOT"); return TakeWord("NULL"); }
            if (Take("=")) return Operand();
            if (Take("<")) { if (!Take("=")) Take(">"); return Operand(); }
            if (Take(">")) { Take("="); return Operand(); }
            if (Take("!")) return Take("=") && Operand();
            return false;
        }
    }
}
