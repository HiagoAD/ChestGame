using System;
using Company.ChestGame.Minigame.Core;

namespace Company.ChestGame.Tests.Common
{
    // Stands in for a real minigame definition asset. MinigameBaseSO is a ScriptableObject, so it
    // has to be built with CreateInstance rather than new.
    public class FakeMinigameSO : MinigameBaseSO
    {
        public int ContainersCreated { get; private set; }

        public override Type ContainerType => typeof(FakeMinigameContainer);

        public override MinigameContainer GetMinigameContainer()
        {
            ContainersCreated++;

            FakeMinigameContainer container = new();
            container.Set(new FakeMinigameController(), null);
            return container;
        }

        public static FakeMinigameSO Create() => CreateInstance<FakeMinigameSO>();
    }

    public class FakeMinigameContainer : MinigameContainer { }

    public class FakeMinigameController : MinigameControllerBase
    {
        public int NewGameCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public bool Disposed => DisposeCalls > 0;

        public override void NewGame() => NewGameCalls++;

        public override void Dispose() => DisposeCalls++;
    }
}
