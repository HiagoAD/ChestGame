using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Hands the content loader an authored minigame list without an asset behind it.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public class FakeMinigameListSource : IMinigameListSource
    {
        public IReadOnlyList<MinigameBaseSO> Entries { get; set; } = new List<MinigameBaseSO>();

        public Exception FailWith { get; set; }

        public int ReadCallCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public UniTask<IReadOnlyList<MinigameBaseSO>> ReadAsync(CancellationToken ct)
        {
            ReadCallCount++;
            LastToken = ct;

            return FailWith != null
                ? UniTask.FromException<IReadOnlyList<MinigameBaseSO>>(FailWith)
                : UniTask.FromResult(Entries);
        }
    }
}
