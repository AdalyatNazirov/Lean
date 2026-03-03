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
using System.Collections.Generic;
using System.Threading;

namespace QuantConnect.Util.RateGateStrategies;

/// <summary>
/// Implements a rate-limiting mechanism using a sliding time window approach.
/// This class enforces a limit on the number of operations that can be executed
/// within a specified time interval, leveraging synchronization techniques such as semaphores.
/// </summary>
public class SlidingWindowLogStrategy : IRateGateStrategy
{
    private readonly int _timeUnitMilliseconds;

    // Semaphore used to count and limit the number of occurrences per
    // unit time.
    private readonly SemaphoreSlim _semaphore;
    private bool _isDisposed;
    private readonly Queue<int> _exitTimes = new();

    /// <summary>
    /// Implements a rate-limiting strategy using a sliding window of time
    /// with semaphore to control the number of allowed occurrences.
    /// </summary>
    public SlidingWindowLogStrategy(int initialCount, int maxCount, int timeUnitMilliseconds)
    {
        _timeUnitMilliseconds = timeUnitMilliseconds;
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
    }

    /// <summary>
    /// Attempts to enter the semaphore by decrementing its count, waiting up to the specified timeout.
    /// </summary>
    /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
    /// <param name="cancellationToken">The CancellationToken to observe.</param>
    /// <returns>True if the semaphore count was decremented successfully within the timeout period; otherwise, false.</returns>
    public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken = default)
    {
        var entered = _semaphore.Wait(millisecondsTimeout, cancellationToken);
        // If we entered the semaphore, compute the corresponding exit time
        // and add it to the queue.
        if (entered)
        {
            var timeToExit = unchecked(Environment.TickCount + _timeUnitMilliseconds);
            lock (_exitTimes)
            {
                _exitTimes.Enqueue(timeToExit);
            }
        }

        return entered;
    }

    /// <summary>
    /// Not supported for the Sliding Window Log Strategy.
    /// </summary>
    /// <param name="tokens">The number of tokens required to proceed.</param>
    /// <param name="millisecondsTimeout">The maximum time, in milliseconds, to wait for permission.</param>
    /// <returns>True if permission was granted within the timeout; otherwise, false.</returns>
    public bool Wait(int tokens, int millisecondsTimeout, CancellationToken cancellationToken = default)
    {
        if (tokens != 1)
        {
            throw new NotSupportedException();
        }

        return Wait(millisecondsTimeout, cancellationToken);
    }

    /// <summary>
    /// Releases a single counting semaphore entry, increasing the count of the semaphore by one
    /// and allowing another thread to gain access to a limited resource.
    /// </summary>
    public void Release()
    {
        lock (_exitTimes)
        {
            _semaphore.Release();
            _exitTimes.Dequeue();
        }
    }

    /// <summary>
    /// Attempts to retrieve the next scheduled fire tick without removing it from the queue.
    /// </summary>
    /// <param name="fireTick">When this method returns, contains the next scheduled fire tick if available; otherwise, an undefined value.</param>
    /// <returns>True if a fire tick is available; otherwise, false.</returns>
    public bool TryPeekNextFireTick(out int fireTick)
    {
        lock (_exitTimes)
        {
            return _exitTimes.TryPeek(out fireTick);
        }
    }

    /// <summary>
    /// Releases unmanaged resources held by an instance of this class.
    /// </summary>
    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
