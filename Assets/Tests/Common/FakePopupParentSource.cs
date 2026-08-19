using System;
using System.Threading;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.Common
{
    // Hands the content loader a popup parent prefab the test owns, with none of the real source's
    // Resources lookup.
    public class FakePopupParentSource : IPopupParentSource
    {
        public PopupParent Prefab { get; set; }

        public Exception FailWith { get; set; }

        public int ReadCallCount { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public UniTask<PopupParent> ReadAsync(CancellationToken ct)
        {
            ReadCallCount++;
            LastToken = ct;

            return FailWith != null
                ? UniTask.FromException<PopupParent>(FailWith)
                : UniTask.FromResult(Prefab);
        }
    }
}
