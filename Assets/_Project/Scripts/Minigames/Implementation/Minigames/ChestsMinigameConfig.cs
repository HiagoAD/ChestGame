using System;
using Company.ChestGame.Common;
using Newtonsoft.Json;

namespace Company.ChestGame.Minigame.Chests
{
    /// <summary>
    /// The chests minigame's own config document, parsed and validated by the minigame that owns it.
    /// </summary>
    /// <remarks>
    /// See docs/minigames.md, "Its own config document".
    /// </remarks>
    public class ChestsMinigameConfig
    {
        [JsonProperty] public int ChestCount { get; private set; }
        [JsonProperty] public int AttempsCount { get; private set; }
        [JsonProperty] public int TimeToOpenChestMiliseconds { get; private set; }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        private ChestsMinigameConfig()
        {
        }

        public static ChestsMinigameConfig Create(int chestCount, int attempsCount, int timeToOpenChestMiliseconds)
        {
            ChestsMinigameConfig config = new()
            {
                ChestCount = chestCount,
                AttempsCount = attempsCount,
                TimeToOpenChestMiliseconds = timeToOpenChestMiliseconds
            };

            config.Validate();

            return config;
        }

        public static ChestsMinigameConfig Parse(string document)
        {
            if (string.IsNullOrEmpty(document))
            {
                throw new GameConfigException("No chests minigame config document was found, make sure the minigame definition points at one");
            }

            ChestsMinigameConfig parsedObject;
            try
            {
                parsedObject = JsonConvert.DeserializeObject<ChestsMinigameConfig>(document);
            }
            catch (JsonException exception)
            {
                throw new GameConfigException("The chests minigame config document is not valid JSON", exception);
            }

            if (parsedObject == null)
            {
                throw new GameConfigException("The chests minigame config document parsed to nothing");
            }

            parsedObject.Validate();

            return parsedObject;
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        private void Validate()
        {
            ConfigValidation.Require(ChestCount > 0, nameof(ChestCount), ChestCount);
            ConfigValidation.Require(AttempsCount > 0, nameof(AttempsCount), AttempsCount);
            ConfigValidation.Require(TimeToOpenChestMiliseconds >= 0, nameof(TimeToOpenChestMiliseconds), TimeToOpenChestMiliseconds);
        }
    }
}
