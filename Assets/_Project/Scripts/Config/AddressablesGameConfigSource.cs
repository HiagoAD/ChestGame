using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Config
{
    // Fetches the config document through the asset provider, and is the only place that knows the
    // key. The local stand-in for an HTTP fetch against a real remote config service.
    public class AddressablesGameConfigSource : IGameConfigSource
    {
        private const string CONFIG_KEY = "GameConfig";

        private readonly IAssetProvider _assets;

        public AddressablesGameConfigSource(IAssetProvider assets) => _assets = assets;

        public async UniTask<string> ReadAsync(CancellationToken ct)
        {
            TextAsset asset = await _assets.LoadAsync<TextAsset>(CONFIG_KEY, ct);

            // Null rather than a throw, because "no config shipped" is the parser's failure to
            // describe. A key that is not in the catalog never gets this far.
            return asset == null ? null : asset.text;
        }
    }
}
