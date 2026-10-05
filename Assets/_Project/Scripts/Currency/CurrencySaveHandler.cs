using System.Threading;
using Company.ChestGame.Saving;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Bridges CurrencyManager's fully synchronous <see cref="ICurrencySaveHandler"/> onto the fully
    /// asynchronous <see cref="ISaveService"/>.
    /// </summary>
    /// <remarks>
    /// Save() never blocks: the document goes to the scheduler, which writes it later. Load() blocks
    /// the calling thread until the load finishes; CurrencyManager calls it once, from its own
    /// constructor. The constructor refuses any ISaveService whose CompletesOnCallingThread is
    /// false, which is what makes that block safe.
    /// See docs/saving.md, "Currency: the first real caller".
    /// </remarks>
    public class CurrencySaveHandler : ICurrencySaveHandler
    {
        /// <summary>
        /// The key the currency document is saved under. Distinct from the legacy PlayerPrefs key
        /// <see cref="CurrencyLegacyImport"/> reads from.
        /// </summary>
        public const string SaveKey = "currency";

        private readonly ISaveService _saveService;
        private readonly SaveScheduler<CurrencySaveDocument> _scheduler;

        /// <exception cref="SaveException">
        /// When either argument is null, or when <paramref name="saveService"/> does not complete on
        /// the calling thread.
        /// </exception>
        public CurrencySaveHandler(ISaveService saveService, SaveScheduler<CurrencySaveDocument> scheduler)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            if (scheduler == null) throw SaveException.NoScheduler();

            if (!saveService.CompletesOnCallingThread) throw SaveException.SynchronousLoadNeedsNonHoppingStore();

            _saveService = saveService;
            _scheduler = scheduler;
        }

        /// <summary>
        /// Hands the document to the scheduler without copying it, and returns without writing.
        /// The scheduler is free to keep it until it writes.
        /// </summary>
        public void Save(CurrencySaveDocument document)
        {
            _scheduler.MarkDirty(document);
        }

        /// <summary>
        /// Blocks the calling thread until the load finishes. Safe because the constructor already
        /// refused any composition where the load could still be pending.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Currency: the first real caller".
        /// </remarks>
        public CurrencySaveDocument Load()
        {
            return _saveService
                .LoadAsync<CurrencySaveDocument>(SaveKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }
}
