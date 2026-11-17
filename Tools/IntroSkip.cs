#if DEBUG
using Il2CppScheduleOne.Cutscenes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.PlayerScripts;
using CharacterCreator = Il2CppScheduleOne.CharacterCreator.CharacterCreator;

namespace Personnel.Tools
{
    /// <summary>
    /// <c>personnel intro done</c>: takes a player new to the host's save through the intro without input. The first
    /// call stops the intro cutscene, which opens the character creator; a call while the creator is open confirms it
    /// with its current appearance. Co-op checks use it on a client that joins a fresh save.
    /// </summary>
    internal static class IntroSkip
    {
        internal static void Run()
        {
            var player = Player.Local;
            if (player == null) { Core.Log?.Warning("intro: no local player yet."); return; }
            if (player._hasCompletedIntro) { Core.Log?.Msg("intro: already completed."); return; }

            var creator = Singleton<CharacterCreator>.Instance;
            if (creator != null && creator.IsOpen)
            {
                creator.Done();
                Core.Log?.Msg("intro: character creator confirmed.");
                return;
            }

            var intro = Singleton<IntroManager>.Instance;
            if (intro == null) { Core.Log?.Warning("intro: no intro manager in this scene."); return; }
            if (intro.Anim != null && intro.Anim.isPlaying)
            {
                intro.Anim.Stop();
                Core.Log?.Msg("intro: cutscene stopped; the character creator opens next.");
                return;
            }
            Core.Log?.Msg("intro: cutscene not playing and creator not open yet; call again in a moment.");
        }
    }
}
#endif
