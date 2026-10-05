using System;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using Company.ChestGame.Saving;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // The root scope, and the only one authored in a scene. Place it in the boot scene; it survives
    // every scene load afterwards, and every other scope in the game descends from it.
    //
    // What it registers is split in two. RegisterCoreServices is everything that can be built
    // immediately; RegisterLoadedServices is everything that needs content to exist first.
    public class GameLifetimeScope : LifetimeScope
    {
        // Held rather than resolved on demand, because the callbacks that use it can fire at any
        // time, including while the container is unavailable.
        private ISaveFlushRegistry _saveFlushRegistry;

        protected override void Awake()
        {
            // Base first: that is where the container is built and the bootstrapper dispatched.
            base.Awake();

            // The game scene's scope descends from this one, so it has to survive the scene load.
            DontDestroyOnLoad(gameObject);

            _saveFlushRegistry = Container.Resolve<ISaveFlushRegistry>();
        }

        // The last callback with any durability guarantee on mobile, so anything holding an
        // unwritten save has to be flushed here or risk losing it whenever the OS kills a
        // backgrounded app.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) FlushAllSavesOrLog();
        }

        // OnApplicationPause(true) does not fire on most desktop platforms on quit, so this is the
        // equivalent close for the same window there.
        private void OnApplicationQuit() => FlushAllSavesOrLog();

        // Wrapped because a pause or quit callback that throws is worse than one that logs and
        // returns. This catches the disk write itself failing - a full disk, a revoked permission -
        // not a wiring mistake, which is refused earlier.
        private void FlushAllSavesOrLog()
        {
            try
            {
                _saveFlushRegistry.FlushAll();
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to flush saves on pause/quit: {exception.Message}");
            }
        }

        // Optional. Wire it in the inspector to show boot progress; leave it empty and boot reports
        // nowhere.
        [SerializeField] private BootStatusLabel _bootStatus;

        // The == comparison is deliberate and must not become `is not null`: a missing or destroyed
        // component only compares equal to null through Unity's overloaded operator.
        protected override void Configure(IContainerBuilder builder) =>
            RegisterCoreServices(builder, _bootStatus != null ? _bootStatus : null,
                CurrencySaveInputsOverride, LegacyCurrencyPlayerPrefsKeyOverride);

        // Everything that can be built the moment the container is, needing no loaded asset. Public
        // and separate from Configure so a caller can build the same container without a scene.
        //
        // Leave the last two null for the real game. Pass them to redirect saving away from the
        // real save location and the real PlayerPrefs entry - which any caller that resolves a save
        // or a currency service must do, or it reads and overwrites the running player's own data.
        public static void RegisterCoreServices(IContainerBuilder builder, IBootStatus status = null,
            SaveFactoryInputs currencySaveInputs = null, string legacyCurrencyPlayerPrefsKey = null)
        {
            // Substituted rather than left null, so no caller downstream needs a guard.
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

            builder.Register<ISaveFlushRegistry, SaveFlushRegistry>(Lifetime.Singleton);

            // One service, shared by every save key in this composition.
            builder.RegisterInstance<ISaveService>(BuildCurrencySaveService(currencySaveInputs, legacyCurrencyPlayerPrefsKey));

            // A factory rather than plain injection, because the key is a plain string the
            // container has nothing to resolve it from.
            builder.Register<SaveScheduler<CurrencySaveDocument>>(resolver =>
            {
                SaveScheduler<CurrencySaveDocument> scheduler = new(
                    resolver.Resolve<ISaveService>(),
                    CurrencySaveHandler.SaveKey,
                    resolver.Resolve<IGameClock>());

                // Inside the factory, so every route to this singleton registers it exactly once.
                // Registering also throws if this scheduler could never be flushed, which is what
                // turns a bad composition into a startup failure instead of a silent data loss.
                resolver.Resolve<ISaveFlushRegistry>().Register(scheduler);

                return scheduler;
            }, Lifetime.Singleton);

            // Not redundant, do not delete: registering for the pause/quit flush is a side effect of
            // resolving, so a scheduler nothing resolves is one nothing flushes. Add a line here for
            // every scheduler registered above.
            builder.RegisterBuildCallback(resolver => resolver.Resolve<SaveScheduler<CurrencySaveDocument>>());

            builder.Register<ICurrencySaveHandler, CurrencySaveHandler>(Lifetime.Singleton);

            builder.Register<SaveScheduler<GameMetaSaveDocument>>(resolver =>
            {
                SaveScheduler<GameMetaSaveDocument> scheduler = new(
                    resolver.Resolve<ISaveService>(),
                    GameMetaSaveDocument.SaveKey,
                    resolver.Resolve<IGameClock>());

                resolver.Resolve<ISaveFlushRegistry>().Register(scheduler);

                return scheduler;
            }, Lifetime.Singleton);

            builder.RegisterBuildCallback(resolver => resolver.Resolve<SaveScheduler<GameMetaSaveDocument>>());

            builder.Register<ICurrencyManager, CurrencyManager>(Lifetime.Singleton);

            builder.Register<GameContentLoader>(Lifetime.Singleton);

            // As its interfaces rather than as an entry point, so it runs from a real scope but
            // stays inert in a container built by hand.
            builder.Register<GameBootstrapper>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
        }

        // Readable, unprotected JSON, written so a kill mid-write cannot leave a torn file behind.
        // Despite the name, the service it returns is the one every save key in this composition
        // shares, not currency's alone. See docs/saving.md for why this combination ships.
        private static ISaveService BuildCurrencySaveService(SaveFactoryInputs inputs, string legacyPlayerPrefsKey)
        {
            inputs ??= DefaultCurrencySaveInputs();
            legacyPlayerPrefsKey ??= DefaultLegacyCurrencyPlayerPrefsKey();

            return new SaveService(
                SaveComponentFactory.CreateCodec(SaveCodec.Json),
                SaveComponentFactory.CreateProtector(SaveProtection.None, inputs),
                SaveComponentFactory.CreateStore(SaveStorage.AtomicFile, inputs),
                migrator: null,
                legacyImport: new CurrencyLegacyImport(legacyPlayerPrefsKey));
        }

        // What a caller loading the boot scene has instead of arguments, since Unity fixes the
        // signature of the callback that builds this container. Assign both before the scene loads
        // and clear them afterwards; they are global and outlive whatever set them.
        //
        // Both must be null in a real run, or a player's own save is redirected somewhere they
        // cannot see. Do not make them conditional on a scripting define: UNITY_INCLUDE_TESTS is set
        // for the whole editor compilation, not only while tests run.
        public static SaveFactoryInputs CurrencySaveInputsOverride { get; set; }

        public static string LegacyCurrencyPlayerPrefsKeyOverride { get; set; }

        private static SaveFactoryInputs DefaultCurrencySaveInputs() =>
            CurrencySaveInputsOverride ?? SaveFactoryInputs.Defaults();

        private static string DefaultLegacyCurrencyPlayerPrefsKey() =>
            string.IsNullOrEmpty(LegacyCurrencyPlayerPrefsKeyOverride)
                ? CurrencyLegacyImport.DefaultLegacyKey
                : LegacyCurrencyPlayerPrefsKeyOverride;

        // The half that needs content. Everything derived from a loaded asset is registered already
        // built, so nothing here can exist without its data.
        public static void RegisterLoadedServices(IContainerBuilder builder, LoadedContent content)
        {
            builder.RegisterInstance<IGameConfig>(new LocalJsonGameConfig(content.GameConfigDocument));
            builder.RegisterInstance<IMinigameCatalog>(new MinigameCatalog(content.Minigames));
            builder.RegisterInstance<IPopupCatalog>(new PopupCatalog(content.Popups));
            builder.RegisterInstance<IPopupParentProvider>(new PopupParentProvider(content.PopupParentPrefab));

            builder.Register<MinigameContentPreloader>(Lifetime.Singleton);

            builder.Register<IPopupManager, PopupManager>(Lifetime.Singleton);
            builder.Register<IMinigameManager, MinigameManager>(Lifetime.Singleton);
            builder.Register<IRewardsManager, RewardsManager>(Lifetime.Singleton);
        }
    }
}
