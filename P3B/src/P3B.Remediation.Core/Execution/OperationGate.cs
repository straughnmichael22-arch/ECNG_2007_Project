namespace P3B.Remediation.Core.Execution;

/// <summary>
/// Allows one system-changing operation at a time (P3B-PR-01). Shared by remediation and
/// rollback so they can never run together.
/// </summary>
public sealed class OperationGate
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>Returns a lease to dispose when done, or null if another operation is active.</summary>
    public IDisposable? TryAcquire() =>
        Interlocked.CompareExchange(ref _busy, 1, 0) == 0 ? new Lease(this) : null;

    private sealed class Lease(OperationGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Volatile.Write(ref gate._busy, 0);
            }
        }
    }
}
