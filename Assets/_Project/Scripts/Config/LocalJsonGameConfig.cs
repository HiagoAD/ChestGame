using System;
using Newtonsoft.Json;
using Company.ChestGame.Config.Internal;

namespace Company.ChestGame.Config
{
    // This class simulates what a remote config loader would look like.
    // Right now is simplified to just parse a Data.json document, with a flag to indicate
    // if the data was loaded. In the case of a proper implementation, callbacks might
    // be needed, depending on the game structure.
    //
    // Where the document comes from is the IGameConfigSource's problem, so pointing the game at a
    // remote config service means registering a different source and changing nothing here.
    public class LocalJsonGameConfig : IGameConfig
    {
        public bool Initialized { get; }

        public int ChestCount { get; }
        public int AttempsCount { get; }
        public int TimeToOpenChestMiliseconds { get; }
        public long GemsReward { get; }
        public long CoinsReward { get; }

        public LocalJsonGameConfig(IGameConfigSource source)
        {
            string document = source.Read();
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

            ChestCount = parsedObject.ChestCount;
            AttempsCount = parsedObject.AttempsCount;
            TimeToOpenChestMiliseconds = parsedObject.TimeToOpenChestMiliseconds;
            GemsReward = parsedObject.GemsReward;
            CoinsReward = parsedObject.CoinsReward;

            Initialized = true;
        }

        // A document can parse cleanly and still describe an unplayable game: a field the server
        // renamed, or one this client predates, deserializes to 0. Zero chests or zero attempts is
        // a round that can never be played or never end, so it is rejected at the boundary rather
        // than surfacing later as a stuck game.
        private static void Validate(GameConfigData data)
        {
            Require(data.ChestCount > 0, nameof(data.ChestCount), data.ChestCount);
            Require(data.AttempsCount > 0, nameof(data.AttempsCount), data.AttempsCount);
            Require(data.TimeToOpenChestMiliseconds >= 0, nameof(data.TimeToOpenChestMiliseconds), data.TimeToOpenChestMiliseconds);
            Require(data.GemsReward >= 0, nameof(data.GemsReward), data.GemsReward);
            Require(data.CoinsReward >= 0, nameof(data.CoinsReward), data.CoinsReward);
        }

        private static void Require(bool satisfied, string fieldName, long actualValue)
        {
            if (satisfied) return;

            throw new GameConfigException($"Game config field '{fieldName}' is out of range (got {actualValue})");
        }
    }
}
