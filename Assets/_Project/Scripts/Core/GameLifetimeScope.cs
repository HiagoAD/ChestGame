using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using Company.ChestGame.Rewards;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // Simple implementation of DI
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<ICurrencyManager, CurrencyManager>(Lifetime.Singleton);
            builder.Register<IGameConfig, LocalJsonGameConfig>(Lifetime.Singleton);
            builder.Register<IRewardsManager, RewardsManager>(Lifetime.Singleton);
            builder.Register<IPopupManager, PopupManager>(Lifetime.Singleton);
        }
    }
}
