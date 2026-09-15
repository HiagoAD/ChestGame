using System;
using Newtonsoft.Json;
using Company.ChestGame.Common;
using Company.ChestGame.Config.Internal;

namespace Company.ChestGame.Config
{
    /// <summary>
    /// Parses and validates GameConfig.json.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// </remarks>
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

        private static void Validate(GameConfigData data)
        {
            ConfigValidation.Require(data.GemsReward >= 0, nameof(data.GemsReward), data.GemsReward);
            ConfigValidation.Require(data.CoinsReward >= 0, nameof(data.CoinsReward), data.CoinsReward);
        }
    }
}
