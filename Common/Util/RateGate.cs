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
using System.Threading;
using QuantConnect.Util.RateGateStrategies;

namespace QuantConnect.Util
{
    /// <summary>
    /// Used to control the rate of some occurrence per unit of time.
    /// </summary>
    /// <see href="http://www.jackleitch.net/2010/10/better-rate-limiting-with-dot-net/"/>
    /// <remarks>
    ///     <para>
    ///     To control the rate of an action using a <see cref="RateGate"/>,
    ///     code should simply call <see cref="WaitToProceed()"/> prior to
    ///     performing the action. <see cref="WaitToProceed()"/> will block
    ///     the current thread until the action is allowed based on the rate
    ///     limit.
    ///     </para>
    ///     <para>
    ///     This class is thread safe. A single <see cref="RateGate"/> instance
    ///     may be used to control the rate of an occurrence across multiple
    ///     threads.
    ///     </para>
    /// </remarks>
    public class RateGate : IDisposable
    {
        private readonly IRateGateStrategy _rateGateStrategy;

        // Timer used to trigger exiting the semaphore.
        private bool _isTimerRunning;
        private readonly Timer _exitTimer;
        private Lock _timerLock = new();

        // Whether this instance is disposed.
        private bool _isDisposed;

        /// <summary>
        /// Number of occurrences allowed per unit of time.
        /// </summary>
        public int Occurrences
        {
            get; private set;
        }

        /// <summary>
        /// The length of the time unit, in milliseconds.
        /// </summary>
        public int TimeUnitMilliseconds
        {
            get; private set;
        }

        /// <summary>
        /// Flag indicating we are currently being rate limited
        /// </summary>
        public bool IsRateLimited
        {
            get { return !WaitToProceed(0); }
        }

