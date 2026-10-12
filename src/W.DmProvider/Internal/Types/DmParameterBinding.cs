using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using W.Dm.Internal.Lobs;
using W.Dm.Internal.Execution;

namespace W.Dm.Internal.Types;

internal readonly record struct DmSqlStatementHead(string First, string Second, string Third);
internal enum DmSqlSavepointKind { Save, Rollback, Release }
internal readonly record struct DmSqlSavepointControl(DmSqlSavepointKind Kind, string Name, bool IsQuoted);

// All scales below are fractional decimal digits except WireScale and EncodingPackedScale,
// which preserve the complete protocol declaration for interval types.
internal sealed record DmTemporalParameterBinding(
    DmParameterMetadata Request, DmParameterTypeSource ResolvedSource,
    DmTemporalRawMetadata? RawMetadata, bool ReliableFixed,
    DmTemporalCapabilityProfile Profile, int CType, int Precision,
    DmTemporalWireLayout? Layout, int? ColumnScale, int? RequestedScale,
    int EncodedScale, int WireScale, int Mask, DmTemporalWriteTarget? Target,
    int? IntervalSubtype, int? IntervalLeadingPrecision, int? EncodingPackedScale);

// ActualFormat is the row format supplied by the reader; RawMetadata.ResultFormat
// remains Unknown when describe did not independently establish a row format.
internal readonly record struct DmTemporalReadDescriptor(
    DmTemporalRawMetadata? RawMetadata, int CType, int Precision, int WireScale,
    int FractionalScale, int Mask, int? IntervalSubtype, int? IntervalLeadingPrecision,
    DmTemporalResultFormat ActualFormat);

internal static class DmTemporalMetadataSemantics
{
    internal static bool IsTemporal(int cType) => cType is 14 or 15 or 16 or 20 or 21 or 22 or 23 or 26 or 27;

    internal static (int FractionalScale, int Mask, int? IntervalSubtype, int? IntervalLeadingPrecision)
        Interpret(int cType, int wireScale, int mask = 0)
    {
        if (!IsTemporal(cType)) throw new InvalidCastException("Field has no temporal metadata.");
        if (cType is 20 or 21)
        {
            if (wireScale < 0 || (wireScale & ~0xFFF) != 0)
                throw new InvalidDataException("Interval metadata contains undefined bits.");
            int subtype = wireScale >> 8, leading = (wireScale >> 4) & 15, fraction = wireScale & 15;
            if (leading is < 1 or > 9 || fraction > 9 ||
                (cType == 20 ? subtype is < 0 or > 2 || fraction != 0 : subtype is < 3 or > 12))
                throw new InvalidDataException("Interval metadata contains invalid declaration fields.");
            return (fraction, mask, subtype, leading);
        }
        int semantic = wireScale;
        if (cType is 16 or 26)
        {
            int flags = wireScale & 0x3000;
            if (flags == 0x3000 || wireScale < 0 || (wireScale & ~0x300F) != 0)
                throw new InvalidDataException("Timestamp metadata contains undefined or conflicting flags.");
            semantic = wireScale & ~0x3000;
            if (flags != 0) mask = flags == 0x1000 ? 4 : 1;
        }
        if (semantic is < 0 or > 9 || (cType == 14 && semantic != 0))
            throw new InvalidDataException("Temporal metadata contains invalid fractional precision.");
        return (semantic, mask, null, null);
    }
}

internal static class DmTemporalBindingResolver
{
    internal static void ValidateRequest(DmParameterMetadata request)
    {
        if (request.ExplicitScale) DmTemporalInteger.ValidateUserScale(request.Scale);
        if (request.ExplicitPrecision && request.Precision != 0)
            throw new NotSupportedException("Temporal parameters use Scale rather than nonzero Precision.");
    }

