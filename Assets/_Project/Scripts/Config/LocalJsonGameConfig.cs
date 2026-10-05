using System;
using Newtonsoft.Json;
using Company.ChestGame.Common;
using Company.ChestGame.Config.Internal;

namespace Company.ChestGame.Config
{
    /// <summary>
    /// Parses and validates GameConfig.json. Where the document came from is
    /// <c>IGameConfigSource</c>'s problem: this takes the document, not the source.
    /// </summary>
    /// <exception cref="GameConfigException">
    /// When the document is empty, is not valid JSON, parses to nothing, or has a reward that is
    /// not positive.
    /// </exception>
    /// <remarks>
    /// See docs/architecture.md, "Config pipeline".
    /// </remarks>
    public class LocalJsonGameConfig : IGameConfig
    {
        /// <summary>
        /// The gems awarded per win. Always positive.
        /// </summary>
        public long GemsReward { get; }
        /// <summary>
        /// The coins awarded per win. Always positive.
        /// </summary>
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

        /// <summary>
        /// Requires every reward to be positive, so a field the document omits is refused too.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Config pipeline".
        /// </remarks>
        private static void Validate(GameConfigData data)
        {
            ConfigValidation.Require(data.GemsReward > 0, nameof(data.GemsReward), data.GemsReward);
            ConfigValidation.Require(data.CoinsReward > 0, nameof(data.CoinsReward), data.CoinsReward);
        }
    }
}
