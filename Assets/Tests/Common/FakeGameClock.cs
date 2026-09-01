using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // A clock the test drives by hand. Awaiters park until AdvanceFrame releases them, and
    // continuations resume synchronously inside that call, so once it returns every effect of that
    // frame has already happened.
    public class FakeGameClock : IGameClock
    {
        // Seconds each AdvanceFrame call represents. 50ms keeps frame counts small and exact.
        public float DeltaTime { get; set; } = 0.05f;

        // Which resumes first when a frame tick and a delay come due together. The real player loop
        // currently runs them in this order but nothing promises it, so tests flip the knob and
        // check behaviour does not depend on the answer.
        public bool FrameWaitersResumeFirst { get; set; } = true;

        public int FramesAdvanced { get; private set; }
        public int PendingWaiters => _frameWaiters.Count + _delayWaiters.Count;

        public double ElapsedMilliseconds => _nowMilliseconds;

        private readonly List<Waiter> _frameWaiters = new();
        private readonly List<Waiter> _delayWaiters = new();
        private double _nowMilliseconds;

        private sealed class Waiter
        {
            public UniTaskCompletionSource Source;
            public double DueMilliseconds;
            public CancellationTokenRegistration Registration;
        }

        public UniTask NextFrame(CancellationToken cancellationToken) =>
            Park(_frameWaiters, _nowMilliseconds, cancellationToken);

        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            Park(_delayWaiters, _nowMilliseconds + milliseconds, cancellationToken);

        // Moves the clock on without advancing a frame: what work that costs time inside one frame
        // looks like, and the only way a test can make a time budget run out. Delays are measured
        // against the same running total, so time spent here brings a pending one closer to due,
        // exactly as real work would.
        public void Spend(double milliseconds) => _nowMilliseconds += milliseconds;

        // Advances one frame: every parked waiter resumes and any due delay fires, ordered by
        // FrameWaitersResumeFirst.
        public void AdvanceFrame()
        {
            FramesAdvanced++;
            _nowMilliseconds += DeltaTime * 1000d;

            if (FrameWaitersResumeFirst)
            {
                ReleaseFrameWaiters();
                ReleaseDueDelays();
            }
            else
            {
                ReleaseDueDelays();
                ReleaseFrameWaiters();
            }
        }

        private void ReleaseFrameWaiters() => Release(_frameWaiters, _ => true);

        private void ReleaseDueDelays() => Release(_delayWaiters, waiter => waiter.DueMilliseconds <= _nowMilliseconds);

        public void AdvanceFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                AdvanceFrame();
            }
        }

        // Advances until nothing is parked, with a budget so a flow that never settles fails rather
        // than hangs.
        public void AdvanceUntilIdle(int maxFrames = 1000)
        {
            int frames = 0;
            while (PendingWaiters > 0)
            {
                if (++frames > maxFrames)
                {
                    throw new TimeoutException($"Clock still had {PendingWaiters} waiter(s) after {maxFrames} frames");
                }
                AdvanceFrame();
            }
        }

        private UniTask Park(List<Waiter> waiters, double dueMilliseconds, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return UniTask.FromCanceled(cancellationToken);
            }

            Waiter waiter = new()
            {
                Source = new UniTaskCompletionSource(),
                DueMilliseconds = dueMilliseconds
            };
            waiters.Add(waiter);

            waiter.Registration = cancellationToken.Register(() =>
            {
                if (waiters.Remove(waiter))
                {
                    waiter.Source.TrySetCanceled(cancellationToken);
                }
            });

            return waiter.Source.Task;
        }

        private static void Release(List<Waiter> waiters, Func<Waiter, bool> isDue)
        {
            // Snapshot first: resuming a waiter runs its continuation synchronously, and that
            // continuation usually parks a fresh waiter belonging to the next frame.
            List<Waiter> ready = waiters.FindAll(waiter => isDue(waiter));

            foreach (Waiter waiter in ready)
            {
                waiters.Remove(waiter);
                waiter.Registration.Dispose();
                waiter.Source.TrySetResult();
            }
        }
    }
}
