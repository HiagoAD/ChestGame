using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Config
{
    // Reads the config document out of a Resources folder. This is the local stand-in for whatever
    // a real deployment would use, an HTTP fetch against a remote config service.
    //
    // The read is synchronous and the completed task says so. A missing document is reported as a
    // null rather than a throw, because "no config shipped" is the parser's failure to describe.
    public class ResourcesGameConfigSource : IGameConfigSource
    {
        private const string FILE_NAME = "GameConfig";

        public UniTask<string> ReadAsync(CancellationToken ct)
        {
            TextAsset asset = Resources.Load<TextAsset>(FILE_NAME);
            return UniTask.FromResult(asset == null ? null : asset.text);
        }
    }
}
