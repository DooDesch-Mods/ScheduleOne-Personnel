#if DEBUG
using System;
using System.Text;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;
using S1Customer = Il2CppScheduleOne.Economy.Customer;
using S1NPC = Il2CppScheduleOne.NPCs.NPC;

namespace Personnel.Tools
{
    /// <summary>
    /// Debug-only console probe for Personnel customer NPCs: <c>personnel cust &lt;id-filter&gt; [bring|dump|checks|offer|done|deal|hand|talk|net|sample|sampleopen|choose|screen|guarantee|stock|lock|lockregion|unlockregion|close]</c>.
    /// <c>bring</c> warps the NPC next to the local player, <c>dump</c> (default) logs the customer and behaviour
    /// state the vanilla sale flow depends on, so a stuck sale can be read from the log instead of a screen.
    /// </summary>
    internal static class CustomerProbe
    {
        internal static void Run(string filter, string action)
        {
            if (string.IsNullOrWhiteSpace(filter)) { Log("usage: personnel cust <id-filter> [bring|dump|checks|offer|done|deal|hand|talk|net|sample|sampleopen|choose|screen|guarantee|stock|lock|lockregion|unlockregion|close]"); return; }
            S1NPC npc = Find(filter, out string id);
            if (npc == null) { Log($"cust: no live NPC matches '{filter}'."); return; }

            switch ((action ?? "dump").ToLowerInvariant())
            {
                case "bring": Bring(npc, id); break;
                case "checks": Checks(npc, id); break;
                case "offer": Offer(npc, id); break;
                case "done": Done(npc, id); break;
                case "deal": Deal(npc, id); break;
                case "hand": Hand(npc, id); break;
                case "talk": Talk(npc, id); break;
                case "net": Net(npc, id); break;
                case "watch": Watch(npc, id); break;
                case "rearm": Rearm(npc, id); break;
                case "rpcs": Rpcs(npc, id); break;
                case "ident": Ident(npc, id); break;
                case "sample": Sample(npc, id, true); break;
                case "sampleopen": Sample(npc, id, false); break;
                case "choose": Choose(npc, id); break;
                case "screen": Screen(npc, id, "screen"); break;
                case "lock":
                    // Puts a customer back to the locked start state, for a locked vanilla control in a played save.
                    try { npc.RelationData.Unlocked = false; } catch (Exception e) { Log($"cust lock {id}: threw {e.GetType().Name}: {e.Message}"); }
                    Log($"cust lock {id}: unlocked={Safe(() => npc.RelationData.Unlocked)}");
                    break;
                case "lockregion":
                case "unlockregion":
                    // Locks or unlocks the NPC's region, the early-game state where only Northtown is open.
                    try
                    {
                        var data = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.Map.Map>.Instance.GetRegionData(npc.Region);
                        data.IsUnlocked = action.ToLowerInvariant() == "unlockregion";
                        Log($"cust {action.ToLowerInvariant()} {id}: region {npc.Region} unlocked={data.IsUnlocked}");
                    }
                    catch (Exception e) { Log($"cust {action} {id}: threw {e.GetType().Name}: {e.Message}"); }
                    break;
                case "guarantee":
                    // Makes the next free sample succeed (CustomerData.GuaranteeFirstSampleSuccess), so a test does not
                    // depend on the acceptance roll. Run it on the host, which rolls.
                    try { npc.GetComponent<S1Customer>().CustomerData.GuaranteeFirstSampleSuccess = true; }
                    catch (Exception e) { Log($"cust guarantee {id}: threw {e.GetType().Name}: {e.Message}"); }
                    Log($"cust guarantee {id}: guaranteeFirstSample={Safe(() => npc.GetComponent<S1Customer>().CustomerData.GuaranteeFirstSampleSuccess)}");
                    break;
                case "stock":
                    // Puts 5 OG Kush into the local inventory. The vanilla `give` runs only on the host, so a co-op
                    // client needs this to have something to hand over.
                    try
                    {
                        var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
                        var def = Il2CppScheduleOne.Registry.GetItem("ogkush");
                        if (inv == null || def == null) { Log($"cust stock {id}: inventory={inv != null} ogkush={def != null}, nothing added."); break; }
                        inv.AddItemToInventory(def.GetDefaultInstance(5));
                        Log($"cust stock {id}: added ogkush x5");
                    }
                    catch (Exception e) { Log($"cust stock {id}: threw {e.GetType().Name}: {e.Message}"); }
                    break;
                case "close":
                    var open = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
                    if (open != null && open.IsOpen) open.Close();
                    Log($"cust close {id}: handoverOpen={Safe(() => open.IsOpen)}");
                    break;
                default: Dump(npc, id); break;
            }
        }

