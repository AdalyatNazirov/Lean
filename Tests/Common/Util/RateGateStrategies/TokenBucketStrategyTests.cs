using System;
using System.Threading;
using NUnit.Framework;
using QuantConnect.Util.RateGateStrategies;

namespace QuantConnect.Tests.Common.Util.RateGateStrategies;

[TestFixture]
public class TokenBucketStrategyTests
{
    private const int DecayTimeoutMilliseconds = 100;

    [Test]
    public void Wait_DecrementsSemaphoreAndSetsNextDecayTick()
    {
        using var strategy = new TokenBucketStrategy(1, 1m, DecayTimeoutMilliseconds);
        var startTime = Environment.TickCount;

        var result = strategy.Wait(1, 0);

        Assert.IsTrue(result);
        Assert.IsTrue(strategy.TryPeekNextFireTick(out var nextFireTick));
        Assert.GreaterOrEqual(unchecked(nextFireTick - (startTime + DecayTimeoutMilliseconds)), 0);
    }

    [Test]
    public void Wait_ReturnsFalse_WhenCapacityIsReached()
    {
        using var strategy = new TokenBucketStrategy(1, 1m, DecayTimeoutMilliseconds);
        strategy.Wait(1, 0);

        var result = strategy.Wait(1, 10);

        Assert.IsFalse(result);
    }

    [Test]
    public void Release_IncrementsSemaphore_WhenFullTokenDecayed()
    {
        using var strategy = new TokenBucketStrategy(1, 1m, DecayTimeoutMilliseconds);
        strategy.Wait(1, 0);
        Assert.IsTrue(strategy.TryPeekNextFireTick(out _));

        strategy.Release();

        Assert.IsFalse(strategy.TryPeekNextFireTick(out _));
        Assert.IsTrue(strategy.Wait(1, 0));
    }

    [Test]
    public void Release_FractionalRefill()
    {
        // 0.5 tokens refilled every 100ms. Needs 2 releases to refill 1 token.
        using var strategy = new TokenBucketStrategy(1, 0.5m, DecayTimeoutMilliseconds);
        strategy.Wait(1, 0);

        // 1st release
        strategy.Release();
        Assert.IsTrue(strategy.TryPeekNextFireTick(out _), "Should still have counter > 0");
        Assert.IsFalse(strategy.Wait(1, 0), "Should NOT have refilled a full token yet");

        // 2nd release
        strategy.Release();
        Assert.IsFalse(strategy.TryPeekNextFireTick(out _), "Counter should be 0 now");
        Assert.IsTrue(strategy.Wait(1, 0), "Should have refilled 1 token");
    }

    [Test]
    public void TryPeekNextFireTick_IsStable()
    {
        using var strategy = new TokenBucketStrategy(1, 1m, DecayTimeoutMilliseconds);
        strategy.Wait(1, 0);

        strategy.TryPeekNextFireTick(out var firstPeek);
        Thread.Sleep(10);
        strategy.TryPeekNextFireTick(out var secondPeek);

        Assert.AreEqual(firstPeek, secondPeek);
    }
}
