using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace Company.ChestGame.Config
{
    // This class simulates what a remote config loader would look like.
    // Right now is simplified to just load a Data.json file, with a flag to indicate
    // if the data was loaded. In the case of a proper implementation, callbacks might
    // be needed, depending on the game structure
    public class GameConfig
    {
        private const string FILE_NAME = "Data";

        [Serializable]
        private class GameConfigData
        {
            public int ChestCount;
            public int AttempsCount;
            public int TimeToOpenChestMiliseconds;
        }

        public bool Initialized { get; }

        public int ChestCount { get; }
        public int AttempsCount { get; }
        public TimeSpan TimeToOpenChest{ get; }

        public GameConfig()
        {
            UnityEngine.Object rawObject = Resources.Load(FILE_NAME);
            if(rawObject == null)
            {
                throw new Exception($"File {FILE_NAME} not found, make sure that it exists on a Resources folder");
            }

            TextAsset castedObject = rawObject as TextAsset;
            if(castedObject == null)
            {
                throw new Exception($"File {FILE_NAME} is not a valid text asset");
            }
            
            GameConfigData parsedObject = JsonConvert.DeserializeObject<GameConfigData>(castedObject.text);

            ChestCount = parsedObject.ChestCount;
            AttempsCount = parsedObject.AttempsCount;
            TimeToOpenChest = new TimeSpan(TimeSpan.TicksPerMillisecond * parsedObject.TimeToOpenChestMiliseconds);

            Initialized = true;
        }

    }
}
