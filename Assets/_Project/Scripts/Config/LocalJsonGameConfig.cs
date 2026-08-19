using System;
using Newtonsoft.Json;
using Company.ChestGame.Common;
using Company.ChestGame.Config.Internal;

namespace Company.ChestGame.Config
{
    // This class simulates what a remote config loader would look like.
    // Right now is simplified to just parse a GameConfig.json document. In the case of a proper
    // implementation, callbacks might be needed, depending on the game structure.
    //
    // Where the document comes from is the IGameConfigSource's problem, so pointing the game at a
    // remote config service means registering a different source and changing nothing here.
    public class LocalJsonGameConfig : IGameConfig
    {
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

            GemsReward = parsedObject.GemsReward;
            CoinsReward = parsedObject.CoinsReward;
        }

        // A negative reward would be handed to AddCurrency, which rejects it and logs an error on
        // every single win, so it is rejected at the boundary instead.
        private static void Validate(GameConfigData data)
        {
            ConfigValidation.Require(data.GemsReward >= 0, nameof(data.GemsReward), data.GemsReward);
            ConfigValidation.Require(data.CoinsReward >= 0, nameof(data.CoinsReward), data.CoinsReward);
        }
    }
}
