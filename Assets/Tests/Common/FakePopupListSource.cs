using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Popups;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Hands the content loader an authored popup list without an asset behind it.
    public class FakePopupListSource : IPopupListSource
    {
        public IReadOnlyList<PopupBase> Entries { get; set; } = new List<PopupBase>();

        public Exception FailWith { get; set; }

        public int ReadCallCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public UniTask<IReadOnlyList<PopupBase>> ReadAsync(CancellationToken ct)
        {
            ReadCallCount++;
            LastToken = ct;

            return FailWith != null
                ? UniTask.FromException<IReadOnlyList<PopupBase>>(FailWith)
                : UniTask.FromResult(Entries);
        }
    }
}
