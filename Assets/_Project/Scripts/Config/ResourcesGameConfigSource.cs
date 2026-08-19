using UnityEngine;

namespace Company.ChestGame.Config
{
    // Reads the config document out of a Resources folder. This is the local stand-in for whatever
    // a real deployment would use, an HTTP fetch against a remote config service.
    public class ResourcesGameConfigSource : IGameConfigSource
    {
        private const string FILE_NAME = "GameConfig";

        public string Read()
        {
            TextAsset asset = Resources.Load<TextAsset>(FILE_NAME);
            return asset == null ? null : asset.text;
        }
    }
}