        // Logs every enable and disable of the attend-deal behaviour on this machine, so a client shows whether the
        // host's Enable/Disable RPC arrived at all.
        private static void Watch(S1NPC npc, string id)
        {
            var attend = npc.GetComponent<S1Customer>()?._attendDealBehaviour;
            if (attend == null) { Log($"cust watch {id}: attend behaviour MISSING"); return; }
            attend.onEnable.AddListener((Action)(() => Log($"cust watch {id}: attend ENABLED at frame {Time.frameCount}")));
            attend.onDisable.AddListener((Action)(() => Log($"cust watch {id}: attend DISABLED at frame {Time.frameCount}\n{Environment.StackTrace}")));
            npc.Behaviour.DEBUG_MODE = true;
            Log($"cust watch {id}: listening, enabled={Safe(() => attend.Enabled)}");
        }

        // Sizes of FishNet's RPC tables on the NPCBehaviour and Customer components, plus the observers of the NPC on
        // the host. An empty observers-RPC table on a client drops every behaviour RPC without a log line.
        private static void Rpcs(S1NPC npc, string id)
        {
            var no = npc.NetworkObject;
            Log($"cust rpcs {id}: server={Il2CppFishNet.InstanceFinder.IsServer} observers={Safe(() => { var o = no.GetType().GetProperty("Observers")?.GetValue(no); return o?.GetType().GetProperty("Count")?.GetValue(o); })}");
            DumpRpcTables(id, "NPCBehaviour", npc.Behaviour);
            DumpRpcTables(id, "Customer", npc.GetComponent<S1Customer>());
            DumpRpcTables(id, "AttendDeal", npc.GetComponent<S1Customer>()?._attendDealBehaviour);
        }

