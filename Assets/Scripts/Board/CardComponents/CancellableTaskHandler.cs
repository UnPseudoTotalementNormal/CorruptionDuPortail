#region

using System;
using System.Threading;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Utility to manage cancellable tasks cleanly.
    /// Encapsulates CancellationTokenSource logic.
    /// </summary>
    public class CancellableTaskHandler : IDisposable
    {
        private CancellationTokenSource currentCts;
        private bool disposed;

        /// <summary>
        /// Cancels the previous task and returns a new CancellationToken.
        /// </summary>
        public CancellationToken GetNewToken()
        {
            Cancel();
            currentCts = new CancellationTokenSource();
            return currentCts.Token;
        }

        /// <summary>
        /// Cancels the current task if it exists.
        /// </summary>
        public void Cancel()
        {
            currentCts?.Cancel();
            currentCts?.Dispose();
            currentCts = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            
            Cancel();
            disposed = true;
        }
    }
}

