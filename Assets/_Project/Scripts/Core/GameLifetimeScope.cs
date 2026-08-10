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
    // Simple implementation of DI
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder) => RegisterServices(builder);

        // The registration list lives apart from Configure so tests can assert against the real
        // composition root instead of a hand-copied duplicate of it.
        public static void RegisterServices(IContainerBuilder builder)
        {
            // Engine-facing seams. Everything downstream draws randomness and time through these,
            // which is what keeps gameplay logic deterministic under test.
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);

            // Asset- and storage-facing sources, each the only place that knows a concrete path.
            builder.Register<IGameConfigSource, ResourcesGameConfigSource>(Lifetime.Singleton);
            builder.Register<IMinigameCatalog, ResourcesMinigameCatalog>(Lifetime.Singleton);
            builder.Register<IPopupCatalog, ResourcesPopupCatalog>(Lifetime.Singleton);
            builder.Register<IPopupParentProvider, ResourcesPopupParentProvider>(Lifetime.Singleton);
            builder.Register<IResourceBankSaveHandler<CurrencyType>, DefaultResourceBankSaveHandle<CurrencyType>>(Lifetime.Singleton);

            builder.Register<ICurrencyManager, CurrencyManager>(Lifetime.Singleton);
            builder.Register<IGameConfig, LocalJsonGameConfig>(Lifetime.Singleton);
            builder.Register<IRewardsManager, RewardsManager>(Lifetime.Singleton);
            builder.Register<IPopupManager, PopupManager>(Lifetime.Singleton);
            builder.Register<IMinigameManager, MinigameManager>(Lifetime.Singleton);
        }
    }
}
