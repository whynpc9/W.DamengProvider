using System;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;

namespace W.Dm;

/// <summary>
/// Read-only snapshot of the driver's capability gates for one open physical session.
/// Each flag is evaluated by the same rule that gates the operation itself, so a
/// caller can choose a supported path before sending anything. Gates are opened only
/// for measured server profiles; an unlisted version reports the conservative value.
/// </summary>
public sealed class DmServerCapabilities
{
    internal const string MeasuredServerVersion = "8.1.5.60";

    /// <summary>The version string reported by the server during login.</summary>
    public string ServerVersion { get; }

    /// <summary>True when the server version matches a profile measured for this driver release.</summary>
    public bool IsMeasuredProfile { get; }

    /// <summary>Whether <see cref="DmTransaction.SupportsSavepoints"/> and user savepoint SQL are available.</summary>
    public bool SupportsSavepoints { get; }

    /// <summary>Whether BeginTransaction accepts <see cref="System.Data.IsolationLevel.ReadUncommitted"/>.</summary>
    public bool SupportsReadUncommitted { get; }

    /// <summary>Whether BeginTransaction accepts <see cref="System.Data.IsolationLevel.Serializable"/>.</summary>
    public bool SupportsSerializable { get; }

    /// <summary>
    /// Whether measured statement-level server errors (for example unique or NOT NULL violations)
    /// keep the session and an active local transaction usable. Otherwise such errors close the session.
    /// </summary>
    public bool SupportsStatementErrorRecovery { get; }

    /// <summary>
    /// Whether a single CREATE/ALTER/DROP/TRUNCATE TABLE statement is allowed inside a local transaction.
    /// Its implicit commit is observed and reported as <see cref="DmTransactionOutcome.CompletedExternally"/>.
    /// </summary>
    public bool SupportsTableDdlInTransaction { get; }

    /// <summary>
    /// Largest fractional-second scale accepted for non-NULL TIMESTAMP and TIMESTAMP WITH TIME ZONE
    /// parameter values: 7 on a verified seven-digit profile, otherwise 6. TIME values stay at 6.
    /// A typed NULL parameter with a larger Scale declares at most this scale, or keeps a
    /// server-described 8/9-digit column declaration on the verified seven-digit profile.
    /// </summary>
    public int MaxTemporalWriteScale { get; }

    /// <summary>Stable identifier of the temporal evidence profile selected for this session.</summary>
    public string TemporalProfile { get; }

    internal DmServerCapabilities(string serverVersion, DmTemporalCapabilityProfile temporal)
    {
        ServerVersion = serverVersion;
        IsMeasuredProfile = IsMeasured(serverVersion);
        SupportsSavepoints = IsMeasuredProfile;
        SupportsReadUncommitted = DmConnection.IsPublicTransactionIsolationSupported(
            System.Data.IsolationLevel.ReadUncommitted, serverVersion);
        SupportsSerializable = DmConnection.IsPublicTransactionIsolationSupported(
            System.Data.IsolationLevel.Serializable, serverVersion);
        SupportsStatementErrorRecovery = DmServerErrorRecovery.IsMeasuredProfile(serverVersion);
        SupportsTableDdlInTransaction = IsMeasuredProfile;
        MaxTemporalWriteScale = temporal.DefaultSeven ? 7 : 6;
        TemporalProfile = temporal.EvidenceId;
    }

    internal static bool IsMeasured(string serverVersion) =>
        string.Equals(serverVersion, MeasuredServerVersion, StringComparison.Ordinal);
}
