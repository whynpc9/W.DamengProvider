using System;

namespace W.Dm.Internal.Types;

internal readonly record struct DmTemporalProfileIdentity(string ServerVersion, int MessageVersion, string ServerEncoding,
    bool EncodingOverrideEmpty);
internal enum DmTemporalCapabilityState { CompatibilityLegacyUnverified, VerifiedSix, VerifiedSeven }

internal readonly record struct DmTemporalWriteTarget(int CType, DmTemporalWireLayout Layout,
    DmTemporalResultFormat Format, int ColumnScale, int EncodedScale, int? RequestedScale,
    bool ReliableFixed, int Mask);

// Compiled decisions from TP01-profile-handoff.physical-v11.md. No connection, probe or mutable registry.
internal readonly record struct DmTemporalCapabilityProfile(DmTemporalProfileIdentity Identity, DmTemporalCapabilityState State)
{
    internal bool DefaultSeven => State == DmTemporalCapabilityState.VerifiedSeven;
    internal string EvidenceId => State switch
    {
        DmTemporalCapabilityState.VerifiedSeven => "TP01-v11-shared-physical",
        DmTemporalCapabilityState.VerifiedSix => "TP01-v11-tls-physical",
        _ => "CompatibilityLegacyUnverified"
    };
    internal static DmTemporalProfileIdentity CaptureIdentity(string version, int messageVersion, string encoding,
        bool encodingOverrideEmpty) => new(version, messageVersion, encoding?.ToLowerInvariant(), encodingOverrideEmpty);

    internal static DmTemporalCapabilityProfile Resolve(DmTemporalProfileIdentity identity)
    {
        var state = identity.EncodingOverrideEmpty && identity.ServerVersion == "8.1.5.60"
            && identity.MessageVersion == 21 && identity.ServerEncoding == "utf-8" ? DmTemporalCapabilityState.VerifiedSeven
            : identity.EncodingOverrideEmpty && identity.ServerVersion == "8.1.4.6"
                && identity.MessageVersion == 11 && identity.ServerEncoding == "gb18030" ? DmTemporalCapabilityState.VerifiedSix
            : DmTemporalCapabilityState.CompatibilityLegacyUnverified;
        return new(identity, state);
    }

    internal void ValidateWriteTarget(DmTemporalWriteTarget target)
    {
        if (target.Format != DmTemporalResultFormat.OrdinaryPacked || target.Mask == 4)
            throw new NotSupportedException("Temporal result format or local timezone semantics are not verified for writing.");
        if (DmTemporalWireCodec.ForCType(target.CType) != target.Layout)
            throw new InvalidOperationException("Temporal target layout does not match its type.");
        if (target.RequestedScale is int requested) DmTemporalInteger.ValidateUserScale(requested);
        int column = target.ColumnScale, encoded = target.EncodedScale;
        if (column is < 0 or > 9 || encoded is < 0 or > 9)
            throw new NotSupportedException("Temporal target has unsupported metadata precision.");
        int demand = Math.Max(encoded, target.RequestedScale ?? encoded);
        if (target.CType == 14)
        {
            if (column != 0 || encoded != 0 || demand != 0)
                throw new NotSupportedException("DATE has no fractional seconds.");
            return;
        }
        bool extended = target.CType is 26 or 27;
        if (demand > (extended ? 7 : 6) || (target.ReliableFixed && column > (extended ? 7 : 6)))
            throw new NotSupportedException("Temporal write precision is unsupported by this layout.");
        if (demand == 7 && State != DmTemporalCapabilityState.VerifiedSeven)
            throw new NotSupportedException("Seven-digit temporal writes are not verified for this negotiated profile.");
        if (extended && State != DmTemporalCapabilityState.VerifiedSeven
            && !(target.ReliableFixed && column <= 6 && demand <= 6))
            throw new NotSupportedException("Extended temporal writes require a verified profile or existing fixed six-digit metadata.");
    }

    // NULL encodes no fraction, so only a reliable server-described declaration may
    // exceed the encodable precision. Advisory targets were already narrowed by the
    // binding and pass the ordinary gate; format, timezone and layout checks remain.
    internal void ValidateNullWriteTarget(DmTemporalWriteTarget target)
    {
        if (!target.ReliableFixed || target.CType == 14 || target.RequestedScale.HasValue)
        {
            ValidateWriteTarget(target);
            return;
        }
        if (target.Format != DmTemporalResultFormat.OrdinaryPacked || target.Mask == 4)
            throw new NotSupportedException("Temporal result format or local timezone semantics are not verified for writing.");
        if (DmTemporalWireCodec.ForCType(target.CType) != target.Layout)
            throw new InvalidOperationException("Temporal target layout does not match its type.");
        if (target.ColumnScale is < 0 or > 9 || target.EncodedScale != target.ColumnScale)
            throw new NotSupportedException("Temporal target has unsupported metadata precision.");
    }

    internal void ValidateWriteValue(DmTemporalWriteTarget target, DmTemporalFields fields)
    {
        ValidateWriteTarget(target);
        if (target.CType is not (15 or 22))
        {
            DmTemporalInteger.ValidateDate(fields);
            if (fields.Year < 1) throw new NotSupportedException("BCE temporal parameter writes are not verified.");
            if (fields.Year > 9999) throw new OverflowException("Temporal calendar exceeds the supported write range.");
            if (fields.Year == 1582 && fields.Month == 10 && fields.Day is >= 5 and <= 14)
                throw new NotSupportedException("Temporal write is in an unverified calendar transform region.");
        }
        DmTemporalInteger.ValidateTime(fields);
        if (fields.Hour == 24) throw new NotSupportedException("TIME hour 24 is not verified for writing.");
        if (target.ReliableFixed) DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, target.ColumnScale);
        DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, target.EncodedScale);
        if (target.RequestedScale is int requested) DmTemporalInteger.ValidateWireFraction(fields.NanosecondsWithinSecond, requested);
        if (fields.OffsetMinutes is int offset)
        {
            if (offset is < -779 or > 840) throw new OverflowException("Temporal offset exceeds the supported write range.");
            if (target.CType == 22) DmTemporalInteger.ProjectTimeOffset(fields);
            else DmTemporalInteger.ProjectDateTimeOffset(fields);
        }
        DmTemporalWireCodec.ValidateFields(fields, target.Layout, target.EncodedScale);
    }
}
