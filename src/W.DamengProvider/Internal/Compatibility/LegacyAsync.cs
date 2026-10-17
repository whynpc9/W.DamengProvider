#if NETSTANDARD2_1
// These aliases apply only to the legacy asset; current framework implementations remain in use elsewhere.
global using W.Dm.Internal.Compatibility;
global using ReferenceEqualityComparer = W.Dm.Internal.Compatibility.DmReferenceEqualityComparer;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace W.Dm.Internal.Compatibility;

internal sealed class DmReferenceEqualityComparer : IEqualityComparer<object>
{
    internal static readonly DmReferenceEqualityComparer Instance = new();
    public new bool Equals(object x, object y) => ReferenceEquals(x, y);
    public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
}

internal static class LegacyAsync
{
    internal static CancellationTokenRegistration UnsafeRegister(this CancellationToken token, Action<object> callback, object state)
    {
        if (ExecutionContext.IsFlowSuppressed()) return token.Register(callback, state, useSynchronizationContext: false);
        using (ExecutionContext.SuppressFlow())
            return token.Register(callback, state, useSynchronizationContext: false);
    }

    internal static async Task WaitAsync(this Task task, CancellationToken token)
    {
        if (task.IsCompleted || !token.CanBeCanceled) { await task.ConfigureAwait(false); return; }
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.UnsafeRegister(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), canceled))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                // DNS and connect can finish after cancellation; observe their eventual faults.
                _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new OperationCanceledException(token);
            }
            await task.ConfigureAwait(false);
        }
    }

    internal static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken token)
    {
        await WaitAsync((Task)task, token).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
}
#endif
