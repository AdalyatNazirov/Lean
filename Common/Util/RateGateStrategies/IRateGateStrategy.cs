using System;
using System.Threading;

namespace QuantConnect.Util.RateGateStrategies;

public interface IRateGateStrategy: IDisposable
{
    /// <summary>
    /// Waits for permission to proceed within the configured rate limit.
    /// Blocks until either the operation is permitted or the specified timeout elapses.
    /// </summary>
    /// <param name="millisecondsTimeout">The maximum time, in milliseconds, to wait for permission.</param>
    /// <param name="cancellationToken">The CancellationToken to observe.</param>
    /// <returns>True if permission was granted within the timeout; otherwise, false.</returns>
    bool Wait(int millisecondsTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for permission to proceed within the configured rate limit.
    /// Blocks until either the operation is permitted or the specified timeout elapses.
    /// </summary>
    /// <param name="tokens">The number of tokens required to proceed.</param>
    /// <param name="millisecondsTimeout">The maximum time, in milliseconds, to wait for permission.</param>
    /// <param name="cancellationToken">The CancellationToken to observe.</param>
    /// <returns>True if permission was granted within the timeout; otherwise, false.</returns>
    bool Wait(int tokens, int millisecondsTimeout, CancellationToken cancellationToken = default);

    void Release();

    bool TryPeekNextFireTick(out int nextFireTick);
}
