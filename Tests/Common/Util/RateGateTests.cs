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
 */

using System;
using NUnit.Framework;
using System.Diagnostics;
using System.Threading;
using Moq;
using QuantConnect.Util.RateGateStrategies;
using RateGateType = QuantConnect.Util.RateGate;

namespace QuantConnect.Tests.Common.Util
{
    [TestFixture, Parallelizable(ParallelScope.Fixtures)]
    public class RateGateTests
    {
        [Test]
        public void RateGateWithMockStrategy_CallsWait()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();
            mockStrategy.Setup(s => s.Wait(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(true);

            using var gate = new RateGateType(mockStrategy.Object);
            var result = gate.WaitToProceed(10, 100);

            Assert.IsTrue(result);
            mockStrategy.Verify(s => s.Wait(10, 100, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public void RateGateWithMockStrategy_IsRateLimited_CallsWaitWithZeroTimeout()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();
            mockStrategy.Setup(s => s.Wait(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(false);

            using var gate = new RateGateType(mockStrategy.Object);
            var isLimited = gate.IsRateLimited;

            Assert.IsTrue(isLimited);
            mockStrategy.Verify(s => s.Wait(1, 0, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Test]
        public void RateGateWithMockStrategy_Dispose_DisposesStrategy()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();

            using (new RateGateType(mockStrategy.Object))
            {
            }

            mockStrategy.Verify(s => s.Dispose(), Times.Once);
        }

        [Test]
        public void RateGate_KicksOffTimer_WhenWaitSucceedsAndFireTickAvailable()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();
            var nextFireTick = Environment.TickCount + 100;

            mockStrategy.Setup(s => s.Wait(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(true);
            mockStrategy.Setup(s => s.TryPeekNextFireTick(out nextFireTick))
                .Returns(true);

            using var gate = new RateGateType(mockStrategy.Object);
            gate.WaitToProceed(1, 100);

            mockStrategy.Verify(s => s.TryPeekNextFireTick(out nextFireTick), Times.Once);
        }

        [Test]
        public void RateGate_DoesNotKickOffTimer_WhenWaitFails()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();
            mockStrategy.Setup(s => s.Wait(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(false);

            using var gate = new RateGateType(mockStrategy.Object);
            gate.WaitToProceed(1, 100);

            mockStrategy.Verify(s => s.TryPeekNextFireTick(out It.Ref<int>.IsAny), Times.Never);
        }

        [Test]
        public void RateGate_DoesNotKickOffTimer_WhenRunning()
        {
            var mockStrategy = new Mock<IRateGateStrategy>();
            var nextFireTick = Environment.TickCount + 100;

            mockStrategy.Setup(s => s.Wait(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(true);
            mockStrategy.Setup(s => s.TryPeekNextFireTick(out nextFireTick))
                .Returns(true);

            using var gate = new RateGateType(mockStrategy.Object);
            gate.WaitToProceed(1, 100);
            mockStrategy.Verify(s => s.TryPeekNextFireTick(out nextFireTick), Times.Once);

            gate.WaitToProceed(1, 100);
            mockStrategy.Verify(s => s.TryPeekNextFireTick(out nextFireTick), Times.Once);
        }

        [TestCase(5)]
        [TestCase(10)]
        public void RateGateWithTimeout(int count)
        {
            TestRateLimit((action) =>
            {
                for (var i = 0; i <= count; i++)
                {
                    action.Invoke();
                }
            }, count);
        }

        [TestCase(5)]
        [TestCase(10)]
        public void RateGateWithTimeoutParallel(int count)
        {
            TestRateLimit((action) =>
            {
                var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 5 };
                System.Threading.Tasks.Parallel.For(0, count + 1, options, i =>
                {
                    action.Invoke();
                });
            }, count);
        }

        [Test]
        public void RateGate_ShouldSkipBecauseOfTimeout()
        {
            using var gate = new RateGateType(1, TimeSpan.FromSeconds(5));
            var timer = Stopwatch.StartNew();

            Assert.IsTrue(gate.WaitToProceed(-1));
            Assert.IsFalse(gate.WaitToProceed(0));
            Assert.IsTrue(gate.IsRateLimited);

            timer.Stop();

            Assert.LessOrEqual(timer.Elapsed, TimeSpan.FromSeconds(5));
            timer.Restart();
            Assert.IsTrue(gate.WaitToProceed(-1));
            timer.Stop();

            Assert.LessOrEqual(timer.Elapsed, TimeSpan.FromSeconds(10));
        }

        private static void TestRateLimit(Action<Action> waitAction, int count)
        {
            var rate = TimeSpan.FromMilliseconds(500);
            using var gate = new RateGateType(1, rate);
            var timer = Stopwatch.StartNew();

            waitAction.Invoke(() =>
            {
                Assert.IsTrue(gate.WaitToProceed(TimeSpan.FromSeconds(5)));
            });

            timer.Stop();

            var elapsed = timer.Elapsed;
            var expectedDelay = rate * count;
            var lowerBound = expectedDelay - expectedDelay * 0.30;
            var upperBound = expectedDelay + expectedDelay * 0.30;

            Assert.GreaterOrEqual(elapsed, lowerBound, $"RateGate was early: {lowerBound - elapsed}");
            Assert.LessOrEqual(elapsed, upperBound, $"RateGate was late: {elapsed - upperBound}");
        }


    }
}
