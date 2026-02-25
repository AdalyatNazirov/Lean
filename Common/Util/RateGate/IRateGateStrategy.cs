using System;

namespace QuantConnect.Util.RateLimit;

public interface IRateGateStrategy: IDisposable
{
    bool Wait(int millisecondsTimeout);
    void Release();
    bool TryPeekNextFireTick(out int nextFireTick);
}
