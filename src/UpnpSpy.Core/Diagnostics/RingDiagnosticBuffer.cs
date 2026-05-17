using UpnpSpy.Core.Models;

namespace UpnpSpy.Core.Diagnostics;

public sealed class RingDiagnosticBuffer : IDiagnosticBuffer
{
    private readonly object _gate = new();
    private readonly Queue<DiagnosticEntry> _entries;
    private readonly List<IObserver<DiagnosticEntry>> _observers = new();

    public RingDiagnosticBuffer(int capacity = 5_000)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        Capacity = capacity;
        _entries = new Queue<DiagnosticEntry>(capacity);
    }

    public int Capacity { get; }

    public void Record(DiagnosticEntry entry)
    {
        IObserver<DiagnosticEntry>[] observers;
        lock (_gate)
        {
            if (_entries.Count >= Capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
            observers = _observers.Count == 0 ? Array.Empty<IObserver<DiagnosticEntry>>() : _observers.ToArray();
        }

        foreach (var observer in observers)
        {
            try { observer.OnNext(entry); }
            catch { /* a misbehaving observer must not disrupt the sink (FR-042) */ }
        }
    }

    public IReadOnlyList<DiagnosticEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public IDisposable Subscribe(IObserver<DiagnosticEntry> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            _observers.Add(observer);
        }
        return new Subscription(this, observer);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly RingDiagnosticBuffer _owner;
        private IObserver<DiagnosticEntry>? _observer;

        public Subscription(RingDiagnosticBuffer owner, IObserver<DiagnosticEntry> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            var observer = _observer;
            if (observer is null) return;
            _observer = null;
            lock (_owner._gate)
            {
                _owner._observers.Remove(observer);
            }
        }
    }
}
