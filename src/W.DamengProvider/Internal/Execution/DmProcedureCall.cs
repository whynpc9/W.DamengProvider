using System;
using System.Data;
using System.Text;

namespace W.Dm.Internal.Execution;

/// <summary>Parses only an identifier, never executable caller-supplied SQL.</summary>
internal static class DmProcedureCall
{
    internal static string Create(string name, DmParameterCollection parameters)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        int offset = 0;
        string identifier = ReadIdentifier(name, ref offset);
        SkipSpace(name, ref offset);
        if (offset < name.Length && name[offset] == '.')
        {
            offset++;
            identifier += "." + ReadIdentifier(name, ref offset);
            SkipSpace(name, ref offset);
        }
        if (offset != name.Length) throw InvalidName();
        int returns = 0;
        foreach (DmParameter parameter in parameters)
            if (parameter.do_Direction == ParameterDirection.ReturnValue) returns++;
        if (returns > 1) throw new InvalidOperationException("A function has only one return parameter.");
        int argumentCount = parameters.Count - returns;
        var sql = new StringBuilder();
        if (returns == 1) sql.Append("? = ");
        else if (argumentCount == 0) sql.Append("CALL ");
        sql.Append(identifier).Append('(');
        for (int index = 0; index < argumentCount; index++)
        {
            if (index != 0) sql.Append(',');
            sql.Append('?');
        }
        return sql.Append(')').ToString();
    }

    private static string ReadIdentifier(string text, ref int offset)
    {
        SkipSpace(text, ref offset);
        if (offset == text.Length) throw InvalidName();
        var decoded = new StringBuilder();
        if (text[offset] == '"')
        {
            offset++;
            bool closed = false;
            while (offset < text.Length)
            {
                char current = text[offset++];
                if (char.IsControl(current)) throw InvalidName();
                if (current != '"') { decoded.Append(current); continue; }
                if (offset < text.Length && text[offset] == '"')
                {
                    offset++;
                    decoded.Append('"');
                    continue;
                }
                closed = true;
                break;
            }
            if (!closed || decoded.Length == 0) throw InvalidName();
        }
        else
        {
            if (!char.IsLetter(text[offset]) && text[offset] != '_') throw InvalidName();
            int start = offset++;
            while (offset < text.Length && (char.IsLetterOrDigit(text[offset]) || text[offset] is '_' or '$' or '#')) offset++;
            decoded.Append(text.Substring(start, offset - start).ToUpperInvariant());
        }
        return "\"" + decoded.ToString().Replace("\"", "\"\"") + "\"";
    }

    private static void SkipSpace(string text, ref int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset])) offset++;
    }

    private static ArgumentException InvalidName() => new(
        "StoredProcedure CommandText must be a single procedure or function identifier, optionally schema-qualified.", "CommandText");
}