        private static void DumpRpcTables(string id, string label, Il2CppFishNet.Object.NetworkBehaviour nb)
        {
            if (nb == null) { Log($"  {label}: MISSING"); return; }
            var sb = new StringBuilder($"  {label}:");
            foreach (var p in typeof(Il2CppFishNet.Object.NetworkBehaviour).GetProperties(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if ((p.Name.IndexOf("rpc", StringComparison.OrdinalIgnoreCase) < 0 && p.Name.IndexOf("Excuted", StringComparison.Ordinal) < 0) || p.GetIndexParameters().Length > 0) continue;
                string value;
                try
                {
                    object v = p.GetValue(nb);
                    var countProp = v?.GetType().GetProperty("Count");
                    value = v == null ? "null" : countProp != null ? "count=" + countProp.GetValue(v) : v.ToString();
                }
                catch (Exception ex) { value = "ERR " + (ex.InnerException ?? ex).GetType().Name; }
                sb.Append($" {p.Name}={value}");
            }
            Log(sb.ToString());
        }

        // Whether the behaviour stack entry an RPC enables is the same object the Customer reads, and how many
        // NPCBehaviour and attend-deal components the NPC carries.
        private static void Ident(S1NPC npc, string id)
        {
            var attend = npc.GetComponent<S1Customer>()?._attendDealBehaviour;
            var beh = npc.Behaviour;
            int idx = attend != null ? attend.BehaviourIndex : -1;
            var stack = beh?.behaviourStack;
            var entry = stack != null && idx >= 0 && idx < stack.Count ? stack[idx] : null;
            Log($"cust ident {id}: server={Il2CppFishNet.InstanceFinder.IsServer} idx={idx} stackCount={stack?.Count} " +
                $"entry={Safe(() => entry.Name)} entry.ptr={entry?.Pointer} attend.ptr={attend?.Pointer} same={(entry != null && attend != null && entry.Pointer == attend.Pointer)} " +
                $"entry.enabled={Safe(() => entry.Enabled)} attend.enabled={Safe(() => attend.Enabled)} entry.npcBeh={Safe(() => entry.beh?.Pointer == beh.Pointer)}");
            var owner = attend?.beh;
            Log($"  attend.beh ptr={owner?.Pointer} npc={Safe(() => owner.Npc?.ID)} go={Safe(() => owner.gameObject.name)} root={Safe(() => owner.transform.root.name)} active={Safe(() => owner.gameObject.activeInHierarchy)} scene={Safe(() => owner.gameObject.scene.name)} attend.customer={(attend?._customer != null ? "set" : "null")}");
            int foreign = 0;
            if (stack != null)
                for (int i = 0; i < stack.Count; i++)
                    if (stack[i] != null && stack[i].beh?.Pointer != beh.Pointer) { foreign++; Log($"  stack[{i}] {Safe(() => stack[i].Name)} beh foreign ptr={stack[i].beh?.Pointer}"); }
            Log($"  stack entries with a foreign owner: {foreign}");
            var behs = npc.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour>(true);
            foreach (var b in behs)
                Log($"  NPCBehaviour id={b.GetInstanceID()} ptr={b.Pointer} go={b.gameObject.name} isNpcBehaviour={b.Pointer == beh?.Pointer} compIdx={Safe(() => b.ComponentIndex)} stack={b.behaviourStack?.Count}");
            var attends = npc.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Behaviour.CustomerAttendDealBehaviour>(true);
            foreach (var a in attends)
                Log($"  attend id={a.GetInstanceID()} ptr={a.Pointer} go={a.gameObject.name} isCustomers={a.Pointer == attend?.Pointer} behIdx={a.BehaviourIndex} enabled={Safe(() => a.Enabled)} compIdx={Safe(() => a.ComponentIndex)}");
        }

        // Host only: sends the attend-deal behaviour's Disable and Enable RPCs again.
        private static void Rearm(S1NPC npc, string id)
        {
            var attend = npc.GetComponent<S1Customer>()?._attendDealBehaviour;
            if (attend == null) { Log($"cust rearm {id}: attend behaviour MISSING"); return; }
            if (!Il2CppFishNet.InstanceFinder.IsServer) { Log($"cust rearm {id}: host only."); return; }
            attend.Disable_Server();
            attend.Enable_Server();
            Log($"cust rearm {id}: sent disable+enable for index {attend.BehaviourIndex}");
        }

        // The FishNet view of the NPC: every NetworkBehaviour the NetworkObject registered, with the ComponentIndex an
        // RPC is routed by, plus the NPCBehaviour and the attend-deal behaviour. Host and client must list the same
        // types at the same indices, or an ObserversRpc lands on another component.
        private static void Net(S1NPC npc, string id)
        {
            var no = npc.NetworkObject;
            if (no == null) { Log($"cust net {id}: NetworkObject MISSING"); return; }
            var beh = npc.Behaviour;
            var attend = npc.GetComponent<S1Customer>()?._attendDealBehaviour;
            Log($"cust net {id}: server={Il2CppFishNet.InstanceFinder.IsServer} objectId={Safe(() => no.ObjectId)} prefabId={Safe(() => no.PrefabId)} " +
                $"spawned={Safe(() => no.IsSpawned)} npcBehaviour.idx={Safe(() => beh.ComponentIndex)} npcBehaviour.client={Safe(() => beh.IsClientInitialized)} " +
                $"attend.idx={Safe(() => attend.ComponentIndex)} attend.client={Safe(() => attend.IsClientInitialized)} attend.server={Safe(() => attend.IsServerInitialized)} " +
                $"attend.no={Safe(() => attend.NetworkObject == no)} attend.go={Safe(() => attend.gameObject.name)}");
            var list = no.NetworkBehaviours;
            int count = list?.Length ?? -1;
            Log($"  registered={count} inChildren={Safe(() => npc.GetComponentsInChildren<Il2CppFishNet.Object.NetworkBehaviour>(true).Length)}");
            var sb = new StringBuilder("  nb:");
            for (int i = 0; i < count; i++)
            {
                var b = list[i];
                if (b == null) { sb.Append($" [{i}]null"); continue; }
                sb.Append($" [{i}]{Safe(() => b.ComponentIndex)}:{b.GetIl2CppType().Name}{(Safe(() => b.IsClientInitialized) == "True" ? "" : "!c")}");
            }
            Log(sb.ToString());
        }

        // Host only: the scheduled-deal path without the phone. ForceDealOffer generates and offers a contract the way
        // the daily roll does, ContractAccepted then accepts it for the deal window the game clock is in, so the
        // customer starts walking to the drop at once.
        private static void Deal(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust deal {id}: customer MISSING"); return; }
            if (!Il2CppFishNet.InstanceFinder.IsServer) { Log($"cust deal {id}: host only."); return; }
            if (c.CurrentContract != null) { Log($"cust deal {id}: already has a contract."); Hand(npc, id); return; }
            try { c.ForceDealOffer(); }
            catch (Exception e) { Log($"cust deal {id}: ForceDealOffer threw {e.GetType().Name}: {e.Message}"); return; }
            if (c.OfferedContractInfo == null) { Log($"cust deal {id}: no contract generated (no listed product or a dealer took it)."); return; }

            int now = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.CurrentTime;
            var window = now >= 600 && now < 1200 ? Il2CppScheduleOne.Economy.EDealWindow.Morning
                : now >= 1200 && now < 1800 ? Il2CppScheduleOne.Economy.EDealWindow.Afternoon
                : now >= 1800 ? Il2CppScheduleOne.Economy.EDealWindow.Night
                : Il2CppScheduleOne.Economy.EDealWindow.LateNight;
            Il2CppScheduleOne.Quests.Contract contract = null;
            try { contract = c.ContractAccepted(window, true, null); }
            catch (Exception e) { Log($"cust deal {id}: ContractAccepted threw {e.GetType().Name}: {e.Message}"); return; }
            Log($"cust deal {id}: contract={(contract != null)} window={window} at {now} " +
                $"location={Safe(() => contract.DeliveryLocation.LocationName)} stand={Safe(() => contract.DeliveryLocation.CustomerStandPoint.position)}");
        }

        // The two gates of the "[Complete Deal]" choice (Customer.IsReadyForHandover, Customer.IsHandoverChoiceValid)
        // and the state behind them, readable on host and client alike.
        private static void Hand(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust hand {id}: customer MISSING"); return; }
            var attend = c._attendDealBehaviour;
            Log($"cust hand {id}: server={Il2CppFishNet.InstanceFinder.IsServer} contract={Safe(() => c.CurrentContract != null)} " +
                $"awaitingDelivery={Safe(() => c.IsAwaitingDelivery)} readyForHandover={Safe(() => c.IsReadyForHandover(true))} " +
                $"handoverValid={Safe(() => { bool ok = c.IsHandoverChoiceValid(out string why); return ok + (ok ? "" : "(" + why + ")"); })} " +
                $"attend.enabled={Safe(() => attend.Enabled)} attend.active={Safe(() => attend.Active)} attend.index={Safe(() => attend.BehaviourIndex)} " +
                $"attend.contract={Safe(() => attend._contract != null)} active={Safe(() => npc.Behaviour.activeBehaviour != null ? npc.Behaviour.activeBehaviour.Name : "none")} " +
                $"dist={Safe(() => Vector3.Distance(npc.transform.position, c.CurrentContract.DeliveryLocation.CustomerStandPoint.position))} " +
                $"contracts={Safe(() => Il2CppScheduleOne.Quests.Contract.Contracts.Count)}");
            var stack = npc.Behaviour?.behaviourStack;
            if (stack == null) { Log("  behaviourStack MISSING"); return; }
            var sb = new StringBuilder("  stack:");
            for (int i = 0; i < stack.Count; i++) sb.Append($" [{i}]{Safe(() => stack[i].Name)}");
            Log(sb.ToString());
        }

        // Opens the NPC's dialogue the way pressing E does (DialogueController.Interacted) and logs the choices it
        // would offer, so the client's view of a deal can be read from the log.
        private static void Talk(S1NPC npc, string id)
        {
            var dc = npc.DialogueHandler?.GetComponent<Il2CppScheduleOne.Dialogue.DialogueController>();
            if (dc == null) { Log($"cust talk {id}: DialogueController MISSING"); return; }
            var sb = new StringBuilder($"cust talk {id}: canStart={Safe(() => dc.CanStartDialogue())} choices:");
            try
            {
                var choices = dc.GetActiveChoices();
                for (int i = 0; i < choices.Count; i++) sb.Append($" '{Safe(() => choices[i].ChoiceText)}'");
                if (choices.Count == 0) sb.Append(" none (greeting only)");
            }
            catch (Exception e) { sb.Append($" ERR({e.GetType().Name}: {e.Message})"); }
            Log(sb.ToString());
            try { dc.Interacted(); }
            catch (Exception e) { Log($"cust talk {id}: Interacted threw {e.GetType().Name}: {e.Message}"); }
        }

        // The reads HandoverScreenCustomerInfoPanel.Open and the "You wanna buy something?" choice depend on.
        private static void Checks(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust checks {id}: customer MISSING"); return; }
            Log($"cust checks {id}: orderedDrugTypes={Safe(() => c.GetOrderedDrugTypes().Count)} " +
                $"standards={Safe(() => c.CustomerData.Standards)} " +
                $"preferredProps={Safe(() => c.CustomerData.PreferredProperties.Count)} " +
                $"defaultAffinities={Safe(() => c.CustomerData.DefaultAffinityData.ProductAffinities.Count)} " +
                $"currentAffinities={Safe(() => c.currentAffinityData.ProductAffinities.Count)} " +
                $"showOffer={Safe(() => c.ShowOfferDealOption(true))} " +
                $"offerValid={Safe(() => { bool ok = c.OfferDealValid(out string why); return ok + (ok ? "" : "(" + why + ")"); })}");
        }

        // Runs the dialogue choice "You wanna buy something?" (Customer.InstantDealOffered) and reports whether the
        // handover screen opened. The roll inside can refuse; run it again in that case.
        private static void Offer(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust offer {id}: customer MISSING"); return; }
            try { c.InstantDealOffered(); }
            catch (Exception e) { Log($"cust offer {id}: InstantDealOffered threw {e.GetType().Name}: {e.Message}"); }
            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            Log($"cust offer {id}: handoverOpen={Safe(() => hs.IsOpen)} pendingInstantDeal={Safe(() => c.pendingInstantDeal)}");
        }

        // The gates of the free-sample choice (Customer.ShowDirectApproachOption, Customer.SampleOptionValid), the
        // connections behind IsMutuallyKnown, then the choice itself (Customer.SampleOffered) and the state of the
        // handover screen it should open: active mode, main container and the customer slot UIs.
        private static void Sample(S1NPC npc, string id, bool close)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log($"cust sample {id}: customer MISSING"); return; }
            var rd = npc.RelationData;
            var conns = new StringBuilder();
            try
            {
                for (int i = 0; i < rd.Connections.Count; i++)
                {
                    var n = rd.Connections[i];
                    conns.Append($" {Safe(() => n.ID)}(unlocked={Safe(() => n.RelationData.Unlocked)})");
                }
            }
            catch (Exception e) { conns.Append($" ERR({e.GetType().Name})"); }
            Log($"cust sample {id}: unlocked={Safe(() => rd.Unlocked)} mutuallyKnown={Safe(() => rd.IsMutuallyKnown())} " +
                $"connections={Safe(() => rd.Connections.Count)}:{conns} region={Safe(() => npc.Region)} " +
                $"directApproach={Safe(() => c.CustomerData.CanBeDirectlyApproached)} showSample={Safe(() => c.ShowDirectApproachOption(true))} " +
                $"sampleValid={Safe(() => { bool ok = c.SampleOptionValid(out string why); return ok + (ok ? "" : "(" + why + ")"); })} " +
                $"requestChance={Safe(() => c.GetSampleRequestSuccessChance())} awaitingSample={Safe(() => c.awaitingSample)} " +
                $"orderedDrugTypes={Safe(() => c.GetOrderedDrugTypes().Count)} preferredProps={Safe(() => c.CustomerData.PreferredProperties.Count)}");

