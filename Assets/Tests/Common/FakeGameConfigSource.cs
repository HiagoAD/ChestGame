using System;
using System.Threading;
using Company.ChestGame.Config;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Hands the content loader whatever document a test wants it to see, including none at all, and
    // can fail the way a real fetch fails.
    public class FakeGameConfigSource : IGameConfigSource
    {
        public const string ValidDocument = @"{
            ""GemsReward"": 10,
            ""CoinsReward"": 50
        }";

        public string Document { get; set; } = ValidDocument;

        // Delivered through the returned task rather than thrown from the call, which is how a
        // source that actually waits on something would report a failure.
        public Exception FailWith { get; set; }

        public int ReadCallCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public UniTask<string> ReadAsync(CancellationToken ct)
        {
            ReadCallCount++;
            LastToken = ct;

            return FailWith != null
                ? UniTask.FromException<string>(FailWith)
                : UniTask.FromResult(Document);
        }
    }
}
