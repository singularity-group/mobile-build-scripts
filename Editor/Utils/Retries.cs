using System;
using System.Threading;
using System.Threading.Tasks;

namespace Commons.Editor {
    public static class Retries {
        private const int retryDelay = 2000;

        public static void DoActionWithRetry(Action action, int maxAttempts, double delayBetweenAttemptsMilliseconds = retryDelay) {
            DoActionWithRetry(() => {

                action();
                return Task.CompletedTask;

            }, maxAttempts, delayBetweenAttemptsMilliseconds).Wait();
        }

        public static async Task DoActionWithRetry(Func<Task> action, int maxAttempts, double delayBetweenAttemptsMilliseconds = retryDelay, CancellationToken cancelToken = default, Func<Exception, int, bool> shouldThrow = null) {
            await DoActionWithRetry(async () => {

                await action();
                return 0;

            }, maxAttempts, delayBetweenAttemptsMilliseconds, cancelToken, shouldThrow);
        }

        public static T DoActionWithRetry<T>(Func<T> action, int maxAttempts, double delayBetweenAttemptsMilliseconds = retryDelay, CancellationToken cancelToken = default, Func<Exception, int, bool> shouldThrow = null) {
#pragma warning disable 1998 // async without await
            return DoActionWithRetry(async () => {
                return action();
            }, maxAttempts, delayBetweenAttemptsMilliseconds, cancelToken, shouldThrow).Result;
#pragma warning restore 1998 // async without await
        }

        public static async Task<T> DoActionWithRetry<T>(Func<Task<T>> action, int maxAttempts, double delayBetweenAttemptsMilliseconds = retryDelay, CancellationToken cancelToken = default, Func<Exception, int, bool> shouldThrow = null) {
            Exception lastEx = null;

            for (int attempt = 0; attempt < maxAttempts; attempt++) {
                try {
                    return await action();
                } catch (Exception ex) {
                    UnityEngine.Debug.Log($"Attempt {attempt + 1}/{maxAttempts} failed.");
                    lastEx = ex;
                    if (shouldThrow != null && shouldThrow(ex, attempt))
                        throw;
                }

                if (cancelToken != default && cancelToken.IsCancellationRequested) {
                    throw new Exception($"cancellation requested after attempt {attempt}");
                }
                if (attempt != maxAttempts - 1) {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayBetweenAttemptsMilliseconds), cancelToken);
                }
            }
            throw new Exception($"stopped retrying after {maxAttempts} attempts", lastEx);
        }
    }
}
