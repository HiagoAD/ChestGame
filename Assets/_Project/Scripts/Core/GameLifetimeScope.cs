using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // Simple implementation of DI
    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<CurrencyManager>(Lifetime.Singleton);
            builder.Register<GameConfig>(Lifetime.Singleton);
        }
    }
}
