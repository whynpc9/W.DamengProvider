using System;

namespace W.Dm;

/// <summary>The Commit request may have reached the server, but its result was not confirmed.</summary>
public sealed class DmCommitOutcomeUnknownException : DmException
{
    internal DmCommitOutcomeUnknownException(Exception cause)
        : base("Commit outcome is unknown. Do not retry this transaction or replay its work automatically.", cause)
    {
    }

#if NETSTANDARD2_1
    public bool IsTransient => false;
#else
    public override bool IsTransient => false;
#endif
}
