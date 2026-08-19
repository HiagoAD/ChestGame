using System;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Takes the result of a UniTask that has already finished.
    //
    // Every source in the game is still backed by Resources.Load, so every one of them completes
    // before it returns and a test needs no player loop to see the answer. The pending check is
    // what keeps that honest: the day a source really waits, this fails loudly instead of handing
    // back a default value, which is the signal to move that test to play mode.
    public static class SynchronousUniTask
    {
        public static T Result<T>(UniTask<T> task)
        {
            if (task.Status == UniTaskStatus.Pending)
            {
                throw new InvalidOperationException(
                    "the task had not finished when the test asked for its result; it needs a player loop now");
            }

            return task.GetAwaiter().GetResult();
        }
    }
}
