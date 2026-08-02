using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;

namespace SignatureFix
{
    public class Mod : IMod
    {
        public static ILog log = CreateLogger();

        /// <summary>
        /// Colossal.Logging closes the log file after every write when <c>keepStreamOpen</c> is false, then reopens it
        /// on the next one. <c>UnityLogger.Open</c> swallows a failed reopen with a bare <c>catch</c> that leaves
        /// <c>m_StreamWriter</c> null, and <c>UnityLogger.Internal_WriteStream</c> immediately dereferences it with no
        /// null check; its surrounding try only catches <c>IOException</c>, so the resulting NullReferenceException
        /// escapes into Unity's log handler and is shown to the player as an error dialog. Any transient lock on the
        /// file - antivirus, a log tailer, cloud sync - is enough to trigger it, which is why it appeared at random.
        /// Holding the stream open removes the reopen, and so the window. See GitHub issue #8.
        /// </summary>
        private static ILog CreateLogger()
        {
            ILog logger = LogManager.GetLogger($"{nameof(SignatureFix)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
            logger.keepStreamOpen = true;
            return logger;
        }
        internal static SignatureFixSettings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new SignatureFixSettings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(SignatureFix), Settings, new SignatureFixSettings(this));

            updateSystem.UpdateBefore<SignatureFixSystem, ResourceBuyerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<VehicleDetailsUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
