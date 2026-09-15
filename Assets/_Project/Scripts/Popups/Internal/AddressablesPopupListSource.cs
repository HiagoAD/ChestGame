using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups.Internal
{
    /// <summary>
    /// Fetches the authored popup list through the asset provider.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Catalogs".
    /// </remarks>
    public class AddressablesPopupListSource : IPopupListSource
    {
        private const string LIST_KEY = "Popups/PopupList";

        private readonly IAssetProvider _assets;

        public AddressablesPopupListSource(IAssetProvider assets) => _assets = assets;

        /// <exception cref="AssetLoadException">The key resolved but the load itself failed.</exception>
        /// <exception cref="MissingAssetException">
        /// The key is not in the shipped catalog, or the loaded asset resolves to null.
        /// </exception>
        public async UniTask<IReadOnlyList<PopupBase>> ReadAsync(CancellationToken ct)
        {
            PopupListSO popupListSO = await _assets.LoadAsync<PopupListSO>(LIST_KEY, ct);
            if (popupListSO == null)
            {
                throw new MissingAssetException(LIST_KEY, "Popup list");
            }

            return popupListSO.Entries;
        }
    }
}