    internal static DmTemporalParameterBinding Create(DmParameterMetadata request,
        DmParameterTypeSource source, int selectedType, DmField field, DmTemporalCapabilityProfile profile)
    {
        ValidateRequest(request);
        var raw = field.TemporalServerMetadata;
        bool fixedMode = field.GetTypeFlag() == 1;
        bool reliable = raw is { Source: DmTemporalMetadataSource.ParameterDescribe, TypeFlag: 1 }
            && DmTemporalMetadataSemantics.IsTemporal(raw.Value.CType);
        if (fixedMode && !reliable)
            throw new InvalidOperationException("Fixed temporal binding requires original server describe metadata.");
        int cType = reliable ? raw.Value.CType : selectedType;
        int mask = 0;
        int? columnScale = null, subtype = null, leading = null;
        if (reliable)
        {
            var semantic = DmTemporalMetadataSemantics.Interpret(cType, raw.Value.Scale, raw.Value.DerivedMask);
            columnScale = semantic.FractionalScale;
            mask = semantic.Mask;
            subtype = semantic.IntervalSubtype;
            leading = semantic.IntervalLeadingPrecision;
        }
        else if (field.mask != 0)
            throw new NotSupportedException("Decorated advisory temporal metadata cannot select a new target.");
        int? requested = request.ExplicitScale ? request.Scale : null;
        if (cType is 20 or 21)
        {
            if ((requested ?? 0) > (cType == 20 ? 0 : 6))
                throw new NotSupportedException("Interval fractional write precision is unsupported.");
            if (!reliable)
            {
                subtype = cType == 21 ? 6 : 1;
                leading = 9;
            }
            int encoded = reliable ? columnScale.Value : requested ?? (cType == 21 ? 6 : 0);
            if (encoded > (cType == 21 ? 6 : 0) || mask != 0)
                throw new NotSupportedException("Interval target declaration is unsupported for writing.");
            int packed = reliable ? raw.Value.Scale : (subtype.Value << 8) | (leading.Value << 4) | encoded;
            return new(request, source, raw, reliable, profile, cType,
                reliable ? raw.Value.Precision : cType == 21 ? 24 : 12,
                null, columnScale, requested, encoded, packed, mask, null, subtype, leading, packed);
        }
        int defaultScale = cType == 14 ? 0 : 6;
        if (!reliable && cType is 16 or 26 or 23 or 27)
        {
            bool explicitDateTime2 = request.ExplicitDbType && request.DeclaredDbType == System.Data.DbType.DateTime2;
            bool inferred = !request.ExplicitDbType && !request.ExplicitDmSqlType;
            defaultScale = explicitDateTime2 ? 7 : inferred && profile.DefaultSeven ? 7 : 6;
        }
        int scale = requested ?? columnScale ?? defaultScale;
        if (!reliable && cType is 16 or 26 or 23 or 27)
            cType = cType is 23 or 27 ? scale >= 7 ? 27 : 23 : scale >= 7 ? 26 : 16;
        var layout = DmTemporalWireCodec.ForCType(cType);
        int width = reliable ? raw.Value.Precision : cType switch
        {
            14 => 3, 15 => 5, 22 => 7, 16 => 8, 26 => 9, 23 => 10, 27 => 11,
            _ => throw new NotSupportedException("Temporal target type is unsupported.")
        };
        var target = new DmTemporalWriteTarget(cType, layout, DmTemporalResultFormat.OrdinaryPacked,
            columnScale ?? scale, scale, requested, reliable, mask);
        profile.ValidateWriteTarget(target); // Includes typed NULL before any marker is written.
        return new(request, source, raw, reliable, profile, cType, width, layout,
            columnScale, requested, scale, reliable ? columnScale.Value : scale, mask,
            target, null, null, null);
    }
}

/// <summary>Frozen binding order from SQL lexical markers; never rewrites the SQL.</summary>
internal sealed class DmParameterBinding
{
    private const int MaxMarkers = 4096;
    private readonly DmParameterCollection parameters;
    private readonly int[] occurrenceIndexes;
    private readonly int[] distinctIndexes;
    private readonly bool named;

    private DmParameterBinding(DmParameterCollection parameters, int[] occurrences, int[] distinct, bool named)
    {
        this.parameters = parameters;
        occurrenceIndexes = occurrences;
        distinctIndexes = distinct;
        this.named = named;
    }

    internal int MarkerCount => occurrenceIndexes.Length;

