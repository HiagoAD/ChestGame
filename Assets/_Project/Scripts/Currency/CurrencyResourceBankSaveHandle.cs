using System.Threading;
using Company.ChestGame.Saving;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    // Bridges ResourceBank's fully synchronous IResourceBankSaveHandler<T> onto
    // Company.ChestGame.Saving's fully asynchronous ISaveService. Lives here rather than in Saving
    // because this is the one assembly allowed to know both CurrencyType and ISaveService - see
    // docs/saving.md, "Currency: the first real caller".
    //
    // The two directions are resolved differently, and each costs something:
    //
    // Save() never blocks: it hands the state to the SaveScheduler<CurrencySaveDocument> this was
    // built with via MarkDirty, which returns immediately and coalesces every call inside one
    // window into at most one real write. See SaveScheduler<T> for what a crash inside that window
    // costs, and GameLifetimeScope for where the window gets force-flushed on pause and quit.
    //
    // Load() blocks the calling thread once, synchronously, inside ResourceBank's own constructor -
    // there is no other way to satisfy a `ResourceBankState<T> Load()` signature that returns a
    // value rather than a UniTask. That is only safe when the ISaveService it blocks on always
    // finishes on the calling thread (ISaveService.CompletesOnCallingThread), which is why the
    // constructor below refuses outright, rather than merely warning, whenever that is not true for
    // whatever it was actually given - a ThreadHoppingStore-backed composition cannot satisfy this
    // contract without either blocking the one thread that would have to service its own
    // continuation (a deadlock) or lying about being synchronous (returning before the real load
    // finished). This composition is refused structurally, at the moment it is wired, rather than
    // shipped to find out which of those two ways it actually fails.
    public class CurrencyResourceBankSaveHandle : IResourceBankSaveHandler<CurrencyType>
    {
        // Its own key, distinct from the legacy PlayerPrefs key CurrencyLegacyImport reads from -
        // see docs/saving.md, "The legacy import".
        public const string SaveKey = "currency";

        private readonly ISaveService _saveService;
        private readonly SaveScheduler<CurrencySaveDocument> _scheduler;

        public CurrencyResourceBankSaveHandle(ISaveService saveService, SaveScheduler<CurrencySaveDocument> scheduler)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            if (scheduler == null) throw SaveException.NoScheduler();

            // Asserted once, here, rather than left to actually deadlock the first time a hopping
            // composition is paired with a handler that blocks on its result - see the type header.
            if (!saveService.CompletesOnCallingThread) throw SaveException.SynchronousLoadNeedsNonHoppingStore();

            _saveService = saveService;
            _scheduler = scheduler;
        }

        public void Save(ResourceBankState<CurrencyType> data)
        {
            _scheduler.MarkDirty(CurrencySaveDocument.From(data));
        }

        public ResourceBankState<CurrencyType> Load()
        {
            // Safe to block on: the guard above already refused any composition where this could
            // still be Pending by the time GetResult() runs.
            CurrencySaveDocument document = _saveService
                .LoadAsync<CurrencySaveDocument>(SaveKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new ResourceBankState<CurrencyType>(document.ResourceAmount);
        }
    }
}
