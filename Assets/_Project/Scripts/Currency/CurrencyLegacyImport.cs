using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Currency
{
    // Reads exactly what the Resource Bank library this project used to vendor wrote, through its
    // DefaultResourceBankSaveHandle<CurrencyType>: a bare {"ResourceAmount":{...}} under
    // "ResourceBankSaveData_CurrencyType" in PlayerPrefs, with no envelope and no version field at
    // all. The library is out of the project, but the saves it wrote on players' devices are not.
    // See docs/saving.md, "The legacy import".
    public class CurrencyLegacyImport : ILegacyImport
    {
        // Exactly what that library's DefaultResourceBankSaveHandle<T>.SAVE_KEY evaluated to for
        // CurrencyType. Never change it: it is the only bridge back to an already-installed
        // player's existing save.
        public const string DefaultLegacyKey = "ResourceBankSaveData_CurrencyType";

        // Where the bytes land instead of being deleted - see Clear(). Suffixed onto whichever key
        // this instance was built with, so a redirected test key and its marker stay paired.
        private const string MigratedSuffix = ".migrated";

        private readonly string _legacyKey;

        // legacyKey lets a caller redirect away from the real PlayerPrefs key, for a test.
        public CurrencyLegacyImport(string legacyKey = null)
        {
            _legacyKey = string.IsNullOrEmpty(legacyKey) ? DefaultLegacyKey : legacyKey;
        }

        // Where the data belongs once imported, as opposed to the legacy key above, where it lives
        // now. Not the literal, so the two can never drift.
        public string TargetKey => CurrencySaveHandler.SaveKey;

        // Present means "there is a value here that behaves like data", not merely "the key
        // exists". An empty string and the literal "null" both count as absent, so LoadAsync takes
        // its first-run path - a fresh T, never PayloadUnreadable. Anything else, a stray brace
        // included, still reaches Import() and still throws: absence is not corruption, corruption
        // is.
        public bool IsPresent()
        {
            if (!PlayerPrefs.HasKey(_legacyKey)) return false;

            string raw = PlayerPrefs.GetString(_legacyKey);
            return !string.IsNullOrWhiteSpace(raw) && raw.Trim() != "null";
        }

        public JObject Import()
        {
            // The legacy shape is already exactly CurrencySaveDocument's, so parsing it is the
            // whole of the reshape Import()'s contract asks for.
            return JObject.Parse(PlayerPrefs.GetString(_legacyKey));
        }

        // Renamed rather than deleted: the original bytes stay recoverable under
        // _legacyKey + MigratedSuffix. Either way, IsPresent() answers false afterwards, so a
        // re-import cannot loop.
        public void Clear()
        {
            string raw = PlayerPrefs.GetString(_legacyKey);

            // Written before the delete: the worst a failure between these two calls can leave
            // behind is both present, never neither.
            PlayerPrefs.SetString(_legacyKey + MigratedSuffix, raw);
            PlayerPrefs.DeleteKey(_legacyKey);

            // Writes the rename through now rather than when the application quits.
            PlayerPrefs.Save();
        }
    }
}
