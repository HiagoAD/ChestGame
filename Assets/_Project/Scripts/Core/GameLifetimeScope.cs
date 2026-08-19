using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using TapNation.Modules.ResourceBank.Saving;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // The root scope, and the only one authored in a scene. It lives in the boot scene and outlives
    // it, because the services registered here are shared with the scope the bootstrapper builds
    // once content has loaded.
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Awake()
        {
            // Base first: that is where the container is built and the bootstrapper dispatched.
            base.Awake();

            // The boot scene is replaced by the game scene a moment later. Everything registered
            // here has to survive that, since the game scene's scope descends from this one.
            DontDestroyOnLoad(gameObject);
        }

        protected override void Configure(IContainerBuilder builder) => RegisterCoreServices(builder);

        // The registration lists live apart from Configure so tests can assert against the real
        // composition root instead of a hand-copied duplicate of it.
        //
        // Everything here can be built the moment the container is, because nothing in it needs an
        // asset. That is what lets the boot scene resolve the loader and the bootstrapper before a
        // single file has been read.
        public static void RegisterCoreServices(IContainerBuilder builder)
        {
            // Engine-facing seams. Everything downstream draws randomness and time through these,
            // which is what keeps gameplay logic deterministic under test.
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);

            // Asset- and storage-facing sources, each the only place that knows a concrete path.
            builder.Register<IGameConfigSource, ResourcesGameConfigSource>(Lifetime.Singleton);
            builder.Register<IMinigameListSource, ResourcesMinigameListSource>(Lifetime.Singleton);
            builder.Register<IPopupListSource, ResourcesPopupListSource>(Lifetime.Singleton);
            builder.Register<IPopupParentSource, ResourcesPopupParentSource>(Lifetime.Singleton);
            builder.Register<IResourceBankSaveHandler<CurrencyType>, DefaultResourceBankSaveHandle<CurrencyType>>(Lifetime.Singleton);

            // Its only dependency is the save handler, so it needs nothing that has to be loaded.
            builder.Register<ICurrencyManager, CurrencyManager>(Lifetime.Singleton);

            builder.Register<GameContentLoader>(Lifetime.Singleton);

            // Registered as its interfaces rather than through RegisterEntryPoint: a LifetimeScope
            // installs the entry point dispatcher itself, so the real game still runs this, while a
            // container a test builds by hand stays inert and does not boot the game from a
            // registration assertion.
            builder.Register<GameBootstrapper>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
        }

        // The half that cannot exist until content has arrived. Everything derived straight from a
        // loaded asset is registered as an already-built instance, so there is no moment at which
        // one of these exists without its data.
        public static void RegisterLoadedServices(IContainerBuilder builder, LoadedContent content)
        {
            builder.RegisterInstance<IGameConfig>(new LocalJsonGameConfig(content.GameConfigDocument));
            builder.RegisterInstance<IMinigameCatalog>(new MinigameCatalog(content.Minigames));
            builder.RegisterInstance<IPopupCatalog>(new PopupCatalog(content.Popups));
            builder.RegisterInstance<IPopupParentProvider>(new PopupParentProvider(content.PopupParentPrefab));

            builder.Register<IPopupManager, PopupManager>(Lifetime.Singleton);
            builder.Register<IMinigameManager, MinigameManager>(Lifetime.Singleton);
            builder.Register<IRewardsManager, RewardsManager>(Lifetime.Singleton);
        }
    }
}
