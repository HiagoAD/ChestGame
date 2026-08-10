using Company.ChestGame.Config;

namespace Company.ChestGame.Tests.Common
{
    // Hands LocalJsonGameConfig whatever document a test wants it to see, including none at all.
    public class FakeGameConfigSource : IGameConfigSource
    {
        public const string ValidDocument = @"{
            ""ChestCount"": 12,
            ""AttempsCount"": 12,
            ""TimeToOpenChestMiliseconds"": 1000,
            ""GemsReward"": 10,
            ""CoinsReward"": 50
        }";

        public string Document { get; set; } = ValidDocument;

        public int ReadCallCount { get; private set; }

        public string Read()
        {
            ReadCallCount++;
            return Document;
        }
    }
}
