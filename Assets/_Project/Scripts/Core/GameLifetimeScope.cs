using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using TapNation.Modules.ResourceBank.Saving;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // The root scope, and the only one authored in a scene. It lives in the boot scene and outlives
    // it, because what is registered here is shared with the scope the bootstrapper builds once
    // content has loaded. See docs/architecture.md.
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Awake()
        {
            // Base first: that is where the container is built and the bootstrapper dispatched.
            base.Awake();

            // The game scene's scope descends from this one, so it has to survive the scene load.
            DontDestroyOnLoad(gameObject);
        }

        // The one thing the root scope cannot construct for itself, so it is wired in the inspector.
        [SerializeField] private BootStatusLabel _bootStatus;

        // Unity's null, not C#'s: a missing or destroyed component compares equal to null only
        // through the overloaded operator.
        protected override void Configure(IContainerBuilder builder) =>
            RegisterCoreServices(builder, _bootStatus != null ? _bootStatus : null);

        // Apart from Configure so tests can assert against the real composition root rather than a
        // hand-copied duplicate. Everything here can be built the moment the container is, because
        // nothing in it needs an asset.
        public static void RegisterCoreServices(IContainerBuilder builder, IBootStatus status = null)
        {
            // Never null, so the bootstrapper needs no guard at any call site.
            builder.RegisterInstance<IBootStatus>(status ?? new SilentBootStatus());

            // Engine-facing seams: everything downstream draws randomness and time through these.
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);

            // Swapping the loading technology is this one line.
            builder.Register<IAssetProvider, AddressablesAssetProvider>(Lifetime.Singleton);

            // Sources, each the only place that knows a concrete key.
            builder.Register<IGameConfigSource, AddressablesGameConfigSource>(Lifetime.Singleton);
            builder.Register<IMinigameListSource, AddressablesMinigameListSource>(Lifetime.Singleton);
            builder.Register<IPopupListSource, AddressablesPopupListSource>(Lifetime.Singleton);
            builder.Register<IPopupParentSource, AddressablesPopupParentSource>(Lifetime.Singleton);
            builder.Register<IResourceBankSaveHandler<CurrencyType>, DefaultResourceBankSaveHandle<CurrencyType>>(Lifetime.Singleton);

            builder.Register<ICurrencyManager, CurrencyManager>(Lifetime.Singleton);

            builder.Register<GameContentLoader>(Lifetime.Singleton);

            // As its interfaces rather than through RegisterEntryPoint: a LifetimeScope installs the
            // dispatcher itself, so the real game still runs this while a container a test builds by
            // hand stays inert.
            builder.Register<GameBootstrapper>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
        }

        // The half that cannot exist until content has arrived. Everything derived from a loaded
        // asset is registered as an already-built instance, so none of these ever exists without
        // its data.
        public static void RegisterLoadedServices(IContainerBuilder builder, LoadedContent content)
        {
            builder.RegisterInstance<IGameConfig>(new LocalJsonGameConfig(content.GameConfigDocument));
            builder.RegisterInstance<IMinigameCatalog>(new MinigameCatalog(content.Minigames));
            builder.RegisterInstance<IPopupCatalog>(new PopupCatalog(content.Popups));
            builder.RegisterInstance<IPopupParentProvider>(new PopupParentProvider(content.PopupParentPrefab));

            // Needs the catalog, so it belongs to this half.
            builder.Register<MinigameContentPreloader>(Lifetime.Singleton);

            builder.Register<IPopupManager, PopupManager>(Lifetime.Singleton);
            builder.Register<IMinigameManager, MinigameManager>(Lifetime.Singleton);
            builder.Register<IRewardsManager, RewardsManager>(Lifetime.Singleton);
        }
    }
}
