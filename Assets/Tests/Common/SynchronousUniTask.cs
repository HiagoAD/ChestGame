using System;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Takes the result of a <c>UniTask</c> that has already finished.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "SynchronousUniTask, and the signal a pending task sends".
    /// </remarks>
    public static class SynchronousUniTask
    {
        public static T Result<T>(UniTask<T> task)
        {
            RequireFinished(task.Status);

            return task.GetAwaiter().GetResult();
        }

        /// <summary>The same for a task carrying no result.</summary>
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