    internal static DmParameterBinding Create(string sql, DmParameterCollection parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        ScanResult scan = Scan(sql);
        if (scan.Named.Count != 0 && scan.PositionalCount != 0)
            throw new NotSupportedException("Named and positional parameter markers cannot be mixed.");

        var occurrences = new List<int>(scan.Named.Count + scan.PositionalCount);
        var distinct = new List<int>();
        var seen = new HashSet<int>();
        bool named = scan.Named.Count != 0;
        if (named)
        {
            foreach (string marker in scan.Named)
            {
                int index = parameters.do_IndexOf(marker);
                if (index < 0) throw new InvalidOperationException("SQL marker has no matching parameter.");
                occurrences.Add(index);
                if (seen.Add(index)) distinct.Add(index);
            }
            if (distinct.Count != parameters.do_Count)
                throw new InvalidOperationException("Parameter collection contains an unused binding.");
        }
        else
        {
            if (scan.PositionalCount != parameters.do_Count)
                throw new InvalidOperationException("Positional marker count does not match parameters.");
            for (int index = 0; index < scan.PositionalCount; index++)
            {
                occurrences.Add(index);
                distinct.Add(index);
            }
        }
        var inputIdentities = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (int index in occurrences)
            if (parameters.do_GetParameter(index).do_Value is DmLobInput input && !inputIdentities.Add(input.SourceIdentity))
                throw new NotSupportedException("One streaming input cannot be bound to multiple SQL occurrences.");
        return new DmParameterBinding(parameters, occurrences.ToArray(), distinct.ToArray(), named);
    }

    internal DmParameter ResolveServerParameter(int serverIndex, string serverName, int serverCount)
    {
        if (serverIndex < 0 || serverIndex >= serverCount ||
            (serverCount != occurrenceIndexes.Length && serverCount != distinctIndexes.Length))
            throw new InvalidOperationException("Server parameter metadata does not match SQL markers.");
        if (named && !string.IsNullOrWhiteSpace(serverName))
        {
            int byName = parameters.do_IndexOf(serverName);
            if (byName >= 0) return parameters.do_GetParameter(byName);
        }
        int index = serverCount == occurrenceIndexes.Length
            ? occurrenceIndexes[serverIndex] : distinctIndexes[serverIndex];
        return parameters.do_GetParameter(index);
    }

    internal void ValidateServerCount(int count)
    {
        if (count != occurrenceIndexes.Length && count != distinctIndexes.Length)
            throw new InvalidOperationException("Server parameter metadata does not match SQL markers.");
    }

    internal static bool IsReliableDescribe(int cType) => cType is
        0 or 1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 14 or 15 or
        16 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 26 or 27 or 28;

    internal static IReadOnlyList<DmSqlStatementHead> ScanTopLevelStatements(string sql) => Scan(sql).Heads;

    // Parse only a complete single control statement. Keep the original SQL for
    // execution; decoded identifiers are used solely for ownership and tracking.
    internal static bool TryParseSavepointControl(string sql, out DmSqlSavepointControl control)
    {
        ArgumentNullException.ThrowIfNull(sql);
        control = default;
        if (sql.IndexOf('\0') >= 0) return false;
        int index = 0;
        DmSqlSavepointKind kind;
        if (ReadRollbackKeyword(sql, ref index, "SAVEPOINT")) kind = DmSqlSavepointKind.Save;
        else
        {
            index = 0;
            if (ReadRollbackKeyword(sql, ref index, "ROLLBACK"))
            {
                if (!ReadRollbackKeyword(sql, ref index, "TO")) return false;
                kind = DmSqlSavepointKind.Rollback;
            }
            else
            {
                index = 0;
                if (!ReadRollbackKeyword(sql, ref index, "RELEASE")) return false;
                if (!ReadRollbackKeyword(sql, ref index, "SAVEPOINT")) return false;
                kind = DmSqlSavepointKind.Release;
            }
            if (kind == DmSqlSavepointKind.Rollback)
            {
                int optional = index;
                if (ReadRollbackKeyword(sql, ref optional, "SAVEPOINT")) index = optional;
            }
        }
        if (!SkipRollbackTrivia(sql, ref index) ||
            !ReadSavepointIdentifier(sql, ref index, out string name, out bool quoted) ||
            !SkipRollbackTrivia(sql, ref index)) return false;
        if (index < sql.Length && sql[index] == ';')
        {
            index++;
            if (!SkipRollbackTrivia(sql, ref index)) return false;
        }
        if (index != sql.Length) return false;
        control = new DmSqlSavepointControl(kind, name, quoted);
        return true;
    }

