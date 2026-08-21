using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Popups.Internal
{
    // Fetches the authored popup list through the asset provider. The only place that knows the key.
    public class AddressablesPopupListSource : IPopupListSource
    {
        private const string LIST_KEY = "Popups/PopupList";

        private readonly IAssetProvider _assets;

        public AddressablesPopupListSource(IAssetProvider assets) => _assets = assets;

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
