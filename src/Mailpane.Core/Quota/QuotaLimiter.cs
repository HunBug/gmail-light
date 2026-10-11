using Mailpane.Core.Gmail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mailpane.Core.Quota;

/// <summary>
/// Token bucket for one account (D-013, ARCHITECTURE.md 8). Capacity and refill are both
/// <c>unitsPerMinute</c>; the bucket starts full. Waiters are served first-in first-out, interactive
/// before background. After a rate-limit error the bucket is emptied and nothing is granted or refilled
/// until the backoff has passed.
/// </summary>
public sealed class QuotaLimiter : IDisposable
{
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(32);

    private readonly string _name;
    private readonly double _capacity;
    private readonly double _perSecond;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Func<double> _jitter;
    private readonly Lock _lock = new();
    private readonly LinkedList<Waiter> _interactive = new();
    private readonly LinkedList<Waiter> _background = new();

    private double _tokens;
    private DateTimeOffset _lastRefill;
    private DateTimeOffset _backoffUntil = DateTimeOffset.MinValue;
    private int _backoffAttempt;
    private ITimer? _timer;
    private bool _disposed;

    private DateTimeOffset _windowStart;
    private long _windowUnits;

    /// <param name="jitter">Returns a value in [0, 1); scaled to up to one second of backoff jitter.</param>
    public QuotaLimiter(string name, int unitsPerMinute, TimeProvider? time = null, ILogger? logger = null, Func<double>? jitter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(unitsPerMinute, 1);
        _name = name;
        _capacity = unitsPerMinute;
        _perSecond = unitsPerMinute / 60.0;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _jitter = jitter ?? Random.Shared.NextDouble;
        _tokens = _capacity;
        _lastRefill = _windowStart = _time.GetUtcNow();
    }

    public int UnitsPerMinute => (int)_capacity;

    /// <summary>Completes when <paramref name="cost"/> units have been taken from the bucket.</summary>
    public Task AcquireAsync(int cost, CallPriority priority, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cost, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cost, (int)_capacity);
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        Waiter waiter;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _time.GetUtcNow();
            Refill(now);
            var queueAhead = priority == CallPriority.Interactive
                ? _interactive.Count > 0
                : _interactive.Count > 0 || _background.Count > 0;
            if (!queueAhead && now >= _backoffUntil && HasTokens(cost))
            {
                Spend(cost, now);
                return Task.CompletedTask;
            }

            waiter = new Waiter(cost);
            waiter.Node = (priority == CallPriority.Interactive ? _interactive : _background).AddLast(waiter);
            Pump();
        }

        // Registered outside the lock: an already-cancelled token runs the callback synchronously.
        if (ct.CanBeCanceled)
        {
            waiter.Registration = ct.Register(() => Cancel(waiter, ct));
            if (waiter.Completion.Task.IsCompleted)
                waiter.Registration.Unregister();
        }
        return waiter.Completion.Task;
    }

    /// <summary>
    /// Call on HTTP 429, or 403 with a rate-limit reason. Empties the bucket and backs off 1 s, 2 s,
    /// 4 s ... up to 32 s, plus jitter. Reports during an ongoing backoff do not escalate it: they come
    /// from calls that were already in flight. Returns the time until calls resume.
    /// </summary>
    public TimeSpan ReportRateLimited()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            if (now < _backoffUntil)
                return _backoffUntil - now;

            var seconds = Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, _backoffAttempt));
            _backoffAttempt++;
            var delay = TimeSpan.FromSeconds(seconds) + TimeSpan.FromSeconds(_jitter());
            _backoffUntil = now + delay;
            _tokens = 0;
            _lastRefill = now;
            _logger.LogWarning("Rate limited by Google for {Account}; backing off {Seconds:F1} s", _name, delay.TotalSeconds);
            Pump();
            return delay;
        }
    }

    /// <summary>Call after a successful API call; resets the backoff sequence.</summary>
    public void ReportSuccess()
    {
        lock (_lock)
            _backoffAttempt = 0;
    }

    public void Dispose()
    {
        List<Waiter> orphans;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            orphans = [.. _interactive, .. _background];
            _interactive.Clear();
            _background.Clear();
        }
        foreach (var w in orphans)
            w.Completion.TrySetException(new ObjectDisposedException(nameof(QuotaLimiter)));
    }

    // Grants queued waiters in order while tokens last, then arms the timer for the next one.
    // Caller holds _lock.
    private void Pump()
    {
        if (_disposed)
            return;
        var now = _time.GetUtcNow();
        Refill(now);
        while (true)
        {
            var node = _interactive.First ?? _background.First;
            if (node is null)
                return;
            if (now < _backoffUntil)
            {
                Arm(_backoffUntil - now);
                return;
            }
            var waiter = node.Value;
            if (!HasTokens(waiter.Cost))
            {
                Arm(TimeSpan.FromSeconds((waiter.Cost - _tokens) / _perSecond));
                return;
            }
            node.List!.Remove(node);
            Spend(waiter.Cost, now);
            waiter.Registration.Unregister();
            waiter.Completion.TrySetResult();
        }
    }

    private void Cancel(Waiter waiter, CancellationToken ct)
    {
        lock (_lock)
        {
            if (waiter.Node.List is null)
                return; // already granted
            waiter.Node.List.Remove(waiter.Node);
            waiter.Completion.TrySetCanceled(ct);
            Pump(); // a cancelled head may have been blocking cheaper waiters behind it
        }
    }

    private void Arm(TimeSpan delay)
    {
        // Round up to whole milliseconds so the timer never fires just before the tokens are there.
        var due = TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(delay.TotalMilliseconds)));
        if (_timer is null)
            _timer = _time.CreateTimer(_ => { lock (_lock) Pump(); }, null, due, Timeout.InfiniteTimeSpan);
        else
            _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void Refill(DateTimeOffset now)
    {
        // Nothing accrues during a backoff.
        var from = _lastRefill > _backoffUntil ? _lastRefill : _backoffUntil;
        if (now > from)
            _tokens = Math.Min(_capacity, _tokens + (now - from).TotalSeconds * _perSecond);
        _lastRefill = now;
    }

    // Tolerates floating-point error from many small refills.
    private bool HasTokens(int cost) => _tokens >= cost - 1e-6;

    private void Spend(int cost, DateTimeOffset now)
    {
        _tokens = Math.Max(0, _tokens - cost);
        if (now - _windowStart >= TimeSpan.FromMinutes(1))
        {
            if (_windowUnits > 0)
                _logger.LogDebug("Quota for {Account}: {Units} units in the minute from {Start:O}", _name, _windowUnits, _windowStart);
            _windowStart = now;
            _windowUnits = 0;
        }
        _windowUnits += cost;
    }

    private sealed class Waiter(int cost)
    {
        public int Cost { get; } = cost;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LinkedListNode<Waiter> Node { get; set; } = null!;
        public CancellationTokenRegistration Registration { get; set; }
    }
}
