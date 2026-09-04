using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // Coalesces many MarkDirty calls into one SaveAsync call per window, so a caller like a
    // resource bank that saves on every add and every spend does not turn every coin into a file
    // write. Owns an ISaveService rather than being one - SaveAsync's own contract ("when it
    // completes, the save is written") has to survive untouched, so nothing here weakens it into
    // "queued"; MarkDirty is a different, and weaker, promise than SaveAsync ever made, and it gets
    // its own name rather than hiding behind SaveAsync's. See docs/saving.md, "Write coalescing, and
    // why it cannot live inside SaveAsync".
    //
    // One key, one state, fixed at construction rather than passed to MarkDirty: a scheduler
    // coalescing several independent keys would need a table of pending writes instead of one, and
    // nothing this phase builds needs more than one save slot. A game with several needs several
    // SaveScheduler<T> instances, the same granularity ISaveService.SaveAsync<T>(key, ...) already
    // has per call.
    //
    // Main-thread only, like every other type in this file that touches Unity through the clock it
    // is handed. MarkDirty, FlushAsync and FlushBlocking must only ever be called from the thread
    // Unity calls its own callbacks on - the same requirement PlayerPrefsStore places on itself,
    // for the same reason. Nothing here takes a lock, because nothing here needs one: every field
    // below is only ever touched from that one thread, at points in time that never overlap, since
    // the only thing that ever leaves it is the write ISaveService.SaveAsync performs somewhere
    // inside its own composed store - see ThreadHoppingStore - and every place that touches these
    // fields runs before that hop starts or after it has already returned control here.
    public class SaveScheduler<T> : IDisposable where T : class
    {
        // Long enough that a burst of MarkDirty calls from one interaction - several chests opened
        // in a row, a combo of pickups in one frame - collapses into one write; short enough that a
        // crash loses at most a second of progress beyond whatever FlushBlocking already covers at
        // the lifecycle points that call it. Not load-bearing for correctness either way - only for
        // how much a crash between windows can lose - so a caller with a stronger opinion overrides
        // it per instance rather than this assembly guessing at one number for every save.
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

        // True from the moment MarkDirty or FlushAsync/FlushBlocking has state neither durably saved
        // nor currently being saved. Read-only window into what would otherwise all be private -
        // useful for a caller deciding whether a flush is worth calling at all, and for a test
        // asserting coalescing actually coalesced rather than writing on every call.
        public bool HasPendingWrite => _hasPending;

        // True while a real SaveAsync call for this key is in flight - not merely while a
        // coalescing window is still counting down. See "One write in flight" in docs/saving.md.
        public bool IsFlushing => _activeFlush != null;

        // Answered ahead of time, at composition time, from the ISaveService this scheduler owns -
        // not discovered reactively the moment FlushBlocking happens to be called. False here means
        // FlushBlocking on this instance will always throw the instant a write is genuinely in
        // flight, every time, because the underlying store needs to leave the calling thread to
        // finish. A composition root that will ever call FlushBlocking on a scheduler - anything
        // wired to OnApplicationPause - should assert this is true once, rather than let a device
        // discover it is false at the one moment durability matters. See docs/saving.md,
        // "FlushBlocking, and why it cannot deadlock".
        public bool CanFlushBlocking => _saveService.CompletesOnCallingThread;

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

        // Never encodes, protects or writes anything itself - only ever remembers state and, once a
        // window elapses, hands it to the ISaveService this was constructed with. state is whatever
        // the caller is about to keep mutating; nothing here reads a single field of it before the
        // window elapses, which is what keeps this safe to call from code that owns a live, mutable
        // save model exactly the way ISaveService.SaveAsync's own state parameter always has.
        public void MarkDirty(T state)
        {
            ThrowIfDisposed();

            _pending = state;
            _hasPending = true;

            ScheduleWindowIfNeeded();
        }

        // The explicit surface SaveAsync's contract needed once coalescing existed: unlike
        // MarkDirty, this does not return until whatever is currently pending - and anything that
        // becomes pending while this call is already waiting on an in-flight write - is durably
        // saved. Safe to call with nothing pending; it is then a no-op rather than an empty write.
        //
        // ct only governs a flush this call itself starts. A flush this call instead joins - one
        // already running because MarkDirty's own window had already elapsed, or because another
        // FlushAsync call got there first - is awaited to completion regardless of ct, the same way
        // a physical write already in progress is never interrupted partway through anywhere else
        // in this file: cancelling a caller's wait for the result is not the same thing as
        // cancelling the write, and this type never does the second to honour the first.
        public async UniTask FlushAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            InterruptWindow();

            if (!_hasPending && _activeFlush == null) return;

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposedCts.Token);

            await EnsureFlushingAsync(linked.Token);
        }

        // The synchronous escape hatch OnApplicationPause(true) needs, per docs/saving.md,
        // "FlushBlocking, and why it cannot deadlock": genuinely synchronous end to end, never a
        // blocking wait on work that itself needs this thread to finish. It either finds nothing to
        // do, finds a save service that never leaves the calling thread and completes on the spot,
        // or refuses outright - it never sits and waits for the third case to resolve itself, which
        // is the one path that could actually deadlock.
        public void FlushBlocking()
        {
            ThrowIfDisposed();

            InterruptWindow();

            // A write already in flight might be hopping through a worker thread right now - see
            // ThreadHoppingStore - and there is no way to tell from here whether it has or would.
            // Waiting for it is exactly the blocking-on-main-thread-bound-work this method exists to
            // refuse; see docs/saving.md for why this throws instead of blocking.
            if (_activeFlush != null) throw SaveException.FlushWouldBlock(_key);

            if (!_hasPending) return;

            FlushSynchronousCore();
        }

        // The scheduler's own loop stops here, cleanly: the coalescing window (if one is counting
        // down) and the disposed-token every in-flight or future SaveAsync call is linked against
        // both get cancelled, so nothing this instance started keeps running once this returns
        // control. A pending write not currently claimed by an in-flight flush is written now if that
        // can happen synchronously - the common case, since only a save service composed over
        // ThreadHoppingStore can ever need more than this thread to finish (CanFlushBlocking answers
        // this ahead of time). If it cannot - that composition, or a flush already mid-hop when
        // Dispose was called - the write is lost. Dispose still never throws, callers are entitled to
        // assume it does not, but the loss is reported through Debug.LogError rather than only a
        // comment, the same way CurrencyManager already reports a failed add or spend: a comment is
        // not a signal anyone reading a device log has. See docs/saving.md, "Disposal and a pending
        // write".
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            InterruptWindow();
            _disposedCts.Cancel();

            if (_activeFlush != null)
            {
                // Left to finish or fail in the background, unobserved: awaiting it here would risk
                // the same deadlock FlushBlocking refuses to risk. Anything newer than what it
                // already claimed is genuinely lost, since ScheduleWindowIfNeeded is a no-op once
                // _disposed is true and nothing else will ever pick this key's pending state back up.
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
                    // Swallowed rather than rethrown, for the reason AtomicFileStore's own
                    // best-effort temp-file cleanup already gives: Dispose must not throw. Logged
                    // rather than only commented so the loss shows up in a device log next to
                    // whatever else went wrong, instead of only in this file's own reasoning.
                    Debug.LogError($"SaveScheduler for '{_key}' could not flush its pending write during Dispose and it was lost: {exception.Message}");
                }
            }

            _disposedCts.Dispose();
        }

        // Shared core of FlushBlocking and Dispose's own best-effort attempt. Claims whatever is
        // currently pending, tries to run it to completion without ever yielding back to the caller,
        // and puts it back as pending - rather than reporting a save that never happened as done -
        // if the underlying SaveAsync call turns out to need more than this thread to finish.
        private void FlushSynchronousCore()
        {
            T toWrite = _pending;
            _pending = null;
            _hasPending = false;

            UniTask task = _saveService.SaveAsync(_key, toWrite, CancellationToken.None);

            // Not a blocking wait: a task that has not already finished by the time control returns
            // here never will without something pumping the main thread this call is currently
            // occupying, so this checks a fact rather than waiting for one to become true.
            if (task.Status == UniTaskStatus.Pending)
            {
                _pending = toWrite;
                _hasPending = true;
                throw SaveException.FlushWouldBlock(_key);
            }

            // Already finished: GetResult() only ever reads out a recorded outcome here, rethrowing
            // a captured exception if SaveAsync faulted rather than waiting for anything.
            task.GetAwaiter().GetResult();
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
                // Disposed, or FlushAsync/FlushBlocking pre-empted the window to flush right away -
                // either way _waiting was already cleared by whichever of those triggered this.
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

        // The one place a real SaveAsync call for this key is ever made. If one is already running,
        // every caller here joins the same UniTaskCompletionSource rather than starting a second,
        // concurrent call that would race the file this key resolves to - see docs/saving.md, "One
        // write in flight". The loop inside carries whatever is latest in _pending at the moment it
        // is claimed, which is what makes a burst of MarkDirty calls collapse into the single
        // follow-up write that same section describes, rather than one write per call.
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
                        // A failed write must not be reported as a successful one below, but it must
                        // also not silently drop the state that failed to save. Only restored if
                        // nothing newer has arrived while this attempt was in flight - a fresher
                        // MarkDirty already waiting to be picked up must win over resurrecting the
                        // stale value that just failed.
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
                completion.TrySetException(exception);
            }
            finally
            {
                _activeFlush = null;

                // A failed or cancelled-before-it-started attempt can leave _hasPending true with
                // nothing counting down to retry it - MarkDirty only schedules a window for itself,
                // not on this loop's behalf. Scheduling one here is what a real failure needs to be
                // retried automatically rather than stranded until an unrelated MarkDirty call
                // happens to arrive; ScheduleWindowIfNeeded is already a no-op once disposed, which
                // is what keeps this from resurrecting a window Dispose just tore down.
                if (_hasPending) ScheduleWindowIfNeeded();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw SaveException.SchedulerDisposed(_key);
        }
    }
}
