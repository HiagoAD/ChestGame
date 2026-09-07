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
        // Resolved once, right after the container that builds it, so OnApplicationPause and
        // OnApplicationQuit below - which Unity can call at any time - never have to reach into the
        // container from inside a callback. Every scheduler this composition builds registers
        // itself here through its own factory registration - see RegisterCoreServices - rather than
        // this scope naming each one by its concrete type, which is what lets a scheduler defined
        // in an assembly Core deliberately does not reference (Company.ChestGame.Minigame.Chests,
        // say) still get flushed from here.
        private ISaveFlushRegistry _saveFlushRegistry;

        protected override void Awake()
        {
            // Base first: that is where the container is built and the bootstrapper dispatched.
            base.Awake();

            // The game scene's scope descends from this one, so it has to survive the scene load.
            DontDestroyOnLoad(gameObject);

            _saveFlushRegistry = Container.Resolve<ISaveFlushRegistry>();
        }

        // The last callback with any durability guarantee on mobile - see docs/saving.md,
        // "FlushBlocking, and why it cannot deadlock". Without this, SaveScheduler<T>'s coalescing
        // window (up to DefaultCoalesceWindowMilliseconds) is a real data-loss window every time the
        // OS suspends or kills a backgrounded app, which on mobile can happen at any point after
        // OnApplicationPause(true) returns.
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) FlushAllSavesOrLog();
        }

        // OnApplicationPause(true) does not fire on most desktop platforms on quit, so this is the
        // equivalent close for the same window there.
        private void OnApplicationQuit() => FlushAllSavesOrLog();

        // Every scheduler registered with _saveFlushRegistry already had CanFlushBlocking asserted
        // at the moment it registered - see ISaveFlushRegistry.Register - so a FlushWouldBlock here
        // is not a realistic outcome, and FlushAll itself is fault-isolating per flushable already.
        // Still wrapped rather than trusted blindly: "the composition is structurally correct" is
        // not the same guarantee as "the disk write it triggers cannot fail" (a full disk, a revoked
        // permission), and an OnApplicationPause/OnApplicationQuit callback throwing is worse than
        // one that logs and returns - the same reasoning SaveScheduler<T>.Dispose() already follows
        // for its own best-effort flush.
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

        // The one thing the root scope cannot construct for itself, so it is wired in the inspector.
        [SerializeField] private BootStatusLabel _bootStatus;

        // Unity's null, not C#'s: a missing or destroyed component compares equal to null only
        // through the overloaded operator.
        // The overrides are what a test booting the real Boot scene has instead of arguments -
        // Unity calls this method and its signature is fixed. Both are null in a real run.
        protected override void Configure(IContainerBuilder builder) =>
            RegisterCoreServices(builder, _bootStatus != null ? _bootStatus : null,
                CurrencySaveInputsOverride, LegacyCurrencyPlayerPrefsKeyOverride);

        // Apart from Configure so tests can assert against the real composition root rather than a
        // hand-copied duplicate. Everything here can be built the moment the container is, because
        // nothing in it needs an asset.
        //
        // currencySaveInputs and legacyCurrencyPlayerPrefsKey follow status's own precedent: both
        // are optional, so no production call site changes, but a test resolving ICurrencyManager
        // against this exact method can redirect both away from the developer's real
        // Application.persistentDataPath and real PlayerPrefs entry instead of reading or
        // clobbering them. What null resolves to is not the same constant in every context - see
        // BuildCurrencySaveService's own DefaultCurrencySaveInputs/DefaultLegacyCurrencyPlayerPrefsKey
        // for why a test that never passes either still cannot reach the real ones, because the one
        // call site that can never pass them at all - Configure(), which is Unity's own callback and
        // takes no arguments - is exactly the one a test reaches by loading the real Boot scene, as
        // GameBootstrapperTests does. See docs/saving.md, "Currency: the first real caller".
        public static void RegisterCoreServices(IContainerBuilder builder, IBootStatus status = null,
            SaveFactoryInputs currencySaveInputs = null, string legacyCurrencyPlayerPrefsKey = null)
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

            // Every SaveScheduler<T> this composition builds registers itself here - see
            // ISaveFlushRegistry - so OnApplicationPause/OnApplicationQuit can flush every one of
            // them without this scope ever naming a concrete scheduler type, including one defined
            // in an assembly Core does not reference. See docs/saving.md, "The pause/quit flush
            // lives on GameLifetimeScope".
            builder.Register<ISaveFlushRegistry, SaveFlushRegistry>(Lifetime.Singleton);

            // Currency's save pipeline: a file-backed, unprotected, non-hopping ISaveService - see
            // docs/saving.md, "What ships, and where the composition asserts its own constraints" -
            // assembled by hand from SaveComponentFactory rather than through SaveServiceFactory,
            // because this composition needs a legacy import that SaveServiceFactory is documented
            // to never grow a parameter for. Built once and registered as an instance: nothing about
            // it needs anything the container itself provides.
            builder.RegisterInstance<ISaveService>(BuildCurrencySaveService(currencySaveInputs, legacyCurrencyPlayerPrefsKey));

            // Coalesces every AddCurrency/TrySpendCurrency call behind at most one write per
            // window, over the ISaveService just registered above. A factory registration rather
            // than plain constructor injection because SaveScheduler<T>'s key argument is a plain
            // string the container has nothing to resolve it from.
            builder.Register<SaveScheduler<CurrencySaveDocument>>(resolver =>
            {
                SaveScheduler<CurrencySaveDocument> scheduler = new(
                    resolver.Resolve<ISaveService>(),
                    CurrencyResourceBankSaveHandle.SaveKey,
                    resolver.Resolve<IGameClock>());

                // Registering is itself the assertion that this scheduler can flush blocking - see
                // ISaveFlushRegistry.Register, which throws if it cannot - so this factory no longer
                // duplicates that check on its own. In the factory rather than beside it so every
                // route to this singleton registers it exactly once, the build callback below and a
                // test's own direct resolve alike.
                resolver.Resolve<ISaveFlushRegistry>().Register(scheduler);

                return scheduler;
            }, Lifetime.Singleton);

            // Forced at container-build time rather than left to whoever happens to resolve the
            // scheduler first. Registering with ISaveFlushRegistry is a side effect of the factory
            // above, so a scheduler nothing resolves is a scheduler nothing registered - and an
            // unregistered save is simply never flushed at pause/quit while looking identical in
            // every other respect. One line here per scheduler this composition owns keeps that a
            // property of the container, assertable through ISaveFlushRegistry.Registered, rather
            // than a line in Awake() somebody has to remember to add alongside the next save.
            builder.RegisterBuildCallback(resolver => resolver.Resolve<SaveScheduler<CurrencySaveDocument>>());

            builder.Register<IResourceBankSaveHandler<CurrencyType>, CurrencyResourceBankSaveHandle>(Lifetime.Singleton);

            // Recorded by GameBootstrapper on every launch, over the same ISaveService currency
            // saves through - see the note on BuildCurrencySaveService below.
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

            // As its interfaces rather than through RegisterEntryPoint: a LifetimeScope installs the
            // dispatcher itself, so the real game still runs this while a container a test builds by
            // hand stays inert.
            builder.Register<GameBootstrapper>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
        }

        // AtomicFile + Json + None: readable, unprotected JSON like a plain FileStore would write,
        // but swapped into place rather than overwritten in place, so a kill mid-write cannot leave
        // a torn currency save behind - a real risk once SaveScheduler<T> is what is doing the
        // writing, since a coalesced write can now land at any point in the app's lifecycle rather
        // than only inside a single synchronous Save() call. See docs/saving.md, "What ships, and
        // where the composition asserts its own constraints", for the rest of this choice.
        //
        // SaveComponentFactory.CreateStore/CreateCodec/CreateProtector rather than
        // SaveServiceFactory: SaveServiceFactory is documented to never grow a parameter for a
        // SaveMigrator or an ILegacyImport, so a composition that needs either - like this one -
        // assembles a SaveService by hand instead, exactly the way docs/saving.md says any future
        // caller with the same need should. No migrator is supplied: CurrentSchemaVersion has never
        // moved past 1, so there is no chain yet to walk - see docs/saving.md, "The migration
        // chain" - only the legacy import phase 4 deferred until this phase had a save model to
        // write one against.
        //
        // inputs and legacyPlayerPrefsKey both default to production's own values exactly where
        // RegisterCoreServices' own optional parameters do - see that method's header - so this
        // private helper carries no defaulting logic of its own beyond resolving what a null means.
        // That resolution is not the same constant in every context - see DefaultCurrencySaveInputs
        // and DefaultLegacyCurrencyPlayerPrefsKey below for why a caller that explicitly passes null
        // does not always get the same answer production does.
        //
        // Kept its currency-flavoured name rather than renamed to something like BuildSaveService:
        // the one ISaveService this builds is now the single registered service every key in this
        // composition saves through - meta and the chests minigame's own scheduler resolve it too,
        // not only currency's. Renaming this and its two overrides below would touch
        // GameBootstrapperTests, GameLifetimeScopeTests and this file's own docs for no behavioural
        // gain, so the name stays and this comment says what it actually governs now.
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

        // Sealing the boot path a test cannot pass arguments through. Configure() is Unity's own
        // callback - LifetimeScope.Awake() calls it with no way for a caller to thread an override
        // through - so RegisterCoreServices(builder, status) is the exact call GameBootstrapperTests
        // makes the moment it loads the real Boot scene, indistinguishable, from inside this method,
        // from a player launching the real game. currencySaveInputs and legacyCurrencyPlayerPrefsKey
        // being optional parameters (see RegisterCoreServices' own header) closes the path a test
        // that builds its own ContainerBuilder takes; it does nothing for this one, because nothing
        // GameBootstrapperTests does can reach past Configure()'s fixed signature to supply either.
        //
        // The two static overrides below are what such a test has instead of arguments. See
        // docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
        // Set by a test before it loads the Boot scene, cleared in its teardown. Null in every
        // other run, including every real player's and every ordinary Editor Play session, so what
        // ships is unchanged. Explicit rather than inferred from a scripting define: UNITY_INCLUDE_TESTS
        // is set for the whole Editor compilation, not only for a test run - verified in this
        // project's own generated DefineConstants - so keying off it would quietly redirect a
        // developer's ordinary Play session away from their real save too.
        public static SaveFactoryInputs CurrencySaveInputsOverride { get; set; }

        public static string LegacyCurrencyPlayerPrefsKeyOverride { get; set; }

        private static SaveFactoryInputs DefaultCurrencySaveInputs() =>
            CurrencySaveInputsOverride ?? SaveFactoryInputs.Defaults();

        private static string DefaultLegacyCurrencyPlayerPrefsKey() =>
            string.IsNullOrEmpty(LegacyCurrencyPlayerPrefsKeyOverride)
                ? CurrencyLegacyImport.DefaultLegacyKey
                : LegacyCurrencyPlayerPrefsKeyOverride;

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
