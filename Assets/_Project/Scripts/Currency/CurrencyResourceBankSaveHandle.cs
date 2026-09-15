using System.Threading;
using Company.ChestGame.Saving;
using TapNation.Modules.ResourceBank.Saving;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Bridges ResourceBank's fully synchronous <see cref="IResourceBankSaveHandler{T}"/> onto the
    /// fully asynchronous <see cref="ISaveService"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Save"/> never blocks: the state goes to the scheduler, which writes it later.
    /// <see cref="Load"/> blocks the calling thread until the load finishes; ResourceBank calls it
    /// once, from its own constructor. The constructor refuses any <see cref="ISaveService"/> whose
    /// <c>CompletesOnCallingThread</c> is false, which is what makes that block safe.
    /// See docs/saving.md, "IResourceBankSaveHandler&lt;T&gt; is fully synchronous; ISaveService is not".
    /// </remarks>
    public class CurrencyResourceBankSaveHandle : IResourceBankSaveHandler<CurrencyType>
    {
        /// <summary>
        /// Distinct from the legacy PlayerPrefs key <see cref="CurrencyLegacyImport"/> reads from.
        /// </summary>
        public const string SaveKey = "currency";

        private readonly ISaveService _saveService;
        private readonly SaveScheduler<CurrencySaveDocument> _scheduler;

        /// <remarks>
        /// See docs/saving.md, "IResourceBankSaveHandler&lt;T&gt; is fully synchronous; ISaveService is not".
        /// </remarks>
        public CurrencyResourceBankSaveHandle(ISaveService saveService, SaveScheduler<CurrencySaveDocument> scheduler)
        {
            if (saveService == null) throw SaveException.NoSaveService();
            if (scheduler == null) throw SaveException.NoScheduler();

            if (!saveService.CompletesOnCallingThread) throw SaveException.SynchronousLoadNeedsNonHoppingStore();

            _saveService = saveService;
            _scheduler = scheduler;
        }

        public void Save(ResourceBankState<CurrencyType> data)
        {
            _scheduler.MarkDirty(CurrencySaveDocument.From(data));
        }

        /// <remarks>
        /// See docs/saving.md, "IResourceBankSaveHandler&lt;T&gt; is fully synchronous; ISaveService is not".
        /// </remarks>
        public ResourceBankState<CurrencyType> Load()
        {
            CurrencySaveDocument document = _saveService
                .LoadAsync<CurrencySaveDocument>(SaveKey, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return new ResourceBankState<CurrencyType>(document.ResourceAmount);
        }
    }
}
