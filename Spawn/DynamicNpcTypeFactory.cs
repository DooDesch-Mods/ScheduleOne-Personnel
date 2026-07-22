using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Personnel.Model;

namespace Personnel.Spawn
{
    /// <summary>
    /// Realises "pack-only NPC mods": for every definition that opted in (pack <c>autoRegister</c> or per-NPC
    /// <c>spawn.auto</c>), emits a tiny <see cref="PersonnelNpc"/> subclass at runtime whose <c>DefId</c>
    /// returns the definition's id. S1API's assembly scan then discovers and spawns these types exactly like
    /// hand-written consumer classes - no consumer C# needed.
    ///
    /// Constraints that shape this class:
    /// - S1API derives the prefab name from the SIMPLE type name ("S1API_" + Type.Name) and reconstructs
    ///   client wrappers by matching simple names across all assemblies - names must be deterministic,
    ///   session-stable and globally unique. Def ids are already normalized to [a-z0-9_], so
    ///   "Personnel_&lt;defId&gt;" is a valid, stable C# identifier; a collision check guards the rest.
    /// - The assembly name must not match S1API's discovery skip-list (System/Unity/Il2Cpp/Mono./__Generated/...).
    /// - Emission must happen before the main scene loads (S1API scans at scene init); Personnel emits
    ///   during OnInitializeMelon.
    /// - Types cannot be un-emitted: pack changes need a restart, so the assembly is a plain non-collectible
    ///   Run assembly (S1API holds strong references to the types anyway).
    /// </summary>
    internal static class DynamicNpcTypeFactory
    {
        private const string AssemblyName = "Personnel.Generated";

        private static ModuleBuilder _module;
        private static readonly HashSet<string> _emittedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Emits one subclass per opted-in definition. Idempotent per id. Returns the number of types emitted
        /// in this call. Never throws: a broken definition is skipped with a warning, a broken module disables
        /// the feature for the session.
        /// </summary>
        public static int EmitAutoRegisteredTypes(IReadOnlyList<NpcDef> allDefs)
        {
            if (allDefs == null) return 0;

            var wanted = new List<NpcDef>();
            foreach (NpcDef def in allDefs)
                if (def?.Spawn != null && def.Spawn.Auto && !_emittedIds.Contains(def.Id))
                    wanted.Add(def);
            if (wanted.Count == 0) return 0;

            // Deterministic emission order across machines/sessions: FishNet spawnable registration is
            // order-sensitive and co-op peers must agree.
            wanted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            CollectExistingConsumers(out HashSet<string> takenDefIds, out HashSet<string> takenTypeNames);

            int emitted = 0;
            foreach (NpcDef def in wanted)
            {
                if (takenDefIds.Contains(def.Id))
                {
                    Core.Log?.Warning($"auto-register: a compiled mod already provides an NPC class for " +
                                      $"'{def.Id}' - skipping the generated one (compiled wins). If you migrated this " +
                                      "pack to auto-registration, remove the old consumer DLL.");
                    continue;
                }

                string typeName = "Personnel_" + def.Id;
                if (takenTypeNames.Contains(typeName))
                {
                    Core.Log?.Error($"auto-register: type name '{typeName}' collides with an existing " +
                                    $"NPC class - '{def.Id}' is NOT registered. Rename the NPC or its pack.");
                    continue;
                }

                try
                {
                    Type t = EmitOne(typeName, def.Id);
                    if (SelfTest(t, def.Id))
                    {
                        _emittedIds.Add(def.Id);
                        takenTypeNames.Add(typeName);
                        emitted++;
                    }
                }
                catch (Exception ex)
                {
                    Core.Log?.Warning($"auto-register: emitting '{def.Id}' failed ({ex.Message}) - skipped.");
                }
            }

            if (emitted > 0)
                Core.Log?.Msg($"auto-registered {emitted} pack NPC(s) as world NPCs (assembly '{AssemblyName}').");
            return emitted;
        }

        private static Type EmitOne(string typeName, string defId)
        {
            if (_module == null)
            {
                var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), AssemblyBuilderAccess.Run);
                _module = asm.DefineDynamicModule(AssemblyName);
            }

            TypeBuilder tb = _module.DefineType(
                AssemblyName + "." + typeName,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(PersonnelNpc));

            MethodBuilder getter = tb.DefineMethod(
                "get_DefId",
                MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                typeof(string), Type.EmptyTypes);
            ILGenerator il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldstr, defId);
            il.Emit(OpCodes.Ret);

            PropertyBuilder prop = tb.DefineProperty("DefId", PropertyAttributes.None, typeof(string), null);
            prop.SetGetMethod(getter);
            tb.DefineMethodOverride(getter, BaseDefIdGetter());

            tb.DefineDefaultConstructor(MethodAttributes.Public);
            return tb.CreateTypeInfo();
        }

        /// <summary>Proves the emitted type behaves under S1API's exact usage (uninitialized instance, reflection).</summary>
        private static bool SelfTest(Type t, string defId)
        {
            if (t == null || t.IsAbstract || !typeof(PersonnelNpc).IsAssignableFrom(t))
            {
                Core.Log?.Error($"auto-register: emitted type for '{defId}' is malformed - skipped.");
                return false;
            }
            object uninit = FormatterServices.GetUninitializedObject(t);
            string got = BaseDefIdGetter().Invoke(uninit, null) as string;
            if (!string.Equals(got, defId, StringComparison.Ordinal))
            {
                Core.Log?.Error($"auto-register: emitted type for '{defId}' returned DefId '{got}' - skipped.");
                return false;
            }
            return true;
        }

        private static MethodInfo BaseDefIdGetter()
            => typeof(PersonnelNpc).GetProperty("DefId", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true);

        /// <summary>
        /// Scans already-loaded assemblies for compiled PersonnelNpc subclasses (their def ids are taken -
        /// emitting a duplicate would create two NPCs with the same save id) and for every S1API-NPC-derived
        /// simple type name (S1API matches client wrappers by simple name only).
        /// </summary>
        private static void CollectExistingConsumers(out HashSet<string> takenDefIds, out HashSet<string> takenTypeNames)
        {
            takenDefIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            takenTypeNames = new HashSet<string>(StringComparer.Ordinal);
            MethodInfo defIdGetter = BaseDefIdGetter();

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic && asm.GetName().Name == AssemblyName) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }

                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract) continue;
                    try
                    {
                        if (!typeof(S1API.Entities.NPC).IsAssignableFrom(t)) continue;
                        takenTypeNames.Add(t.Name);
                        if (!typeof(PersonnelNpc).IsAssignableFrom(t)) continue;

                        object uninit = FormatterServices.GetUninitializedObject(t);
                        if (defIdGetter.Invoke(uninit, null) is string id && !string.IsNullOrWhiteSpace(id))
                            takenDefIds.Add(id);
                    }
                    catch
                    {
                        // A consumer's DefId getter may legitimately not work this early - it just cannot
                        // block emission then.
                    }
                }
            }
        }
    }
}
