using System;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Takes the result of a UniTask that has already finished. The pending check is what keeps the
    // edit-mode suite honest: a task that really waits fails loudly here instead of handing back a
    // default, which is the signal to move that test to play mode.
    public static class SynchronousUniTask
    {
        public static T Result<T>(UniTask<T> task)
        {
            RequireFinished(task.Status);

            return task.GetAwaiter().GetResult();
        }

        // The same for a task carrying no result. BeginAsync is one: against the fake provider
        // every load it makes is already complete.
        public static void Complete(UniTask task)
        {
            RequireFinished(task.Status);

            task.GetAwaiter().GetResult();
        }

        private static void RequireFinished(UniTaskStatus status)
        {
            if (status == UniTaskStatus.Pending)
            {
                throw new InvalidOperationException(
                    "the task had not finished when the test asked for its result; it needs a player loop now");
            }
        }
    }
}
