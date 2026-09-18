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
using Company.ChestGame.UI;
using TapNation.Modules.ResourceBank.Saving;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// Root <see cref="LifetimeScope"/>, and the only one authored in a scene. Registration is split
    /// in two: <see cref="RegisterCoreServices"/> is everything buildable immediately,
    /// <see cref="RegisterLoadedServices"/> is everything that needs content to exist first.
    /// </summary>
    /// <remarks>
    /// Place it in the boot scene; it survives every scene load afterwards, and every other scope in
    /// the game descends from it.
    /// See docs/architecture.md, "Boot".
    /// See docs/architecture.md, "Registration, in two halves".
    /// </remarks>
    public class GameLifetimeScope : LifetimeScope
    {
        /// <remarks>
        /// See docs/saving.md, "The pause/quit flush lives on GameLifetimeScope".
        /// </remarks>
        private ISaveFlushRegistry _saveFlushRegistry;

        /// <remarks>
        /// See docs/saving.md, "The pause/quit flush lives on GameLifetimeScope".
        /// </remarks>
        protected override void Awake()
        {
            base.Awake();

            DontDestroyOnLoad(gameObject);

            _saveFlushRegistry = Container.Resolve<ISaveFlushRegistry>();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) FlushAllSavesOrLog();
        }

        private void OnApplicationQuit() => FlushAllSavesOrLog();

        /// <remarks>
        /// See docs/saving.md, "The pause/quit flush lives on GameLifetimeScope".
        /// </remarks>
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

        /// <summary>
        /// Wire in the inspector to show boot progress. Left empty, boot reports nowhere.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [SerializeField] private BootStatusLabel _bootStatus;

        /// <summary>
        /// Builds the boot-scene container by delegating to <see cref="RegisterCoreServices"/>.
        /// </summary>
        /// <param name="builder">The container builder VContainer provides.</param>
        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        protected override void Configure(IContainerBuilder builder) =>
            RegisterCoreServices(builder, _bootStatus != null ? _bootStatus : null,
                CurrencySaveInputsOverride, LegacyCurrencyPlayerPrefsKeyOverride);

        /// <summary>
        /// Registers everything buildable the moment the container is, needing no loaded asset.
        /// </summary>
        /// <param name="builder">The container builder to register into.</param>
        /// <param name="status">Where boot progress is reported. Defaults to a no-op when null.</param>
        /// <param name="currencySaveInputs">
        /// Left null for the real game. Pass a value to redirect the currency save composition away
        /// from the real save location.
        /// </param>
        /// <param name="legacyCurrencyPlayerPrefsKey">
        /// Left null for the real game. Pass a value to redirect the legacy currency import away from
        /// the real PlayerPrefs entry.
        /// </param>
        /// <remarks>
        /// Public and separate from <see cref="Configure"/> so a caller can build the same container
        /// without a scene. Any caller that resolves a save or a currency service must leave
        /// <paramref name="currencySaveInputs"/> and <paramref name="legacyCurrencyPlayerPrefsKey"/>
        /// null, or it reads and overwrites the running player's own data.
        /// See docs/architecture.md, "Registration, in two halves".
        /// See docs/architecture.md, "Engine seams: clock and random".
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// See docs/context/self-contained-minigames.md, "5. The agreement this work added".
        /// See docs/context/self-contained-minigames.md, "4. First attempts that were replaced".
        /// See docs/saving.md, "Why the flush stopped being one field".
        /// See docs/saving.md, "An unregistered save looks exactly like a registered one".
        /// </remarks>
        public static void RegisterCoreServices(IContainerBuilder builder, IBootStatus status = null,
            SaveFactoryInputs currencySaveInputs = null, string legacyCurrencyPlayerPrefsKey = null)
        {
            builder.RegisterInstance<IBootStatus>(status ?? new SilentBootStatus());

            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            builder.Register<IGameClock, UnityGameClock>(Lifetime.Singleton);

            builder.Register<IAssetProvider, AddressablesAssetProvider>(Lifetime.Singleton);

            builder.Register<IGameConfigSource, AddressablesGameConfigSource>(Lifetime.Singleton);
            builder.Register<IMinigameListSource, AddressablesMinigameListSource>(Lifetime.Singleton);
            builder.Register<IPopupListSource, AddressablesPopupListSource>(Lifetime.Singleton);
            builder.Register<IPopupParentSource, AddressablesPopupParentSource>(Lifetime.Singleton);

            builder.Register<ISaveFlushRegistry, SaveFlushRegistry>(Lifetime.Singleton);

            builder.RegisterInstance<ISaveService>(BuildCurrencySaveService(currencySaveInputs, legacyCurrencyPlayerPrefsKey));

            builder.Register<SaveScheduler<CurrencySaveDocument>>(resolver =>
            {
                SaveScheduler<CurrencySaveDocument> scheduler = new(
                    resolver.Resolve<ISaveService>(),
                    CurrencyResourceBankSaveHandle.SaveKey,
                    resolver.Resolve<IGameClock>());

                resolver.Resolve<ISaveFlushRegistry>().Register(scheduler);

                return scheduler;
            }, Lifetime.Singleton);

            builder.RegisterBuildCallback(resolver => resolver.Resolve<SaveScheduler<CurrencySaveDocument>>());

            builder.Register<IResourceBankSaveHandler<CurrencyType>, CurrencyResourceBankSaveHandle>(Lifetime.Singleton);

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
            builder.Register<CurrencyLabelControllerFactory>(Lifetime.Singleton);

            builder.Register<GameContentLoader>(Lifetime.Singleton);

            builder.Register<GameBootstrapper>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
        }

        /// <summary>
        /// Builds the single <see cref="ISaveService"/> every save key in this composition shares,
        /// despite the currency-flavoured name.
        /// </summary>
        /// <param name="inputs">Save file inputs. Defaults to the real save location when null.</param>
        /// <param name="legacyPlayerPrefsKey">
        /// The legacy PlayerPrefs key to import from. Defaults to the real key when null.
        /// </param>
        /// <remarks>
        /// Readable, unprotected JSON, written so a kill mid-write cannot leave a torn file behind.
        /// See docs/saving.md, "What ships, and where the composition asserts its own constraints".
        /// See docs/saving.md, "One ISaveService, three keys - and a naming debt".
        /// </remarks>
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

        /// <summary>
        /// Redirects <see cref="RegisterCoreServices"/>'s currency save composition away from the
        /// real save location, for a caller loading the boot scene that cannot pass arguments through
        /// <see cref="Configure"/>. <see cref="LegacyCurrencyPlayerPrefsKeyOverride"/> is its
        /// companion for the legacy PlayerPrefs key.
        /// </summary>
        /// <remarks>
        /// Assign both before the scene loads and clear them afterwards; they are global and outlive
        /// whatever set them. Both must be null in a real run, or a player's own save is redirected
        /// somewhere they cannot see. Do not make them conditional on a scripting define:
        /// UNITY_INCLUDE_TESTS is set for the whole editor compilation, not only while tests run.
        /// See docs/saving.md, "Redirecting this composition away from a developer's real save".
        /// See docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
        /// </remarks>
        public static SaveFactoryInputs CurrencySaveInputsOverride { get; set; }

        public static string LegacyCurrencyPlayerPrefsKeyOverride { get; set; }

        private static SaveFactoryInputs DefaultCurrencySaveInputs() =>
            CurrencySaveInputsOverride ?? SaveFactoryInputs.Defaults();

        private static string DefaultLegacyCurrencyPlayerPrefsKey() =>
            string.IsNullOrEmpty(LegacyCurrencyPlayerPrefsKeyOverride)
                ? CurrencyLegacyImport.DefaultLegacyKey
                : LegacyCurrencyPlayerPrefsKeyOverride;

        /// <summary>
        /// Registers the half that needs content: everything derived from a loaded asset, plus the
        /// services built from them.
        /// </summary>
        /// <param name="builder">The container builder to register into.</param>
        /// <param name="content">The already-loaded content to register services from.</param>
        /// <remarks>
        /// Everything derived from a loaded asset is registered already built, so nothing here can
        /// exist without its data.
        /// See docs/architecture.md, "Registration, in two halves".
        /// </remarks>
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
