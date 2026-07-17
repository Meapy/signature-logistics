using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace SignatureFix
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(SignatureFix)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        internal static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(SignatureFix), Settings, new Setting(this));

            updateSystem.UpdateAt<SignatureFixSystem>(SystemUpdatePhase.PrefabUpdate);
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
