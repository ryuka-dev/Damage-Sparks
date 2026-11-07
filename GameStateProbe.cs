using PerfectRandom.Sulfur.Core;

namespace SulfurDamageNumbersMod
{
    internal enum GameplayPhase
    {
        /// <summary>No GameManager yet: launch scene and main menu.</summary>
        NoGame = 0,

        /// <summary>The player is in control of a live level or the hub.</summary>
        Running = 1,

        /// <summary>A level or the hub is being loaded.</summary>
        Loading = 2,

        /// <summary>Paused, dialogue, inventory, cinematic and the other menu-like states.</summary>
        Other = 3
    }

    /// <summary>
    /// Reads the game's own state machine (GameManager.gameState), which is the owner of "is the
    /// game paused or loading". GameManager.Update sets Time.timeScale from the same state.
    /// </summary>
    internal static class GameStateProbe
    {
        public static GameplayPhase Read()
        {
            GameManager gameManager = StaticInstance<GameManager>.Instance;
            if (gameManager == null)
                return GameplayPhase.NoGame;

            switch (gameManager.gameState)
            {
                case GameState.Running:
                    return GameplayPhase.Running;
                case GameState.Loading:
                    return GameplayPhase.Loading;
                default:
                    return GameplayPhase.Other;
            }
        }

        /// <summary>
        /// True once the game has a GameManager, which the unit asset loader needs
        /// (UnitSO.FetchAndLoadUnitLoader goes through GameManager.currentLoadedUnits).
        /// </summary>
        public static bool HasGameManager
        {
            get { return StaticInstance<GameManager>.Instance != null; }
        }
    }
}
