using MelonLoader;

namespace Personnel.Config
{
    /// <summary>
    /// MelonPreferences wrapper. Category id is prefixed with the mod name so the "Mod Manager &amp; Phone App"
    /// settings UI auto-detects it. Personnel is primarily a library, so it has a single user-facing toggle:
    /// whether to drop a bundled example pack on disk as a template.
    /// </summary>
    internal static class Preferences
    {
        private const string CategoryId = "Personnel_01_Main";

        private static MelonPreferences_Category _category;
        private static MelonPreferences_Entry<bool> _loadExamplePack;

        internal static void Initialize()
        {
            if (_category != null) return;

            _category = MelonPreferences.CreateCategory(CategoryId, "Personnel (Custom NPCs)");

            _loadExamplePack = _category.CreateEntry("LoadExamplePack", false, "Load example NPC pack",
                "OFF by default. When ON, Personnel drops a small example pack into " +
                "UserData/Personnel/Packs/Examples on startup (if not already there) so you get a working " +
                "manifest template to copy for your own pack. Requires a game restart.");
        }

        internal static bool LoadExamplePack => _loadExamplePack?.Value ?? false;
    }
}
