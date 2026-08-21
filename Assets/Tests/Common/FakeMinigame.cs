using System;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;

namespace Company.ChestGame.Tests.Common
{
    // Stands in for a real minigame definition asset. MinigameBaseSO is a ScriptableObject, so it
    // has to be built with CreateInstance rather than new.
    public class FakeMinigameSO : MinigameBaseSO
    {
        public int ContainersCreated { get; private set; }

        // What the containers this hands out point their view at. Settable because a definition
        // names its content rather than holding it, and every test wants to name something else.
        public AssetReferenceGameObject ViewReference { get; set; }

        // What its content hook loads and lets go of, when a test wants one. Left unset, the
        // definition is a minigame that owns no content of its own, which is also a real case.
        public AssetReference ContentReference { get; set; }

        public int ConfigureCalls { get; private set; }
        public int ReleaseContentCalls { get; private set; }

        public override Type ContainerType => typeof(FakeMinigameContainer);

        public override MinigameContainer GetMinigameContainer()
        {
            ContainersCreated++;

            FakeMinigameContainer container = new();
            container.Set(new FakeMinigameController(), ViewReference, this);
            return container;
        }

        public override async UniTask ConfigureControllerAsync(
            MinigameControllerBase controller, IAssetProvider assets, CancellationToken ct)
        {
            ConfigureCalls++;

            if (ContentReference != null)
            {
                await assets.LoadAsync<TextAsset>(ContentReference, ct);
            }
        }

        public override void ReleaseContent(IAssetProvider assets)
        {
            ReleaseContentCalls++;

            if (ContentReference != null) assets.Release(ContentReference);
        }

        // The id matches what MinigameBaseSO exposes, so a manager asked for "fake" finds this.
        public static FakeMinigameSO Create(string id = "fake") =>
            CreateInstance<FakeMinigameSO>().WithId(id);
    }

    public class FakeMinigameContainer : MinigameContainer { }

    public class FakeMinigameController : MinigameControllerBase
    {
        public int NewGameCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int InjectCalls { get; private set; }

        public bool Disposed => DisposeCalls > 0;

        // Takes the resolver rather than a game service, so any container at all can satisfy it and
        // counting injections costs a fixture nothing.
        [Inject]
        public void Inject(IObjectResolver resolver) => InjectCalls++;

        public override void NewGame() => NewGameCalls++;

        public override void Dispose() => DisposeCalls++;
    }
}