            // The dialogue offers the choice only when both gates pass; anything else would roll a rejection (and a
            // police call) that no player could trigger.
            bool shown = false, valid = false;
            try { shown = rd.Unlocked ? false : c.CustomerData.CanBeDirectlyApproached && !c.IsAwaitingDelivery; valid = c.SampleOptionValid(out _); }
            catch (Exception e) { Log($"cust sample {id}: gate read threw {e.GetType().Name}: {e.Message}"); }
            if (!shown || !valid) { Log($"cust sample {id}: not offered (choice shown={shown}, valid={valid})."); return; }

            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            try { c.SampleOffered(); }
            catch (Exception e) { Log($"cust sample {id}: SampleOffered threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
            Screen(npc, id, "sample");
            if (close && hs.IsOpen)
            {
                hs.Close();
                Log($"cust sample {id}: closed the handover screen, handoverOpen={Safe(() => hs.IsOpen)}");
            }
        }

        // The handover screen as the player sees it: open, active mode, main container and the customer slot UIs.
        private static void Screen(S1NPC npc, string id, string label)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            int activeSlots = 0, slotUis = 0;
            try
            {
                var uis = hs._customerSlotUIs;
                slotUis = uis.Length;
                for (int i = 0; i < uis.Length; i++) if (uis[i] != null && uis[i].gameObject.activeInHierarchy) activeSlots++;
            }
            catch (Exception e) { Log($"cust {label} {id}: slot read threw {e.GetType().Name}"); }
            var canvas = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.DialogueCanvas>.Instance;
            Log($"cust {label} {id}: handoverOpen={Safe(() => hs.IsOpen)} mode={Safe(() => hs._activeMode != null ? hs._activeMode.GetIl2CppType().Name : "none")} " +
                $"mainContainer={Safe(() => hs._mainContainer.activeInHierarchy)} customerSlots={activeSlots}/{slotUis} awaitingSample={Safe(() => c.awaitingSample)} " +
                $"dialogueOpen={Safe(() => canvas.IsOpen)} dialogueBehaviour={Safe(() => npc.Behaviour.GenericDialogueBehaviour.Enabled)}");
        }

