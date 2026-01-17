using Newtonsoft.Json;
using UnityEngine;
using System;

namespace TapNation.Modules.ResourceBank.Saving
{
    public interface IResourceBankSaveHandler<T> where T : struct, Enum
    {
        public void Save(ResourceBankState<T> data);
        public ResourceBankState<T> Load();
    }
    
    public class DefaultResourceBankSaveHandle<T> : IResourceBankSaveHandler<T> where T : struct, Enum
    {
        private static string SAVE_KEY => "ResourceBankSaveData_" + typeof(T).Name;
        
        public void Save(ResourceBankState<T> data)
        {
            string json = JsonConvert.SerializeObject(data);
            PlayerPrefs.SetString(SAVE_KEY, json);
        }

        public ResourceBankState<T> Load()
        {
            if (!PlayerPrefs.HasKey(SAVE_KEY)) return null;
            
            string json = PlayerPrefs.GetString(SAVE_KEY);
            return JsonConvert.DeserializeObject<ResourceBankState<T>>(json);
        }
    }
}