namespace W.Dm.Internal.Types;

internal enum DmTemporalMetadataSource { Synthetic, ParameterDescribe, ResultColumn }
internal enum DmTemporalResultFormat { OrdinaryPacked, Bdta, Unknown }

// Captured before effective metadata is normalized or rebound. No mutable field/connection references.
// If the protocol sends no mask, MaskBeforeNormalization is zero; DerivedMask records interpretation.
internal readonly record struct DmTemporalRawMetadata(
    DmTemporalMetadataSource Source, int CType, int Precision, int Scale, int TypeFlag,
    int MaskBeforeNormalization, int DerivedMask, DmTemporalResultFormat ResultFormat)
{
    internal bool HasServerSource => Source is DmTemporalMetadataSource.ParameterDescribe
        or DmTemporalMetadataSource.ResultColumn;
}
