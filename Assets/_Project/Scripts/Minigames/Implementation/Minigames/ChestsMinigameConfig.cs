using System;
using Company.ChestGame.Common;
using Newtonsoft.Json;

namespace Company.ChestGame.Minigame.Chests
{
    // The chests minigame's own config document, parsed and validated by the minigame that owns it.
    // Immutable once built, like LocalJsonGameConfig, so "validated" is a durable guarantee. Create
    // and Parse are the only two ways in and both validate.
    public class ChestsMinigameConfig
    {
        [JsonProperty] public int ChestCount { get; private set; }
        [JsonProperty] public int AttempsCount { get; private set; }
        [JsonProperty] public int TimeToOpenChestMiliseconds { get; private set; }

        // Private and parameterless on purpose: Json.NET picks a public parameterized constructor
        // when it finds one, so validation in a constructor would surface wrapped in
        // JsonSerializationException and Parse would report it as "not valid JSON".
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

        // A document can parse cleanly and still describe an unplayable round: a field the server
        // renamed, or one this client predates, deserializes to 0.
        private void Validate()
        {
            ConfigValidation.Require(ChestCount > 0, nameof(ChestCount), ChestCount);
            ConfigValidation.Require(AttempsCount > 0, nameof(AttempsCount), AttempsCount);
            ConfigValidation.Require(TimeToOpenChestMiliseconds >= 0, nameof(TimeToOpenChestMiliseconds), TimeToOpenChestMiliseconds);
        }
    }
}
