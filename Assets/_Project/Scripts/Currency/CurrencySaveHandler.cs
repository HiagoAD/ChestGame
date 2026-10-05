using System.Threading;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Currency
{
    // Bridges CurrencyManager's fully synchronous ICurrencySaveHandler onto the fully asynchronous
    // ISaveService. See docs/saving.md, "Currency: the first real caller".
    //
    // Save() never blocks: the document goes to the scheduler, which writes it later. Load() blocks
    // the calling thread until the load finishes; CurrencyManager calls it once, from its own
    // constructor. The constructor refuses any ISaveService whose CompletesOnCallingThread is
    // false, which is what makes that block safe.
    public class CurrencySaveHandler : ICurrencySaveHandler
    {
        // Distinct from the legacy PlayerPrefs key CurrencyLegacyImport reads from.
        public const string SaveKey = "currency";

        private readonly ISaveService _saveService;
        private readonly SaveScheduler<CurrencySaveDocument> _scheduler;

        public CurrencySaveHandler(ISaveService saveService, SaveScheduler<CurrencySaveDocument> scheduler)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            if (scheduler == null) throw SaveException.NoScheduler();

            // Checked once, at construction, rather than at the first Load().
            if (!saveService.CompletesOnCallingThread) throw SaveException.SynchronousLoadNeedsNonHoppingStore();

            _saveService = saveService;
            _scheduler = scheduler;
        }

        // Not copied here: ICurrencySaveHandler.Save is handed a document nothing else holds, so the
        // scheduler is free to keep it until it writes.
        public void Save(CurrencySaveDocument document)
        {
            _scheduler.MarkDirty(document);
        }

        public CurrencySaveDocument Load()
        {
            // Safe to block on: the guard above already refused any composition where this could
            // still be Pending by the time GetResult() runs.
            return _saveService
                .LoadAsync<CurrencySaveDocument>(SaveKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }
}