        // Picks the first choice of the open dialogue through DialogueCanvas.ChoiceSelected, the method the choice
        // button calls, after closing the phone if it covers the dialogue. Read the result a moment later with "screen".
        private static void Choose(S1NPC npc, string id)
        {
            var phone = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.UI.Phone.Phone>.Instance;
            if (phone != null && phone.IsOpen) { phone.SetIsOpen(false); Log($"cust choose {id}: closed the phone."); }
            var canvas = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.DialogueCanvas>.Instance;
            if (canvas == null || !canvas.IsOpen) { Log($"cust choose {id}: no dialogue open (run talk first)."); return; }
            try { canvas.ChoiceSelected(0); Log($"cust choose {id}: selected choice 0."); }
            catch (Exception e) { Log($"cust choose {id}: ChoiceSelected threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}"); }
        }

        // Packages the first product in the player's inventory, puts it into the open handover screen and presses
        // Done - the same Submit path the button takes.
        private static void Done(S1NPC npc, string id)
        {
            S1Customer c = npc.GetComponent<S1Customer>();
            var hs = Il2CppScheduleOne.DevUtilities.Singleton<Il2CppScheduleOne.UI.Handover.HandoverScreen>.Instance;
            if (c == null || hs == null || !hs.IsOpen) { Log($"cust done {id}: no open handover screen (run offer first)."); return; }

            var inv = Il2CppScheduleOne.DevUtilities.PlayerSingleton<Il2CppScheduleOne.PlayerScripts.PlayerInventory>.Instance;
            var pack = Il2CppScheduleOne.Registry.GetItem("baggie").TryCast<Il2CppScheduleOne.Product.Packaging.PackagingDefinition>();
            Il2CppScheduleOne.ItemFramework.ItemSlot from = null;
            foreach (var slot in inv.GetAllInventorySlots())
                if (slot?.ItemInstance?.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>() != null) { from = slot; break; }
            if (from == null) { Log($"cust done {id}: no product in the inventory (give ogkush 5)."); return; }

            var product = from.ItemInstance.TryCast<Il2CppScheduleOne.Product.ProductItemInstance>();
            if (product.AppliedPackaging == null && pack != null) product.SetPackaging(pack);
            var item = from.ItemInstance;
            from.ClearStoredInstance();
            hs._customerSlots[0].SetStoredItem(item);

            float cashBefore = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.cashBalance;
            int dealsBefore = c.CompletedDeliveries;
            try { hs.DoneButtonPressed(); }
            catch (Exception e) { Log($"cust done {id}: DoneButtonPressed threw {e.GetType().Name}: {e.Message}"); }
            float cashAfter = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.cashBalance;
            Log($"cust done {id}: handed '{Safe(() => item.Name)}' x{Safe(() => item.Quantity)}; handoverOpen={Safe(() => hs.IsOpen)} " +
                $"cash {cashBefore} -> {cashAfter}, completedDeliveries {dealsBefore} -> {Safe(() => c.CompletedDeliveries)}, " +
                $"timeSinceLastDeal={Safe(() => c.TimeSinceLastDealCompleted)}");
        }

