using System;
using Company.ChestGame.Common;
using Newtonsoft.Json;

namespace Company.ChestGame.Minigame.Chests
{
    // The chests minigame's own config document, parsed and validated by the minigame that owns it.
    //
    // These three values mean nothing to the rest of the game, so nothing else gets to name them.
    // Keeping the document here is also what lets this minigame ship as a self-contained unit
    // later: the definition asset points at its own TextAsset and no shared config has to know.
    //
    // Immutable once built, like LocalJsonGameConfig: "validated" has to be a durable guarantee,
    // not something a later assignment can undo. Create and Parse are the only two ways in and
    // both validate.
    public class ChestsMinigameConfig
    {
        [JsonProperty] public int ChestCount { get; private set; }
        [JsonProperty] public int AttempsCount { get; private set; }
        [JsonProperty] public int TimeToOpenChestMiliseconds { get; private set; }

        // Private and parameterless on purpose. Json.NET picks a public parameterized constructor
        // when it finds one, so validation living in a constructor would run during deserialization
        // and surface wrapped in JsonSerializationException, which Parse would then report as "not
        // valid JSON". Validate() runs after deserialization instead.
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
        // renamed, or one this client predates, deserializes to 0. Zero chests or zero attempts is
        // a round that can never be played or never end, so it is rejected at the boundary rather
        // than surfacing later as a stuck game.
        private void Validate()
        {
            ConfigValidation.Require(ChestCount > 0, nameof(ChestCount), ChestCount);
            ConfigValidation.Require(AttempsCount > 0, nameof(AttempsCount), AttempsCount);
            ConfigValidation.Require(TimeToOpenChestMiliseconds >= 0, nameof(TimeToOpenChestMiliseconds), TimeToOpenChestMiliseconds);
        }
    }
}