        /// <summary>
        /// Initializes a <see cref="RateGate"/> with a rate of <paramref name="occurrences"/>
        /// per <paramref name="timeUnit"/>.
        /// </summary>
        /// <param name="occurrences">Number of occurrences allowed per unit of time.</param>
        /// <param name="timeUnit">Length of the time unit.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// If <paramref name="occurrences"/> or <paramref name="timeUnit"/> is negative.
        /// </exception>
        public RateGate(int occurrences, TimeSpan timeUnit)
        {
            // Check the arguments.
            if (occurrences <= 0)
                throw new ArgumentOutOfRangeException(nameof(occurrences), "Number of occurrences must be a positive integer");
            if (timeUnit != timeUnit.Duration())
                throw new ArgumentOutOfRangeException(nameof(timeUnit), "Time unit must be a positive span of time");
            if (timeUnit >= TimeSpan.FromMilliseconds(UInt32.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(timeUnit), "Time unit must be less than 2^32 milliseconds");

            Occurrences = occurrences;
            TimeUnitMilliseconds = (int)timeUnit.TotalMilliseconds;

            // Create the semaphore, with the number of occurrences as the maximum count.
            _rateGateStrategy = new SlidingWindowLogStrategy(Occurrences, Occurrences, TimeUnitMilliseconds);

            // Create a timer to exit the semaphore. Use the time unit as the original
            // interval length because that's the earliest we will need to exit the semaphore.
            _exitTimer = new Timer(ExitTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Initializes a <see cref="RateGate"/> with a custom rate gate strategy.
        /// </summary>
        /// <param name="strategy">The custom rate gate strategy to use.</param>
        public RateGate(IRateGateStrategy strategy)
        {
            _rateGateStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            _exitTimer = new Timer(ExitTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        // Callback for the exit timer that exits the semaphore based on exit times
        // in the queue and then sets the timer for the nextexit time.
        // Credit to Jim: http://www.jackleitch.net/2010/10/better-rate-limiting-with-dot-net/#comment-3620
        // for providing the code below, fixing issue #3499 - https://github.com/QuantConnect/Lean/issues/3499
        private void ExitTimerCallback(object state)
        {
            try
            {
                // While there are exit times that are passed due still in the queue,
                // exit the semaphore and dequeue the exit time.
                var exitTime = 0;
                var exitTimeValid = false;
                var tickCount = Environment.TickCount;
                lock (_timerLock)
                {
                    exitTimeValid = _rateGateStrategy.TryPeekNextFireTick(out exitTime);
                    while (exitTimeValid)
                    {
                        if (unchecked(exitTime - tickCount) > 0)
                        {
                            break;
                        }

                        _rateGateStrategy.Release();
                        exitTimeValid = _rateGateStrategy.TryPeekNextFireTick(out exitTime);
                    }

                    // only schedule if there's someone waiting
                    if (exitTimeValid)
                    {
                        // we are already holding the next item from the queue, do not peek again
                        // although this exit time may have already pass by this stmt.
                        var maxWait = TimeUnitMilliseconds > 0 ? TimeUnitMilliseconds : int.MaxValue;
                        var timeUntilNextCheck = Math.Min(maxWait, Math.Max(0, exitTime - tickCount));

                        _exitTimer.Change(timeUntilNextCheck, Timeout.Infinite);
                    }
                    else
                    {
                        // The queue is empty, mark the timer as dormant so WaitToProceed knows to kickstart it next time
                        _isTimerRunning = false;
                    }
                }
            }
            catch (Exception)
            {
                // can throw if called when disposing
            }
        }

        /// <summary>
        /// Blocks the current thread until allowed to proceed or until the
        /// specified timeout elapses.
        /// </summary>
        /// <param name="tokens">The number of tokens required to proceed.</param>
        /// <param name="millisecondsTimeout">Number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <param name="cancellationToken">The CancellationToken to observe.</param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public bool WaitToProceed(int tokens, int millisecondsTimeout, CancellationToken cancellationToken = default)
        {
            // Check the arguments.
            if (millisecondsTimeout < -1)
                throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

            CheckDisposed();

            // Block until we can enter the semaphore or timeout expires.
            // The strategy handles its internal queue and returns the timeToExit.
            var entered = _rateGateStrategy.Wait(tokens, millisecondsTimeout, cancellationToken);
            if (entered)
            {
                lock (_timerLock)
                {
                    // Only kickstart the timer if it's currently dormant
                    if (!_isTimerRunning && _rateGateStrategy.TryPeekNextFireTick(out int nextFireTick))
                    {
                        var timeUntilNextCheck = Math.Max(0, unchecked(nextFireTick - Environment.TickCount));
                        _exitTimer.Change(timeUntilNextCheck, Timeout.Infinite);
                        _isTimerRunning = true; // Mark as running so subsequent calls ignore it
                    }
                }
            }

            return entered;
        }

        /// <summary>
        /// Blocks the current thread until allowed to proceed or until the
        /// specified timeout elapses.
        /// </summary>
        /// <param name="millisecondsTimeout">Number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public bool WaitToProceed(int millisecondsTimeout)
        {
            return WaitToProceed(1, millisecondsTimeout);
        }

        /// <summary>
        /// Blocks the current thread until allowed to proceed or until the
        /// specified timeout elapses.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns>true if the thread is allowed to proceed, or false if timed out</returns>
        public bool WaitToProceed(TimeSpan timeout)
        {
            return WaitToProceed((int)timeout.TotalMilliseconds);
        }

        /// <summary>
        /// Blocks the current thread indefinitely until allowed to proceed.
        /// </summary>
        public void WaitToProceed()
        {
            WaitToProceed(Timeout.Infinite);
        }

        // Throws an ObjectDisposedException if this object is disposed.
        private void CheckDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException("RateGate is already disposed");
        }

        /// <summary>
        /// Releases unmanaged resources held by an instance of this class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged resources held by an instance of this class.
        /// </summary>
        /// <param name="isDisposing">Whether this object is being disposed.</param>
        protected virtual void Dispose(bool isDisposing)
        {
            if (!_isDisposed)
            {
                if (isDisposing)
                {
                    // The semaphore and timer both implement IDisposable and
                    // therefore must be disposed.
                    _rateGateStrategy.Dispose();
                    _exitTimer.Dispose();

                    _isDisposed = true;
                }
            }
        }
    }
}
