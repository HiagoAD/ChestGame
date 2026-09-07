using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Currency
{
    // The concrete ILegacyImport phase 4 deferred until a save model existed to write it against -
    // see docs/saving.md, "The legacy import". Reads exactly what
    // DefaultResourceBankSaveHandle<CurrencyType> has always written: a bare
    // {"ResourceAmount":{...}} under "ResourceBankSaveData_CurrencyType" in PlayerPrefs, with no
    // envelope and no version field at all.
    public class CurrencyLegacyImport : ILegacyImport
    {
        // Exactly DefaultResourceBankSaveHandle<T>.SAVE_KEY evaluates to for T = CurrencyType - see
        // AssetLibrary/ResourceBank/Saving/ResourceBankSaveHandle.cs. Never changed: this is the
        // only bridge back to whatever an already-installed player's save is sitting under, and the
        // default this class falls back to when nothing overrides it.
        public const string DefaultLegacyKey = "ResourceBankSaveData_CurrencyType";

        // Never deleted outright - see Clear() below for why - so this is where the bytes land
        // instead, suffixed onto whichever key this instance was actually built with rather than
        // onto DefaultLegacyKey unconditionally, so a redirected test key and its migrated marker
        // stay paired the same way the real key and its own marker do.
        private const string MigratedSuffix = ".migrated";

        private readonly string _legacyKey;

        // legacyKey is an explicit constructor argument, not only a default, for the same reason
        // FileStore's root and PlayerPrefsStore's prefix are: a caller supplies its own to redirect
        // away from whatever the default resolves to, rather than this class reading a fixed key no
        // test could ever repoint. GameLifetimeScope.RegisterCoreServices is what actually threads a
        // test's override through - see docs/saving.md, "Currency: the first real caller".
        public CurrencyLegacyImport(string legacyKey = null)
        {
            _legacyKey = string.IsNullOrEmpty(legacyKey) ? DefaultLegacyKey : legacyKey;
        }

        // Present means "there is a value here that behaves like data", not merely "the key exists".
        // DefaultResourceBankSaveHandle<T>.Save(null) - never triggered by ResourceBank itself, but
        // not something PlayerPrefs stops anyone from having written by hand - writes the JSON
        // literal "null", four bytes, via JsonConvert.SerializeObject(null); an empty string is the
        // same absence PlayerPrefs itself cannot otherwise distinguish from "never written". The old
        // path already treated both as nothing to load:
        // JsonConvert.DeserializeObject<ResourceBankState<T>>("null") returns null, and
        // ResourceBank.Load's own `?? new ResourceBankState<T>()` turned that into booting at zero,
        // silently. Answering false here instead of letting Import() reach JObject.Parse keeps that
        // exact outcome - a fresh T from SaveService.LoadAsync's own first-run path, not
        // PayloadUnreadable. Anything else under this key - truly malformed JSON, a stray brace -
        // still reaches Import() and still throws loudly, which is correct: absence is not
        // corruption, but corruption is still corruption.
        public bool IsPresent()
        {
            if (!PlayerPrefs.HasKey(_legacyKey)) return false;

            string raw = PlayerPrefs.GetString(_legacyKey);
            return !string.IsNullOrWhiteSpace(raw) && raw.Trim() != "null";
        }

        public JObject Import()
        {
            // {"ResourceAmount":{...}} is already exactly CurrencySaveDocument's own shape, so
            // parsing it is the entire reshape ILegacyImport.Import()'s contract asks for - nothing
            // here renames or restructures a single field.
            return JObject.Parse(PlayerPrefs.GetString(_legacyKey));
        }

        // Renamed rather than deleted. SaveService only ever calls this once the new save under the
        // real key is already durably written, so nothing here is load-bearing for that specific
        // load - but IsPresent() answering false from here on is exactly what stops a much rarer
        // failure from compounding into a silent one: if the new save and its .bak both later
        // disappear - a manual delete, a future bug - LoadAsync falls back to this import again, and
        // a plain delete here would be indistinguishable from "nothing was ever imported" from that
        // point on, importing genuinely stale data over whatever the player's real balance had
        // become since. Moving the value instead of erasing it keeps IsPresent() false the same way
        // a delete would - re-import still cannot loop - while keeping the bytes themselves
        // recoverable under MigratedSuffix rather than gone: a rollback to a build that only knows
        // DefaultLegacyKey finds a marker to explain the zero rather than no trace at all, and a run
        // that reaches this method by mistake - the exact failure phase 6b's own gate ran into -
        // displaces a developer's data instead of destroying it.
        public void Clear()
        {
            string raw = PlayerPrefs.GetString(_legacyKey);

            // Written before the delete, the same ordering SaveService itself already enforces one
            // level up (the new save durable before this runs at all): the worst case a failure
            // between these two calls can leave behind is the marker and the original both present,
            // never neither.
            PlayerPrefs.SetString(_legacyKey + MigratedSuffix, raw);
            PlayerPrefs.DeleteKey(_legacyKey);

            // Only ever reached once the new save has already been durably written under the new
            // key - see SaveService.ImportLegacyOrFreshAsync - so this is tidiness, not a
            // correctness requirement: LoadAsync never asks IsPresent() again for this key once that
            // write succeeds, whether or not either call above survives an unclean quit.
            // PlayerPrefs.Save() forces both to anyway, for the same reason PlayerPrefsStore's own
            // writes do. SaveService already logs rather than swallows a failure that reaches it
            // from this method - see SaveService.ImportLegacyOrFreshAsync's own catch.
            PlayerPrefs.Save();
        }
    }
}
