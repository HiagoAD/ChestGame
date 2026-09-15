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
    /// <summary>
    /// Stands in for a real minigame definition asset.
    /// </summary>
    /// <remarks>
    /// A <see cref="ScriptableObject"/>: build it with <c>CreateInstance</c>, not <c>new</c>.
    /// </remarks>
    public class FakeMinigameSO : MinigameBaseSO
    {
        public int ContainersCreated { get; private set; }

        /// <summary>
        /// What the containers this hands out point their view at.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "A definition names its content, it does not hold it".
        /// </remarks>
        public AssetReferenceGameObject ViewReference { get; set; }

        /// <summary>
        /// What its content hook loads and lets go of.
        /// </summary>
        /// <remarks>
        /// Left unset, this is a minigame that owns no content, which is also a real case.
        /// </remarks>
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

        /// <summary>
        /// Builds a fake definition whose <c>Id</c> is <paramref name="id"/>, which is what a
        /// manager asked for that id resolves to.
        /// </summary>
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

        /// <remarks>
        /// See docs/testing.md, "What the minigame fixtures choose not to fake".
        /// </remarks>
        [Inject]
        public void Inject(IObjectResolver resolver) => InjectCalls++;

        public override void NewGame() => NewGameCalls++;

        public override void Dispose() => DisposeCalls++;
    }
}
