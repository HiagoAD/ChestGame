using System.Threading;
using Company.ChestGame.Saving;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    // Bridges ResourceBank's fully synchronous IResourceBankSaveHandler<T> onto the fully
    // asynchronous ISaveService. See docs/saving.md, "Currency: the first real caller".
    //
    // Save() never blocks: MarkDirty returns immediately and coalesces. Load() blocks the calling
    // thread once, inside ResourceBank's constructor, because a `ResourceBankState<T> Load()`
    // signature has no UniTask to hand the wait back through. The constructor refuses any
    // ISaveService whose CompletesOnCallingThread is false, so that block is always safe by the
    // time Load() runs.
    public class CurrencyResourceBankSaveHandle : IResourceBankSaveHandler<CurrencyType>
    {
        // Distinct from the legacy PlayerPrefs key CurrencyLegacyImport reads from.
        public const string SaveKey = "currency";

        private readonly ISaveService _saveService;
        private readonly SaveScheduler<CurrencySaveDocument> _scheduler;

        public CurrencyResourceBankSaveHandle(ISaveService saveService, SaveScheduler<CurrencySaveDocument> scheduler)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            if (scheduler == null) throw SaveException.NoScheduler();

            // Checked once, at construction, rather than at the first Load().
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
