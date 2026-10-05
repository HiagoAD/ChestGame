using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // Collapses many saves of the same state into at most one write per window. Hand it state
    // whenever that state changes, and it writes once the window elapses instead of once per call.
    //
    // One key and one state per instance, both fixed at construction; use one instance per thing
    // being saved. Dispose it when that thing goes away - disposal makes a last attempt to write
    // anything still pending.
    //
    // Not thread safe, and holds no lock. Every member must be called from the same thread, which
    // for a Unity game means the main thread. See docs/saving.md for why the coalescing lives here
    // rather than behind the save call itself.
    public class SaveScheduler<T> : IDisposable, ISaveFlushable where T : class
    {
        // How long changes are collected before a write. Longer coalesces more and risks losing
        // more to a crash; pass your own to the constructor to choose differently.
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

        // State neither durably saved nor currently being saved.
        public bool HasPendingWrite => _hasPending;

        // A write is actually running, as opposed to a window merely counting down towards one.
        public bool IsFlushing => _activeFlush != null;

        // Whether FlushBlocking can ever succeed here. Readable before anything is pending, so a
        // caller can reject a composition it cannot flush at construction rather than discovering
        // it at the one moment durability matters.
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

        // Records state to be written when the window elapses, replacing anything recorded earlier.
        // Nothing reads state until then, so the caller may keep mutating the same instance.
        // Returns immediately; it is not a promise that anything has been written yet.
        public void MarkDirty(T state)
        {
            ThrowIfDisposed();

            _pending = state;
            _hasPending = true;

            ScheduleWindowIfNeeded();
        }

        // Writes now instead of waiting for the window, and returns once everything pending is
        // durable - including anything that becomes pending while this is waiting. Does nothing if
        // nothing is pending.
        //
        // Cancelling ct abandons this call's wait, never the write itself. A write already running
        // when this is called runs to completion either way.
        public async UniTask FlushAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            InterruptWindow();

            if (!_hasPending && _activeFlush == null) return;

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposedCts.Token);

            await EnsureFlushingAsync(linked.Token);
        }

        // FlushAsync for a caller with no time left to await anything, such as an application
        // pause or quit callback. Writes on the spot, or throws; it never waits. Check
        // CanFlushBlocking once at construction to know which of those a given composition gets.
        public void FlushBlocking()
        {
            ThrowIfDisposed();

            InterruptWindow();

            // Refused rather than awaited: a write already running may need this thread to finish,
            // so waiting for it here is the deadlock this method exists to avoid.
            if (_activeFlush != null) throw SaveException.FlushWouldBlock(_key);

            if (!_hasPending) return;

            FlushSynchronousCore();
        }

        // Stops the window, cancels any write in flight, and makes one synchronous attempt to save
        // what is pending. That attempt is best effort: if it cannot happen synchronously the write
        // is lost and logged, because disposal never throws. Call FlushAsync first when the pending
        // write matters. Every other member throws once disposed.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            InterruptWindow();
            _disposedCts.Cancel();

            if (_activeFlush != null)
            {
                // Not awaited: waiting on a write already running risks the same deadlock
                // FlushBlocking refuses. Anything queued behind it is lost.
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
                    // Swallowed because disposal must not throw, logged so the loss is visible in
                    // a device log rather than only here.
                    Debug.LogError($"SaveScheduler for '{_key}' could not flush its pending write during Dispose and it was lost: {exception.Message}");
                }
            }

            _disposedCts.Dispose();
        }

        // Restores the pending state rather than reporting a save that never happened, whether the
        // write turns out to need more than this thread or simply fails.
        private void FlushSynchronousCore()
        {
            T toWrite = _pending;
            _pending = null;
            _hasPending = false;

            bool finished;
            try
            {
                UniTask task = _saveService.SaveAsync(_key, toWrite, CancellationToken.None);

                // A status check, not a wait: nothing could complete this task without the thread this
                // call is already occupying. Do not turn it into an await.
                finished = task.Status != UniTaskStatus.Pending;

                // Already finished, so this reads a recorded outcome and rethrows a captured
                // exception rather than blocking.
                if (finished) task.GetAwaiter().GetResult();
            }
            catch
            {
                // Not logged: the caller gets the exception, or Dispose logs it. Nothing is
                // counting down to retry once the state is back, so a window is opened here, as
                // RunFlushLoopAsync's finally does. Disposal has already ruled that out.
                _pending = toWrite;
                _hasPending = true;
                ScheduleWindowIfNeeded();
                throw;
            }

            if (!finished)
            {
                _pending = toWrite;
                _hasPending = true;
                throw SaveException.FlushWouldBlock(_key);
            }
        }

        private void ScheduleWindowIfNeeded()
        {
            if (_disposed || _waiting || _activeFlush != null) return;

            _waiting = true;
            _windowCts = CancellationTokenSource.CreateLinkedTokenSource(_disposedCts.Token);
            WaitThenFlushAsync(_windowCts.Token).Forget();
        }

        private async UniTaskVoid WaitThenFlushAsync(CancellationToken windowToken)
        {
            try
            {
                await _clock.Delay(_coalesceWindowMilliseconds, windowToken);
            }
            catch (OperationCanceledException)
            {
                // Disposed, or a flush pre-empted the window; either way _waiting is already
                // cleared by whatever did it.
                return;
            }

            _waiting = false;
            _windowCts.Dispose();
            _windowCts = null;

            try
            {
                await EnsureFlushingAsync(_disposedCts.Token);
            }
            catch (Exception)
            {
                // Nobody awaits this path. A failed write was already logged, naming the key, by
                // RunFlushLoopAsync; letting it escape the UniTaskVoid would report it a second
                // time as an unobserved exception. Cancellation is disposal, and equally expected.
            }
        }

        private void InterruptWindow()
        {
            if (!_waiting) return;

            _waiting = false;
            _windowCts.Cancel();
            _windowCts.Dispose();
            _windowCts = null;
        }

        // The only place a write is started. Callers arriving while one runs join it rather than
        // starting a second write against the same key.
        private UniTask EnsureFlushingAsync(CancellationToken ct)
        {
            if (_activeFlush != null) return _activeFlush.Task;
            if (!_hasPending) return UniTask.CompletedTask;

            UniTaskCompletionSource completion = new();
            _activeFlush = completion;
            RunFlushLoopAsync(completion, ct).Forget();

            return completion.Task;
        }

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
                        // A failed write is neither reported as success nor dropped. Restored only
                        // if nothing newer arrived meanwhile, so fresher state always wins.
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
                // Nobody is awaiting this path, so an exception here would otherwise vanish with
                // nothing saying a save failed. Logged unconditionally, then retried at the same
                // window rather than backing off.
                Debug.LogError($"SaveScheduler for '{_key}' failed to save and will retry in {_coalesceWindowMilliseconds}ms: {exception.Message}");
                completion.TrySetException(exception);
            }
            finally
            {
                _activeFlush = null;

                // A failed or cancelled attempt leaves state pending with nothing counting down to
                // retry it. This is what turns that into a retry rather than a stranded write.
                if (_hasPending) ScheduleWindowIfNeeded();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw SaveException.SchedulerDisposed(_key);
        }
    }
}