        internal static S1NPC Find(string filter, out string id)
        {
            id = null;
            var reg = Il2CppScheduleOne.NPCs.NPCManager.NPCRegistry;
            if (reg == null) return null;
            foreach (var n in reg)
            {
                if (n == null || n.ID == null) continue;
                if (n.ID.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                id = n.ID;
                return n;
            }
            return null;
        }

        private static void Bring(S1NPC npc, string id)
        {
            Player local = Player.Local;
            if (local == null) { Log("cust bring: no local player."); return; }
            Vector3 target = local.transform.position + local.transform.forward * 2f;
            npc.Movement.Warp(target);
            Log($"cust bring: warped {id} to {target}.");
        }

        private static void Dump(S1NPC npc, string id)
        {
            var sb = new StringBuilder();
            sb.Append($"cust dump {id}: ");
            sb.Append($"relUnlocked={Safe(() => npc.RelationData.Unlocked)} ");
            sb.Append($"relDelta={Safe(() => npc.RelationData.RelationDelta)} ");
            sb.Append($"unlockType={Safe(() => npc.RelationData.UnlockType)} ");

            S1Customer c = npc.GetComponent<S1Customer>();
            if (c == null) { Log(sb.Append("customer=MISSING").ToString()); return; }
            sb.Append($"customer.NPC={(Safe(() => c.NPC != null))} ");
            sb.Append($"data={(Safe(() => c.CustomerData != null))} ");
            sb.Append($"inUnlocked={Safe(() => S1Customer.UnlockedCustomers.Contains(c))} ");
            sb.Append($"inLocked={Safe(() => S1Customer.LockedCustomers.Contains(c))} ");
            sb.Append($"awaitingDelivery={Safe(() => c.IsAwaitingDelivery)} ");
            sb.Append($"contract={Safe(() => c.CurrentContract != null)} ");
            sb.Append($"recommended={Safe(() => c.HasBeenRecommended)} ");
            sb.Append($"component.enabled={Safe(() => c.enabled)} ");
            sb.Append($"attendDeal={Safe(() => c._attendDealBehaviour != null)} ");
            sb.Append($"offerDealChoice={Safe(() => c.offerDealChoice != null)} ");
            sb.Append($"sampleChoice={Safe(() => c.sampleChoice != null)} ");
            Log(sb.ToString());

            var beh = npc.Behaviour;
            if (beh == null) { Log("  behaviour manager MISSING"); return; }
            Log($"  activeBehaviour={Safe(() => beh.activeBehaviour != null ? beh.activeBehaviour.Name : "none")} stack={Safe(() => beh.behaviourStack.Count)}");
            var all = npc.GetComponentsInChildren<Il2CppScheduleOne.NPCs.Behaviour.Behaviour>(true);
            foreach (var b in all)
                Log($"  behaviour '{Safe(() => b.Name)}' ({b.GetIl2CppType().Name}) enabled={Safe(() => b.Enabled)} active={Safe(() => b.Active)} prio={Safe(() => b.Priority)} index={Safe(() => b.BehaviourIndex)} inStack={Safe(() => beh.behaviourStack.Contains(b))}");

            var dh = npc.DialogueHandler;
            Log($"  dialogueHandler={(dh != null)} ");
        }

        internal static string Safe<T>(Func<T> f)
        {
            try { return Convert.ToString(f()); }
            catch (Exception e) { return "ERR(" + e.GetType().Name + ")"; }
        }

        private static void Log(string msg) => Core.Log?.Msg(msg);
    }
}
#endif
