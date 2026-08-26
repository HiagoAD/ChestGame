using System;
using Newtonsoft.Json;
using Company.ChestGame.Common;
using Company.ChestGame.Config.Internal;

namespace Company.ChestGame.Config
{
    // Parses and validates GameConfig.json. Where the document came from is IGameConfigSource's
    // problem, and it takes the document rather than the source so parse-and-validate stays a
    // synchronous constructor. A real remote config would likely need callbacks here.
    public class LocalJsonGameConfig : IGameConfig
    {
        public long GemsReward { get; }
        public long CoinsReward { get; }

        public LocalJsonGameConfig(string document)
        {
            if (string.IsNullOrEmpty(document))
            {
                throw new GameConfigException("No game config document was found, make sure the configured source can reach one");
            }

            GameConfigData parsedObject;
            try
            {
                parsedObject = JsonConvert.DeserializeObject<GameConfigData>(document);
            }
            catch (JsonException exception)
            {
                throw new GameConfigException("The game config document is not valid JSON", exception);
            }

            if (parsedObject == null)
            {
                throw new GameConfigException("The game config document parsed to nothing");
            }

            Validate(parsedObject);

            GemsReward = parsedObject.GemsReward;
            CoinsReward = parsedObject.CoinsReward;
        }

        // A negative reward reaches AddCurrency, which rejects it and logs an error on every win.
        private static void Validate(GameConfigData data)
        {
            ConfigValidation.Require(data.GemsReward >= 0, nameof(data.GemsReward), data.GemsReward);
            ConfigValidation.Require(data.CoinsReward >= 0, nameof(data.CoinsReward), data.CoinsReward);
        }
    }
}
