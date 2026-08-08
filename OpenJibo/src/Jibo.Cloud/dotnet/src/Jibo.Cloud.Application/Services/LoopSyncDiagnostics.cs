namespace Jibo.Cloud.Application.Services;

/// <summary>
/// One recorded robot-facing Loop protocol call, kept for <c>GET /api/portal/loop-sync-status</c>
/// so an operator can see whether SSM has ever called <c>Loop#list()</c>, what identity it
/// resolved to, and whether it silently got the synthetic bootstrap loop instead of the
/// household loop.
/// </summary>
public sealed record LoopSyncCallRecord(
    DateTimeOffset AtUtc,
    string? HostName,
    string? Path,
    string Operation,
    string IdentitySource,
    string? ResolvedDeviceId,
    string? AccessKeyFingerprint,
    bool FirstUseBindingCreated,
    string? LoopId,
    int MemberCount,
    int MembersWithFirstNameCount,
    bool BootstrapLoopReturned);

/// <summary>
/// Thread-safe ring buffer of the most recent robot Loop protocol calls
/// (<c>List</c> / <c>ListLoops</c> / <c>ListMembers</c> / <c>ListLoopMembers</c>).
/// </summary>
public sealed class LoopSyncDiagnostics
{
    private const int MaxRecords = 25;

    private readonly Lock _syncRoot = new();
    private readonly List<LoopSyncCallRecord> _records = [];
    private long _totalListCallsSeen;
    private long _credentialBindingsCreated;

    public void RecordCall(LoopSyncCallRecord record)
    {
        lock (_syncRoot)
        {
            _records.Add(record);
            if (_records.Count > MaxRecords)
                _records.RemoveAt(0);
        }

        if (IsListOperation(record.Operation))
            Interlocked.Increment(ref _totalListCallsSeen);
        if (record.FirstUseBindingCreated)
            Interlocked.Increment(ref _credentialBindingsCreated);
    }

    /// <summary>Most recent calls first.</summary>
    public IReadOnlyList<LoopSyncCallRecord> GetRecentCalls(int count = 10)
    {
        lock (_syncRoot)
        {
            return _records
                .Skip(Math.Max(0, _records.Count - count))
                .Reverse()
                .ToArray();
        }
    }

    public LoopSyncCallRecord? LastListCall
    {
        get
        {
            lock (_syncRoot)
            {
                for (var i = _records.Count - 1; i >= 0; i--)
                {
                    if (IsListOperation(_records[i].Operation))
                        return _records[i];
                }

                return null;
            }
        }
    }

    /// <summary>Total <c>List</c>/<c>ListLoops</c> calls ever seen, not limited to the ring buffer.</summary>
    public long TotalListCallsSeen => Interlocked.Read(ref _totalListCallsSeen);

    /// <summary>Total first-use SigV4 credential bindings created via protocol calls.</summary>
    public long CredentialBindingsCreated => Interlocked.Read(ref _credentialBindingsCreated);

    private static bool IsListOperation(string operation) =>
        operation.Equals("List", StringComparison.OrdinalIgnoreCase) ||
        operation.Equals("ListLoops", StringComparison.OrdinalIgnoreCase);
}
