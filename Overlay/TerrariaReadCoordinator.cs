using System;
using System.Threading;

namespace TerraWikiOverlay;

// One attachment at a time: navigation must not race a state/world scan.
internal static class TerrariaReadCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IDisposable Enter(CancellationToken token = default, int timeoutMilliseconds = 5000)
    {
        if (!Gate.Wait(timeoutMilliseconds, token))
            throw new InvalidOperationException("游戏读取器正在处理另一个请求，请稍后重试。");
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Gate.Release();
        }
    }
}
