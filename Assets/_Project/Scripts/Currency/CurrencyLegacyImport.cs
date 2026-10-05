using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Reads exactly what the Resource Bank library this project used to vendor wrote, through its
    /// DefaultResourceBankSaveHandle&lt;CurrencyType&gt;: a bare <c>{"ResourceAmount":{...}}</c> under
    /// "ResourceBankSaveData_CurrencyType" in PlayerPrefs, with no envelope and no version field at
    /// all. The library is out of the project, but the saves it wrote on players' devices are not.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
    /// </remarks>
    public class CurrencyLegacyImport : ILegacyImport
    {
        /// <summary>
        /// Exactly what that library's DefaultResourceBankSaveHandle&lt;T&gt;.SAVE_KEY evaluated to
        /// for <see cref="CurrencyType"/>. Never change it: it is the only bridge back to an
        /// already-installed player's existing save.
        /// </summary>
        public const string DefaultLegacyKey = "ResourceBankSaveData_CurrencyType";

        /// <summary>
        /// Where the bytes land instead of being deleted, see <see cref="Clear"/>. Suffixed onto
        /// whichever key this instance was built with.
        /// </summary>
        private const string MigratedSuffix = ".migrated";

        private readonly string _legacyKey;

        /// <summary>
        /// Creates an import over the real legacy key, or over <paramref name="legacyKey"/>.
        /// </summary>
        /// <param name="legacyKey">
        /// Redirects away from the real PlayerPrefs key, for a test. Null or empty selects
        /// <see cref="DefaultLegacyKey"/>.
        /// </param>
        public CurrencyLegacyImport(string legacyKey = null)
        {
            _legacyKey = string.IsNullOrEmpty(legacyKey) ? DefaultLegacyKey : legacyKey;
        }

        /// <summary>
        /// Where the data belongs once imported, as opposed to the legacy key, where it lives now.
        /// </summary>
        public string TargetKey => CurrencySaveHandler.SaveKey;

        /// <summary>
        /// True when there is a value here that behaves like data, not merely when the key exists.
        /// An empty or whitespace-only string and the literal "null" both count as absent, so
        /// LoadAsync takes its first-run path: a fresh T, never PayloadUnreadable.
        /// </summary>
        /// <remarks>
        /// Anything else, a stray brace included, still reaches <see cref="Import"/> and still
        /// throws.
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        public bool IsPresent()
        {
            if (!PlayerPrefs.HasKey(_legacyKey)) return false;

            string raw = PlayerPrefs.GetString(_legacyKey);
            return !string.IsNullOrWhiteSpace(raw) && raw.Trim() != "null";
        }

        /// <summary>
        /// Parses the legacy value. The legacy shape is already exactly
        /// <see cref="CurrencySaveDocument"/>'s, so parsing it is the whole of the reshape.
        /// </summary>
        public JObject Import()
        {
            return JObject.Parse(PlayerPrefs.GetString(_legacyKey));
        }

        /// <summary>
        /// Renames the legacy value instead of deleting it: the original bytes stay recoverable
        /// under the legacy key plus ".migrated". <see cref="IsPresent"/> answers false afterwards,
        /// so a re-import cannot loop.
        /// </summary>
        /// <remarks>
        /// The marker is written before the original key is deleted, and the rename is written
        /// through with <c>PlayerPrefs.Save()</c>.
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        public void Clear()
        {
            string raw = PlayerPrefs.GetString(_legacyKey);

            PlayerPrefs.SetString(_legacyKey + MigratedSuffix, raw);
            PlayerPrefs.DeleteKey(_legacyKey);

            PlayerPrefs.Save();
        }
    }
}
