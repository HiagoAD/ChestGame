using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Collapses many saves of the same state into at most one write per window. Hand it state
    /// whenever that state changes, and it writes once the window elapses instead of once per call.
    /// </summary>
    /// <remarks>
    /// One key and one state per instance, both fixed at construction; use one instance per thing
    /// being saved. Dispose it when that thing goes away: disposal makes a last attempt to write
    /// anything still pending.
    /// Not thread safe, and holds no lock. Every member must be called from the same thread, which
    /// for a Unity game means the main thread.
    /// See docs/saving.md, "Write coalescing, and why it cannot live inside SaveAsync".
    /// </remarks>
    public class SaveScheduler<T> : IDisposable, ISaveFlushable where T : class
    {
        /// <summary>
        /// How long changes are collected before a write. Longer coalesces more and risks losing
        /// more to a crash; pass your own to the constructor to choose differently.
        /// </summary>
        public const int DefaultCoalesceWindowMilliseconds = 1000;

        private readonly ISaveService _saveService;
        private readonly string _key;
        private readonly IGameClock _clock;
        private readonly int _coalesceWindowMilliseconds;
        private readonly CancellationTokenSource _disposedCts = new();

        private T _pending;
        private bool _hasPending;
        private bool _waiting;
        private CancellationTokenSource _windowCts;
        private UniTaskCompletionSource _activeFlush;
        private bool _disposed;

        /// <summary>
        /// State neither durably saved nor currently being saved.
        /// </summary>
        public bool HasPendingWrite => _hasPending;

        /// <summary>
        /// A write is actually running, as opposed to a window merely counting down towards one.
        /// </summary>
        public bool IsFlushing => _activeFlush != null;

        /// <summary>
        /// Whether <see cref="FlushBlocking"/> can ever succeed here. Readable before anything is
        /// pending.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        public bool CanFlushBlocking => _saveService.CompletesOnCallingThread;

        public string SaveKey => _key;

        public SaveScheduler(ISaveService saveService, string key, IGameClock clock,
            int coalesceWindowMilliseconds = DefaultCoalesceWindowMilliseconds)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            SaveKeyPath.EnsurePresent(key);
            if (clock == null) throw SaveException.NoClock();
            if (coalesceWindowMilliseconds <= 0) throw SaveException.CoalesceWindowNotPositive(coalesceWindowMilliseconds);

            _saveService = saveService;
            _key = key;
            _clock = clock;
            _coalesceWindowMilliseconds = coalesceWindowMilliseconds;
        }

        /// <summary>
        /// Records state to be written when the window elapses, replacing anything recorded earlier.
        /// Nothing reads state until then, so the caller may keep mutating the same instance.
        /// Returns immediately; it is not a promise that anything has been written yet.
        /// </summary>
        public void MarkDirty(T state)
        {
            ThrowIfDisposed();

            _pending = state;
            _hasPending = true;

            ScheduleWindowIfNeeded();
        }

        /// <summary>
        /// Writes now instead of waiting for the window, and returns once everything pending is
        /// durable - including anything that becomes pending while this is waiting. Does nothing if
        /// nothing is pending.
        /// </summary>
        /// <param name="ct">
        /// Cancelling abandons this call's wait, never the write itself. A write already running
        /// when this is called runs to completion either way.
        /// </param>
        /// <exception cref="SaveException">When this scheduler has been disposed, or the save itself fails.</exception>
        public async UniTask FlushAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            InterruptWindow();

            if (!_hasPending && _activeFlush == null) return;

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposedCts.Token);

            await EnsureFlushingAsync(linked.Token);
        }

        /// <summary>
        /// <see cref="FlushAsync"/> for a caller with no time left to await anything, such as an
        /// application pause or quit callback. Writes on the spot, or throws; it never waits. Check
        /// <see cref="CanFlushBlocking"/> once at construction to know which of those a given
        /// composition gets.
        /// </summary>
        /// <exception cref="SaveException">
        /// When this scheduler has been disposed, when a write is already running, when the pending
        /// write turns out to need more than the calling thread, or when the save itself fails.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        public void FlushBlocking()
        {
            ThrowIfDisposed();

            InterruptWindow();

            if (_activeFlush != null) throw SaveException.FlushWouldBlock(_key);

            if (!_hasPending) return;

            FlushSynchronousCore();
        }

        /// <summary>
        /// Stops the window, cancels any write in flight, and makes one synchronous attempt to save
        /// what is pending. That attempt is best effort: if it cannot happen synchronously the write
        /// is lost and logged, and this method does not throw. Call <see cref="FlushAsync"/> first
        /// when the pending write matters. Every other member throws once disposed.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Disposal and a pending write".
        /// </remarks>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            InterruptWindow();
            _disposedCts.Cancel();

            if (_activeFlush != null)
            {
                if (_hasPending)
                {
                    Debug.LogError($"SaveScheduler for '{_key}' was disposed with a write already in flight and a newer one queued behind it; the newer write was never saved.");
                }
            }
            else if (_hasPending)
            {
                try
                {
                    FlushSynchronousCore();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"SaveScheduler for '{_key}' could not flush its pending write during Dispose and it was lost: {exception.Message}");
                }
            }

            _disposedCts.Dispose();
        }

        /// <summary>
        /// Restores the pending state rather than reporting a save that never happened, if the
        /// write turns out to need more than this thread.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        private void FlushSynchronousCore()
        {
            T toWrite = _pending;
            _pending = null;
            _hasPending = false;

            UniTask task = _saveService.SaveAsync(_key, toWrite, CancellationToken.None);

            if (task.Status == UniTaskStatus.Pending)
            {
                _pending = toWrite;
                _hasPending = true;
                throw SaveException.FlushWouldBlock(_key);
            }

            task.GetAwaiter().GetResult();
        }

        private void ScheduleWindowIfNeeded()
        {
            if (_disposed || _waiting || _activeFlush != null) return;

            _waiting = true;
            _windowCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
            WaitThenFlushAsync(_windowCts.Token).Forget();
        }

        /// <remarks>
        /// See docs/saving.md, "Write coalescing, and why it cannot live inside SaveAsync".
        /// </remarks>
        private async UniTaskVoid WaitThenFlushAsync(CancellationToken windowToken)
        {
            try
            {
                await _clock.Delay(_coalesceWindowMilliseconds, windowToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _waiting = false;
            _windowCts.Dispose();
            _windowCts = null;

            await EnsureFlushingAsync(_disposedCts.Token).SuppressCancellationThrow();
        }

        private void InterruptWindow()
        {
            if (!_waiting) return;

            _waiting = false;
            _windowCts.Cancel();
            _windowCts.Dispose();
            _windowCts = null;
        }

        /// <summary>
        /// The only place a write is started. Callers arriving while one runs join it rather than
        /// starting a second write against the same key.
        /// </summary>
        private UniTask EnsureFlushingAsync(CancellationToken ct)
        {
            if (_activeFlush != null) return _activeFlush.Task;
            if (!_hasPending) return UniTask.CompletedTask;

            UniTaskCompletionSource completion = new();
            _activeFlush = completion;
            RunFlushLoopAsync(completion, ct).Forget();

            return completion.Task;
        }

        /// <remarks>
        /// See docs/saving.md, "One write in flight".
        /// See docs/saving.md, "A failed write now says so".
        /// </remarks>
        private async UniTaskVoid RunFlushLoopAsync(UniTaskCompletionSource completion, CancellationToken ct)
        {
            try
            {
                while (_hasPending)
                {
                    T toWrite = _pending;
                    _pending = null;
                    _hasPending = false;

                    try
                    {
                        await _saveService.SaveAsync(_key, toWrite, ct);
                    }
                    catch
                    {
                        if (!_hasPending)
                        {
                            _pending = toWrite;
                            _hasPending = true;
                        }

                        throw;
                    }
                }

                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(ct);
            }
            catch (Exception exception)
            {
                Debug.LogError($"SaveScheduler for '{_key}' failed to save and will retry in {_coalesceWindowMilliseconds}ms: {exception.Message}");
                completion.TrySetException(exception);
            }
            finally
            {
                _activeFlush = null;

                if (_hasPending) ScheduleWindowIfNeeded();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw SaveException.SchedulerDisposed(_key);
        }
    }
}
