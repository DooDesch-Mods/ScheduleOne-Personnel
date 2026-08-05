using Personnel.Config;
using Personnel.Content;
using Personnel.Registration;
using MelonLoader;

[assembly: MelonInfo(typeof(Personnel.Core), "Personnel", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Personnel")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Personnel
{
    /// <summary>
    /// MelonLoader entry point for Personnel (the NPC-library provider). On init it loads user NPC packs (managed
    /// data only); custom avatar layers are realised lazily the first time a definition's appearance is built.
    /// Other mods consume the definitions via <see cref="API"/>. The only patch it applies is the dev-console
    /// bridge for pack authors (see <see cref="Tools.PersonnelConsole"/>); nothing in the game loop is touched.
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

            if (Preferences.EnableAutoRegister)
                Spawn.DynamicNpcTypeFactory.EmitAutoRegisteredTypes(NpcRegistry.AllDefs);

            // Authoring commands ("personnel pos" and friends). A failed patch costs the console bridge, not
            // the library, so the roster still loads either way.
            try { HarmonyInstance.PatchAll(); }
            catch (System.Exception e) { Log.Warning("Console commands unavailable: " + e.Message); }

            LogRosterSummary(packDefs);
            Log.Msg($"Drop packs in: {PackLoader.PacksRoot}");
            Log.Msg("Writing a schedule? Type 'personnel help' in the dev console to grab coordinates.");
        }

#if DEBUG
        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            if (sceneName == "Main")
                MelonCoroutines.Start(DiagnoseAfterLoad());
        }

        // Dev-only probe: proves whether S1API's discovery sees the emitted types and whether their
        // instances made it into NPC.All (world side), ~15s after the main scene comes up.
        private static System.Collections.IEnumerator DiagnoseAfterLoad()
        {
            yield return new UnityEngine.WaitForSeconds(15f);
            try
            {
                var s1apiAsm = typeof(S1API.Entities.NPC).Assembly;
                var utils = s1apiAsm.GetType("S1API.Internal.Utils.ReflectionUtils");
                var m = utils?.GetMethod("GetDerivedClasses",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var generic = m?.MakeGenericMethod(typeof(S1API.Entities.NPC));
                if (generic?.Invoke(null, null) is System.Collections.Generic.List<System.Type> discovered)
                {
                    int emitted = 0;
                    foreach (var t in discovered)
                        if (t?.Assembly?.IsDynamic == true) emitted++;
                    Log.Msg($"[diag] S1API discovery sees {discovered.Count} NPC type(s), {emitted} from the dynamic assembly.");
                }
                else
                {
                    Log.Msg("[diag] could not invoke S1API ReflectionUtils.GetDerivedClasses.");
                }

                var all = S1API.Entities.NPC.All;
                Log.Msg($"[diag] NPC.All has {all.Count} wrapper(s):");
                foreach (var npc in all)
                {
                    string type = npc?.GetType().FullName ?? "<null>";
                    string id = "?";
                    try { id = npc?.ID ?? "?"; } catch { }
                    bool dyn = npc?.GetType().Assembly?.IsDynamic == true;
                    string world = "no-go";
                    try
                    {
                        var go = npc?.gameObject;
                        if (go != null)
                            world = $"go='{go.name}' active={go.activeInHierarchy} pos={npc.Position}";
                    }
                    catch (System.Exception e) { world = "go-err:" + e.Message; }
                    Log.Msg($"[diag]   {id} ({type}){(dyn ? " [emitted]" : "")} physical={npc?.IsPhysical} {world}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[diag] probe failed: {ex}");
            }
        }
#endif

        private static void LogRosterSummary(int packDefs)
        {
            int physical = 0, contacts = 0, customers = 0, dealers = 0, scheduled = 0, auto = 0;
            foreach (var def in NpcRegistry.AllDefs)
            {
                bool hasSchedule = def.Schedule != null && def.Schedule.Count > 0;
                if (def.Spawn?.Physical ?? hasSchedule) physical++; else contacts++;
                string role = API.ResolveRole(def);
                if (role == "customer") customers++;
                else if (role == "dealer") dealers++;
                if (hasSchedule) scheduled++;
                if (def.Spawn?.Auto == true) auto++;
            }
            Log.Msg($"Personnel {Instance.Info.Version} - {packDefs} NPC def(s) from packs ({NpcRegistry.AllDefs.Count} total): " +
                    $"{physical} physical / {contacts} contact-only, {customers} customer(s), {dealers} dealer(s), " +
                    $"{scheduled} with schedules, {auto} auto-registered.");
        }
    }
}
