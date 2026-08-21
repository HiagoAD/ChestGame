using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Config
{
    // Fetches the config document through the asset provider. This is the local stand-in for
    // whatever a real deployment would use, an HTTP fetch against a remote config service.
    //
    // It is the only place that knows the key, which is the whole of what it owns: the provider
    // decides how a key becomes bytes, and the parser decides what the bytes mean.
    public class AddressablesGameConfigSource : IGameConfigSource
    {
        private const string CONFIG_KEY = "GameConfig";

        private readonly IAssetProvider _assets;

        public AddressablesGameConfigSource(IAssetProvider assets) => _assets = assets;

        public async UniTask<string> ReadAsync(CancellationToken ct)
        {
            TextAsset asset = await _assets.LoadAsync<TextAsset>(CONFIG_KEY, ct);

            // A provider that hands back nothing is still reported as a null document rather than a
            // throw, because "no config shipped" is the parser's failure to describe. A key that is
            // not in the catalog at all never gets this far: the provider throws first.
            return asset == null ? null : asset.text;
        }
    }
}
