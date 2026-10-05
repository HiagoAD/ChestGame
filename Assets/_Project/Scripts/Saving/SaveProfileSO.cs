using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Three dropdowns and nothing else: an authoring seam for the three save components, not a
    /// working save pipeline on its own.
    /// </summary>
    [CreateAssetMenu(menuName = "Saving/Save Profile")]
    public class SaveProfileSO : ScriptableObject
    {
        [SerializeField] private SaveStorage _storage;
        [SerializeField] private SaveCodec _codec;
        [SerializeField] private SaveProtection _protection;

        public SaveStorage Storage => _storage;
        public SaveCodec Codec => _codec;
        public SaveProtection Protection => _protection;
    }
}
