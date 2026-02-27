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

namespace QuantConnect.Util.RateLimit;

public class TokenBucketStrategy : IRateGateStrategy
{
    private readonly int _maxThreshold;
    private readonly decimal _decayVelocity;
    private readonly int _decayTimeoutMilliseconds;
    private decimal _counter = 0;
    private int _nextDecayTick;
    private readonly object _lock = new();

    public TokenBucketStrategy(int maxThreshold, decimal decayVelocity, int decayTimeoutMilliseconds)
    {
        _maxThreshold = maxThreshold;
        _decayVelocity = decayVelocity;
        _decayTimeoutMilliseconds = decayTimeoutMilliseconds;
    }

    public bool Wait(long tokens, int millisecondsTimeout, CancellationToken cancellationToken = default)
    {
        var startTime = Environment.TickCount;
        lock (_lock)
        {
            while (_counter + tokens > _maxThreshold)
            {
                var remainingTimeout = millisecondsTimeout == Timeout.Infinite
                    ? Timeout.Infinite
                    : Math.Max(0, millisecondsTimeout - unchecked(Environment.TickCount - startTime));

                if (remainingTimeout == 0 || !Monitor.Wait(_lock, remainingTimeout))
                {
                    return false;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            }

            if (_counter == 0)
            {
                _nextDecayTick = unchecked(Environment.TickCount + _decayTimeoutMilliseconds);
            }
            _counter += tokens;

            return true;
        }
    }

    /// <summary>
    /// Waits for a specified duration or until the required tokens are available in the rate-limiting strategy.
    /// Ensures the operation observes the provided timeout and supports cancellation via a cancellation token.
    /// </summary>
    /// <param name="millisecondsTimeout">The amount of time, in milliseconds, to wait for the tokens to be available before timing out.</param>
    /// <returns>Returns true if the wait is successful within the allowed timeout; otherwise, returns false.</returns>
    public bool Wait(int millisecondsTimeout) => Wait(1, millisecondsTimeout);

    /// <summary>
    /// Reduces the accumulated tokens in the token bucket by the decay velocity.
    /// If the current token count is greater than zero, it decreases the count by the decay velocity.
    /// Ensures that the token count never drops below zero.
    /// Updates the next decay tick to prepare for subsequent decay operations.
    /// Notifies any threads waiting for a release event.
    /// </summary>
    public void Release()
    {
        lock (_lock)
        {
            if (_counter > 0)
            {
                _counter -= _decayVelocity;
                if (_counter < 0)
                {
                    _counter = 0;
                }

                _nextDecayTick = unchecked(_nextDecayTick + _decayTimeoutMilliseconds);
                Monitor.PulseAll(_lock);
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve the next decay tick without modifying the state of the rate-limiting strategy.
    /// </summary>
    /// <param name="nextFireTick">When this method returns, contains the next tick time (in milliseconds) at which a token will decay if available, or 0 if no tokens are queued.</param>
    /// <returns>Returns true if a next decay tick is available; otherwise, returns false.</returns>
    public bool TryPeekNextFireTick(out int nextFireTick)
    {
        lock (_lock)
        {
            if (_counter > 0)
            {
                nextFireTick = _nextDecayTick;
                return true;
            }
        }

        nextFireTick = 0;
        return false;
    }

    public void Dispose()
    {
    }
}
