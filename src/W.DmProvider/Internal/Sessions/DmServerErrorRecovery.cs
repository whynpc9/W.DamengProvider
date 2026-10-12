using System;

namespace W.Dm.Internal.Sessions;

/// <summary>
/// Statement-level server errors after which the physical session is proven to stay
/// synchronized on the measured profile. Every number was characterized on 8.1.5.60
/// (sync/async, autocommit and inside a local transaction where the SQL is legal there):
/// same server session afterwards, no partial statement effect, no lock left, and an
/// enclosing transaction neither committed nor rolled back. Anything else stays Broken.
/// </summary>
internal static class DmServerErrorRecovery
{
    internal const string MeasuredServerVersion = "8.1.5.60";

    internal static bool IsStatementScoped(int number) => number switch
    {
        -564 => true,   // DISKSPACE LIMIT exceeded
        -2007 => true,  // syntax error
        -2106 => true,  // invalid table or view name
        -2124 => true,  // duplicate object name
        -2207 => true,  // missing sequence (DEFAULT NEXTVAL)
        -2713 => true,  // unsupported IDENTITY type
        -5512 => true,  // missing CREATE SCHEMA privilege
        -5622 => true,  // missing CREATE MATERIALIZED VIEW privilege
        -6169 => true,  // value too long
        -6602 => true,  // unique constraint violated
        -6607 => true,  // foreign key violated
        -6609 => true,  // NOT NULL violated
        -6628 => true,  // parent row still referenced (UPDATE ... NO ACTION)
        -6804 => true,  // parameter marker has no bound value
        -7069 => true,  // NOCYCLE sequence exhausted
        // -3510 is also EC_BAK_INFO_WRITE; the measured out-of-range RAISE case
        // does not prove that every use of that number leaves a usable session.
        // RAISE_APPLICATION_ERROR user range, measured at -20000/-20071/-20999/-29999/-30000.
        <= -20000 and >= -30000 => true,
        _ => false
    };

    internal static bool IsMeasuredProfile(string serverVersion) =>
        string.Equals(serverVersion, MeasuredServerVersion, StringComparison.Ordinal);

    // No local transaction is open: the statement ran in autocommit and its own
    // failure is the only server-side effect. Starting/Committing/RollingBack/
    // OutcomeUnknown are excluded because their protocol state is in flight.
    internal static bool IsIdleTransactionState(DmLocalTransactionState state) => state is
        DmLocalTransactionState.None or DmLocalTransactionState.Committed or
        DmLocalTransactionState.RolledBack or DmLocalTransactionState.CompletedExternally;
}
