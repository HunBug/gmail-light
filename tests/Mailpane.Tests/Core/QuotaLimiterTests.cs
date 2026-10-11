using Mailpane.Core.Gmail;
using Mailpane.Core.Quota;
using Microsoft.Extensions.Time.Testing;

namespace Mailpane.Tests.Core;

public sealed class QuotaLimiterTests
{
    // 60 units per minute = 1 unit per second, which keeps the arithmetic readable.
    private readonly FakeTimeProvider _time = new();

    private QuotaLimiter Create(int unitsPerMinute = 60) => new("test", unitsPerMinute, _time, jitter: () => 0);

    private static Task Interactive(QuotaLimiter l, int cost, CancellationToken ct = default) =>
        l.AcquireAsync(cost, CallPriority.Interactive, ct);

    private static Task Background(QuotaLimiter l, int cost) => l.AcquireAsync(cost, CallPriority.Background);

    [Fact]
    public void Starts_full_and_grants_immediately()
    {
        using var limiter = Create();
        Assert.True(Interactive(limiter, 60).IsCompletedSuccessfully);
    }

    [Fact]
    public void Waits_for_refill_when_empty()
    {
        using var limiter = Create();
        Interactive(limiter, 60);

        var next = Interactive(limiter, 10);
        Assert.False(next.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(9.9));
        Assert.False(next.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True(next.IsCompletedSuccessfully);
    }

    [Fact]
    public void Budget_is_respected_over_time()
    {
        using var limiter = Create(4500);
        int granted;
        var pending = new List<Task>();
        for (var i = 0; i < 400; i++)
        {
            pending.Add(Background(limiter, QuotaCost.ThreadsGet));
        }

        // Full bucket (4,500) plus one minute of refill (4,500): at most 9,000 units, 225 calls.
        _time.Advance(TimeSpan.FromMinutes(1));
        granted = pending.Count(t => t.IsCompletedSuccessfully);
        Assert.InRange(granted * QuotaCost.ThreadsGet, 8960, 9000);

        // And the rate holds afterwards: another minute, another 4,500 units at most.
        _time.Advance(TimeSpan.FromMinutes(1));
        var granted2 = pending.Count(t => t.IsCompletedSuccessfully) - granted;
        Assert.InRange(granted2 * QuotaCost.ThreadsGet, 4480, 4520);
    }

    [Fact]
    public void Interactive_goes_before_queued_background()
    {
        using var limiter = Create();
        Interactive(limiter, 60);

        var background = Background(limiter, 10);
        var interactive = Interactive(limiter, 10);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(interactive.IsCompletedSuccessfully);
        Assert.False(background.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(background.IsCompletedSuccessfully);
    }

    [Fact]
    public void Background_waits_while_interactive_is_queued_even_if_cheaper()
    {
        using var limiter = Create();
        Interactive(limiter, 55); // 5 left

        var interactive = Interactive(limiter, 40);
        var background = Background(limiter, 1);
        Assert.False(background.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(35));
        Assert.True(interactive.IsCompletedSuccessfully);
        Assert.False(background.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(background.IsCompletedSuccessfully);
    }

    [Fact]
    public void New_background_call_does_not_jump_the_background_queue()
    {
        using var limiter = Create();
        Interactive(limiter, 55); // 5 left

        var first = Background(limiter, 40);
        var second = Background(limiter, 1);
        Assert.False(second.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(35));
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void Interactive_jumps_ahead_of_background_when_tokens_are_there()
    {
        using var limiter = Create();
        Interactive(limiter, 55); // 5 left

        var background = Background(limiter, 40);
        var interactive = Interactive(limiter, 5);

        Assert.True(interactive.IsCompletedSuccessfully);
        Assert.False(background.IsCompleted);
    }

    [Fact]
    public void Cancelled_waiter_leaves_the_queue_and_unblocks_the_next()
    {
        using var limiter = Create();
        Interactive(limiter, 55); // 5 left

        using var cts = new CancellationTokenSource();
        var expensive = Interactive(limiter, 40, cts.Token);
        var cheap = Interactive(limiter, 5);
        Assert.False(cheap.IsCompleted);

        cts.Cancel();
        Assert.True(expensive.IsCanceled);
        Assert.True(cheap.IsCompletedSuccessfully);
    }

    [Fact]
    public void Already_cancelled_token_returns_cancelled_task()
    {
        using var limiter = Create();
        Assert.True(Interactive(limiter, 1, new CancellationToken(canceled: true)).IsCanceled);
    }

    [Fact]
    public void Rate_limit_empties_the_bucket_and_blocks_for_the_backoff()
    {
        using var limiter = Create();
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.ReportRateLimited());

        var call = Interactive(limiter, 1);
        _time.Advance(TimeSpan.FromSeconds(1)); // backoff over, but the bucket starts from zero
        Assert.False(call.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(call.IsCompletedSuccessfully);
    }

    [Fact]
    public void Backoff_doubles_up_to_32_seconds_and_resets_on_success()
    {
        using var limiter = Create();
        var delays = new List<double>();
        for (var i = 0; i < 8; i++)
        {
            var d = limiter.ReportRateLimited();
            delays.Add(d.TotalSeconds);
            _time.Advance(d);
        }
        Assert.Equal([1, 2, 4, 8, 16, 32, 32, 32], delays);

        limiter.ReportSuccess();
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.ReportRateLimited());
    }

    [Fact]
    public void Reports_during_a_backoff_do_not_escalate_it()
    {
        using var limiter = Create();
        limiter.ReportRateLimited(); // 1 s
        _time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(0.5), limiter.ReportRateLimited());

        _time.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(2), limiter.ReportRateLimited());
    }

    [Fact]
    public void Jitter_adds_up_to_one_second()
    {
        using var limiter = new QuotaLimiter("test", 60, _time, jitter: () => 0.75);
        Assert.Equal(TimeSpan.FromSeconds(1.75), limiter.ReportRateLimited());
    }

    [Fact]
    public void Rejects_costs_outside_the_bucket()
    {
        using var limiter = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Interactive(limiter, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = Interactive(limiter, 61); });
    }

    [Fact]
    public void Dispose_fails_waiters()
    {
        var limiter = Create();
        Interactive(limiter, 60);
        var waiting = Interactive(limiter, 1);
        limiter.Dispose();
        Assert.IsType<ObjectDisposedException>(waiting.Exception?.InnerException);
    }
}
