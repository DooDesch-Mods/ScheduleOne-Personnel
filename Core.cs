using Personnel.Config;
using Personnel.Content;
using Personnel.Registration;
using MelonLoader;

[assembly: MelonInfo(typeof(Personnel.Core), "Personnel", "1.0.0", "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Personnel")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonOptionalDependencies("ModManager&PhoneApp")]

namespace Personnel
{
    /// <summary>
    /// MelonLoader entry point for Personnel (the NPC-library provider). On init it loads user NPC packs (managed
    /// data only); custom avatar layers are realised lazily the first time a definition's appearance is built.
    /// Other mods consume the definitions via <see cref="API"/>. This mod patches nothing - it is a pure library.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            Preferences.Initialize();
            ExamplePack.ExtractIfEnabled();

            int packDefs = NpcRegistry.LoadPacks();

            Log.Msg($"Personnel {Info.Version} - {packDefs} NPC def(s) from packs ({NpcRegistry.AllDefs.Count} total).");
            Log.Msg($"Drop packs in: {PackLoader.PacksRoot}");
        }
    }
}