    private static bool ReadSavepointIdentifier(string sql, ref int index, out string name, out bool quoted)
    {
        name = null;
        quoted = index < sql.Length && sql[index] == '"';
        if (index == sql.Length) return false;
        if (!quoted)
        {
            if (!IsWordStart(sql[index])) return false;
            int start = index++;
            while (index < sql.Length && IsWordPart(sql[index])) index++;
            name = sql.Substring(start, index - start);
            return true;
        }
        index++;
        var decoded = new StringBuilder();
        while (index < sql.Length)
        {
            char current = sql[index++];
            if (current != '"') { decoded.Append(current); continue; }
            if (index < sql.Length && sql[index] == '"')
            {
                decoded.Append('"');
                index++;
                continue;
            }
            if (decoded.Length == 0) return false;
            name = decoded.ToString();
            return true;
        }
        return false;
    }

    /// <summary>Recognizes one complete savepoint rollback without rewriting its SQL.</summary>
    internal static bool IsStrictRollbackToSavepoint(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (sql.IndexOf('\0') >= 0) return false;
        int index = 0;
        if (!ReadRollbackKeyword(sql, ref index, "ROLLBACK") ||
            !ReadRollbackKeyword(sql, ref index, "TO") ||
            !SkipRollbackTrivia(sql, ref index) || index == sql.Length)
            return false;

        if (sql[index] != '"')
        {
            int nameStart = index;
            if (!IsWordStart(sql[index])) return false;
            while (++index < sql.Length && IsWordPart(sql[index])) { }
            if (sql.AsSpan(nameStart, index - nameStart).Equals("SAVEPOINT".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                if (!SkipRollbackTrivia(sql, ref index) || index == sql.Length ||
                    !ReadRollbackIdentifier(sql, ref index)) return false;
            }
        }
        else if (!ReadRollbackIdentifier(sql, ref index)) return false;

        if (!SkipRollbackTrivia(sql, ref index)) return false;
        if (index < sql.Length && sql[index] == ';')
        {
            index++;
            if (!SkipRollbackTrivia(sql, ref index)) return false;
        }
        return index == sql.Length;
    }

    private static bool ReadRollbackKeyword(string sql, ref int index, string keyword)
    {
        if (!SkipRollbackTrivia(sql, ref index)) return false;
        int start = index;
        if (index == sql.Length || !IsWordStart(sql[index])) return false;
        while (++index < sql.Length && IsWordPart(sql[index])) { }
        return sql.AsSpan(start, index - start).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadRollbackIdentifier(string sql, ref int index)
    {
        if (index == sql.Length) return false;
        if (sql[index] != '"')
        {
            if (!IsWordStart(sql[index])) return false;
            while (++index < sql.Length && IsWordPart(sql[index])) { }
            return true;
        }
        index++;
        bool hasCharacter = false;
        while (index < sql.Length)
        {
            char current = sql[index++];
            if (current != '"') { hasCharacter = true; continue; }
            if (index < sql.Length && sql[index] == '"')
            {
                hasCharacter = true;
                index++;
                continue;
            }
            return hasCharacter;
        }
        return false;
    }

    private static bool SkipRollbackTrivia(string sql, ref int index)
    {
        while (index < sql.Length)
        {
            if (char.IsWhiteSpace(sql[index])) { index++; continue; }
            if (sql[index] == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (sql[index] == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                if (!TrySkipBlockComment(sql, ref index)) return false;
                continue;
            }
            break;
        }
        return true;
    }

    private static bool TrySkipBlockComment(string sql, ref int index)
    {
        // R1 supports non-nested block comments, ending at the first closing delimiter.
        int end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
        if (end < 0) return false;
        index = end + 2;
        return true;
    }

    private sealed class ScanResult
    {
        internal List<string> Named { get; } = new();
        internal int PositionalCount;
        internal List<DmSqlStatementHead> Heads { get; } = new();
    }

    private static ScanResult Scan(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var result = new ScanResult();
        var words = new List<string>(3);
        int parentheses = 0;
        for (int index = 0; index < sql.Length;)
        {
            char current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                index += 2;
                while (index < sql.Length && sql[index] is not ('\r' or '\n')) index++;
                continue;
            }
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                if (!TrySkipBlockComment(sql, ref index))
                    throw new InvalidDataException("Unterminated SQL comment.");
                continue;
            }
            if ((current is 'q' or 'Q') && index + 2 < sql.Length && sql[index + 1] == '\'')
            {
                char opening = sql[index + 2];
                char closing = opening switch { '[' => ']', '(' => ')', '{' => '}', '<' => '>', _ => opening };
                index += 3;
                bool ended = false;
                while (index + 1 < sql.Length)
                {
                    if (sql[index] == closing && sql[index + 1] == '\'')
                    {
                        index += 2;
                        ended = true;
                        break;
                    }
                    index++;
                }
                if (!ended) throw new InvalidDataException("Unterminated SQL alternate quoted value.");
                continue;
            }
            if (current == '\'' || current == '"')
            {
                char quote = current;
                index++;
                bool ended = false;
                while (index < sql.Length)
                {
                    if (sql[index++] != quote) continue;
                    if (index < sql.Length && sql[index] == quote) { index++; continue; }
                    ended = true;
                    break;
                }
                if (!ended) throw new InvalidDataException("Unterminated SQL quoted value.");
                continue;
            }
            if (current == '[')
            {
                index++;
                while (index < sql.Length && sql[index] != ']') index++;
                if (index == sql.Length) throw new InvalidDataException("Unterminated SQL identifier.");
                index++;
                continue;
            }
            if (current == ':' && index + 1 < sql.Length && (sql[index + 1] is ':' or '='))
            {
                index += 2;
                continue;
            }
            if (current == '@' && index + 1 < sql.Length && sql[index + 1] == '@')
            {
                index += 2;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                continue;
            }
            if ((current is ':' or '@') && index + 1 < sql.Length && IsWordStart(sql[index + 1]))
            {
                int start = ++index;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                if (result.Named.Count + result.PositionalCount >= MaxMarkers)
                    throw new InvalidDataException("Too many SQL parameter markers.");
                result.Named.Add(sql.Substring(start, index - start));
                continue;
            }
            if (current == '?')
            {
                if (result.Named.Count + result.PositionalCount >= MaxMarkers)
                    throw new InvalidDataException("Too many SQL parameter markers.");
                result.PositionalCount++;
                index++;
                continue;
            }
            if (current == '(') { parentheses++; index++; continue; }
            if (current == ')') { if (parentheses > 0) parentheses--; index++; continue; }
            if (current == ';' && parentheses == 0)
            {
                AddHead(result.Heads, words);
                words.Clear();
                index++;
                continue;
            }
            if (parentheses == 0 && IsWordStart(current))
            {
                int start = index++;
                while (index < sql.Length && IsWordPart(sql[index])) index++;
                if (words.Count < 3) words.Add(sql.Substring(start, index - start).ToUpperInvariant());
                continue;
            }
            index++;
        }
        AddHead(result.Heads, words);
        return result;
    }

    private static void AddHead(List<DmSqlStatementHead> heads, List<string> words)
    {
        if (words.Count == 0) return;
        if (heads.Count >= MaxMarkers) throw new InvalidDataException("Too many SQL statements.");
        heads.Add(new DmSqlStatementHead(words[0], words.Count > 1 ? words[1] : string.Empty,
            words.Count > 2 ? words[2] : string.Empty));
    }

    private static bool IsWordStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsWordPart(char value) => char.IsLetterOrDigit(value) || value is '_' or '$' or '#';
}
