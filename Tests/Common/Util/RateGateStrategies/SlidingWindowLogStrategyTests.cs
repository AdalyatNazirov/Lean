/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 */

using System;
using NUnit.Framework;
using QuantConnect.Util.RateGateStrategies;

namespace QuantConnect.Tests.Common.Util.RateGateStrategies;

[TestFixture, Parallelizable(ParallelScope.Fixtures)]
public class SlidingWindowLogStrategyTests
{
    private const int TimeUnitMilliseconds = 100;

    [Test]
    public void Wait_DecrementsSemaphoreAndEnqueuesExitTime()
    {
        using var strategy = new SlidingWindowLogStrategy(1, 1, TimeUnitMilliseconds);
        var startTime = Environment.TickCount;

        var result = strategy.Wait(1, 0);

        Assert.IsTrue(result);
        Assert.IsTrue(strategy.TryPeekNextFireTick(out var nextFireTick));
        Assert.GreaterOrEqual(nextFireTick, startTime + TimeUnitMilliseconds);
    }

    [Test]
    public void Wait_ReturnsFalse_WhenCapacityIsReached()
    {
        using var strategy = new SlidingWindowLogStrategy(1, 1, TimeUnitMilliseconds);
        strategy.Wait(1, 0);

        var result = strategy.Wait(1, 10);

        Assert.IsFalse(result);
    }

    [Test]
    public void Release_IncrementsSemaphoreAndDequeuesExitTime()
    {
        using var strategy = new SlidingWindowLogStrategy(1, 1, TimeUnitMilliseconds);
        strategy.Wait(1, 0);
        Assert.IsTrue(strategy.TryPeekNextFireTick(out _));

        strategy.Release();

        Assert.IsFalse(strategy.TryPeekNextFireTick(out _));
        Assert.IsTrue(strategy.Wait(1, 0));
    }

    [Test]
    public void TryPeekNextFireTick_ReturnsTrueAndCorrectTick()
    {
        using var strategy = new SlidingWindowLogStrategy(2, 2, TimeUnitMilliseconds);
        var startTime = Environment.TickCount;
        strategy.Wait(1, 0);
        var expectedTick = startTime + TimeUnitMilliseconds;

        var result = strategy.TryPeekNextFireTick(out var nextFireTick);

        Assert.IsTrue(result);
        Assert.GreaterOrEqual(nextFireTick, expectedTick);
        // Ensure it doesn't change on second wait
        strategy.Wait(1, 0);
        strategy.TryPeekNextFireTick(out var secondPeek);
        Assert.AreEqual(nextFireTick, secondPeek);
    }

    [Test]
    public void TryPeekNextFireTick_ReturnsFalse_WhenQueueIsEmpty()
    {
        using var strategy = new SlidingWindowLogStrategy(1, 1, TimeUnitMilliseconds);

        var result = strategy.TryPeekNextFireTick(out _);

        Assert.IsFalse(result);
    }

    [Test]
    public void Dispose_DisposesSemaphore()
    {
        var strategy = new SlidingWindowLogStrategy(1, 1, TimeUnitMilliseconds);
        strategy.Dispose();

        Assert.Throws<ObjectDisposedException>(() => strategy.Wait(1, 0));
    }

    [Test]
    public void MultipleWaitAndRelease()
    {
        const int maxCount = 5;
        using var strategy = new SlidingWindowLogStrategy(maxCount, maxCount, TimeUnitMilliseconds);

        for (int i = 0; i < maxCount; i++)
        {
            Assert.IsTrue(strategy.Wait(1, 0), $"Failed to wait at index {i}");
        }

        Assert.IsFalse(strategy.Wait(1, 0), "Should have been limited");

        strategy.Release();
        Assert.IsTrue(strategy.Wait(1, 0), "Should have been able to wait after release");
        Assert.IsFalse(strategy.Wait(1, 0), "Should have been limited again");
    }
}
