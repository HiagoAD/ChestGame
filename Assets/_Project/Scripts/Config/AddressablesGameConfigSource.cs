using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Config
{
    /// <summary>
    /// Fetches the config document through the asset provider.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// </remarks>
    public class AddressablesGameConfigSource : IGameConfigSource
    {
        private const string CONFIG_KEY = "GameConfig";

        private readonly IAssetProvider _assets;

        public AddressablesGameConfigSource(IAssetProvider assets) => _assets = assets;

        /// <summary>
        /// Returns null when the config's document slot was found but is empty.
        /// </summary>
        /// <exception cref="AssetLoadException">The key resolved but the load itself failed.</exception>
        /// <exception cref="MissingAssetException">The key is not in the shipped catalog.</exception>
        /// <remarks>
        /// See docs/architecture.md, "Config pipeline".
        /// </remarks>
        public async UniTask<string> ReadAsync(CancellationToken ct)
        {
            TextAsset asset = await _assets.LoadAsync<TextAsset>(CONFIG_KEY, ct);

            return asset == null ? null : asset.text;
        }
    }
}
