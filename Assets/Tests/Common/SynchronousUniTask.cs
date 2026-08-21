using System;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Takes the result of a UniTask that has already finished.
    //
    // The edit-mode suite drives the sources through FakeAssetProvider, which hands back an
    // already-completed task, so a test needs no player loop to see the answer. The pending check
    // is what keeps that honest: a task that really waits fails loudly here instead of handing back
    // a default value, which is the signal to move that test to play mode. The real sources do wait
    // — they go through Addressables — which is why the play-mode fixtures are UnityTests rather
    // than callers of this.
    public static class SynchronousUniTask
    {
        public static T Result<T>(UniTask<T> task)
        {
            RequireFinished(task.Status);

            return task.GetAwaiter().GetResult();
        }

        // The same for a task carrying no result. BeginAsync is one: against the fake provider
        // every load it makes is already complete, so the whole of it runs inside the call and a
        // test can assert the moment this returns.
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
