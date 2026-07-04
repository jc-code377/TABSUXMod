using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TABSUXMod
{
    // When launched via Thunderstore Mod Manager, TABS fires a spurious
    // "You were invited to a multiplayer game" popup on every launch.
    //
    // Root cause (from IL of TFBGames.SocialProfileService):
    //   OnInvitationReceived stores the invite and calls SetState(1 or 2).
    //   SetState's switch: state 2 -> HandleInviteBasedOnGameMode(true) -> ModalPanel.Choice(...) => the popup.
    //   States 1 and 2 form a self-healing loop (OnUpdate re-shows the popup if another dialog
    //   bumped it), so blocking a leaf method or resetting state afterwards gets undone the next
    //   frame. Every transition funnels through the private SetState — the only reliable choke point.
    //
    // Fix: auto-decline the FIRST invitation of the session by redirecting its SetState(1|2) to
    // SetState(0) (Idle -> ClearInvitation) before the modal ever opens. The bogus invite comes
    // from the launch args the mod manager passes, so it is always the first invite the service
    // sees — but its *arrival time* is unpredictable (log evidence: it can land during loading or
    // well after the menu settles), which is why every timing-based gate failed. After that first
    // decline the patch never touches the state machine again, so real invites — in the menu or
    // mid-battle — behave normally. Worst case, a real invite in a session where the bogus one
    // somehow didn't fire costs the friend one re-invite.
    public static class SuppressLaunchInvitePatch
    {
        private static bool declinedFirstInvite;

        public static void Apply(Harmony harmony)
        {
            Assembly asm = null;
            foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "Assembly-CSharp") { asm = a; break; }
            }
            if (asm == null)
            {
                Debug.LogWarning("[TABSUXMod] Assembly-CSharp not found — launch invite suppression skipped.");
                return;
            }

            var svcType = asm.GetType("TFBGames.SocialProfileService");
            if (svcType == null)
            {
                Debug.LogWarning("[TABSUXMod] SocialProfileService not found — launch invite suppression skipped.");
                return;
            }

            var setState = svcType.GetMethod("SetState", BindingFlags.NonPublic | BindingFlags.Instance);
            if (setState == null)
            {
                Debug.LogWarning("[TABSUXMod] SocialProfileService.SetState not found — launch invite suppression skipped.");
                return;
            }

            var pre = typeof(SuppressLaunchInvitePatch).GetMethod(
                nameof(SetStatePrefix), BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(setState, prefix: new HarmonyMethod(pre));

            Debug.Log("[TABSUXMod] Launch invite suppression active (first invite of the session will be auto-declined).");
        }

        // Redirect the first transition into the invite-popup states (1, 2) to Idle (0),
        // which clears the stored invitation before the modal can open.
        // __0 is the incoming State argument (boxed enum).
        private static bool SetStatePrefix(ref object __0)
        {
            if (declinedFirstInvite) return true; // never interfere again

            int target = System.Convert.ToInt32(__0);
            if (target == 1 || target == 2)
            {
                declinedFirstInvite = true;
                Debug.Log("[TABSUXMod] Auto-declined spurious launch invite (redirected SetState(" + target + ") to Idle). Later invites are untouched.");
                __0 = System.Enum.ToObject(__0.GetType(), 0); // State.Idle -> ClearInvitation()
            }
            return true; // run SetState with the (possibly rewritten) target
        }
    }
}
