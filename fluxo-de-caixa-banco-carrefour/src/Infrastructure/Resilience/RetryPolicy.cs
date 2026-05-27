using System;
using System.Threading.Tasks;

namespace FluxoDeCaixa.Infrastructure.Resilience
{
    public static class RetryPolicy
    {
        public static async Task ExecuteAsync(Func<Task> action, int maxAttempts = 3, TimeSpan? delay = null)
        {
            delay ??= TimeSpan.FromMilliseconds(500);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await action();
                    return;
                }
                catch when (attempt < maxAttempts)
                {
                    await Task.Delay(delay.Value);
                }
            }

            throw new InvalidOperationException($"RetryPolicy failed after {maxAttempts} attempts.");
        }
    }
}
