using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Serialization;
using Game.Simulation;
using Game.UI;
using Unity.Entities;

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

        /// <summary>
        /// True when rcav8tr's Change Company mod is present. That mod has a Company Workplaces feature which owns
        /// <c>WorkProvider.m_MaxWorkers</c> outright, including on signature buildings, and documents that an override
        /// "prevents this normal game logic". Two mods writing the same field cannot both win, so this one stands
        /// down: the worker multiplier is forced to 1x and its sliders are hidden, leaving workplaces entirely to the
        /// mod built for it. Storage, vehicles and the production multiplier are unaffected - those live on this mod's
        /// private prefab copy, which nothing else touches.
        /// </summary>
        internal static bool ChangeCompanyDetected { get; private set; }

        /// <summary>
        /// Change Company's <c>WorkplacesOverride</c> component, resolved by name at load, or null when that mod is
        /// absent. A company carrying it is one the player has explicitly given a workplaces override, and
        /// <c>CompanyWorkplacesSystem.OverrideWorkplacesJob</c> forces <c>m_MaxWorkers</c> back to that value right
        /// after each company AI system runs. Its query requires the component, so companies without an override are
        /// never touched by that mod - which is why this mod only needs to stand aside per company, not globally.
        /// </summary>
        internal static ComponentType? WorkplacesOverrideType { get; private set; }

        private static void ResolveWorkplacesOverrideType()
        {
            WorkplacesOverrideType = null;
            try
            {
                foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type type = assembly.GetType("ChangeCompany.WorkplacesOverride", false);
                    if (type == null)
                        continue;

                    WorkplacesOverrideType = ComponentType.ReadOnly(type);
                    log.Info("Resolved ChangeCompany.WorkplacesOverride; companies with a workplaces override will be left to that mod.");
                    return;
                }
            }
            catch (System.Exception exception)
            {
                log.Warn(exception, "Could not resolve ChangeCompany.WorkplacesOverride; per-company deferral is unavailable.");
            }
        }

        /// <summary>
        /// True when workplaces should be left entirely to Change Company: it is installed and the player has not
        /// turned the option off. Everything that reads the worker multiplier goes through this, so the choice takes
        /// effect the moment it is toggled - no reload needed.
        /// </summary>
        internal static bool DeferWorkersToChangeCompany =>
            ChangeCompanyDetected && (Settings?.UseChangeCompanyForEmployees ?? SignatureFixSettings.DefaultUseChangeCompanyForEmployees);

        private static void DetectChangeCompany()
        {
            ChangeCompanyDetected = false;
            try
            {
                foreach (Game.Modding.ModManager.ModInfo modInfo in GameManager.instance.modManager)
                {
                    string name = modInfo?.name;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    if (name.IndexOf("ChangeCompany", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Change Company", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ChangeCompanyDetected = true;
                        log.Info($"Change Company detected ({name}). Worker capacity control is disabled; its Company Workplaces feature manages workplaces instead.");
                        return;
                    }
                }
            }
            catch (System.Exception exception)
            {
                // Detection is a courtesy, not a requirement. If the mod list cannot be read, carry on as if absent.
                log.Warn(exception, "Could not inspect the mod list to detect Change Company.");
            }
        }

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));
            DetectChangeCompany();
            ResolveWorkplacesOverrideType();

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            Settings = new SignatureFixSettings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(SignatureFix), Settings, new SignatureFixSettings(this));

            updateSystem.UpdateBefore<SignatureFixSystem, ResourceBuyerSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<VehicleDetailsUISystem>(SystemUpdatePhase.UIUpdate);

            // Scoped prefab copies must not exist while the city is written. They carry Unity.Entities.Prefab and are
            // therefore absent from the serialization entity table, so BinaryWriter.Write(Entity) stores -1 for any
            // PrefabRef aimed at one and the tenant reloads with Entity.Null for its prefab. Release before the write,
            // restore after it, both inside the Serialize phase so no simulation frame sees the vanilla limits.
            updateSystem.UpdateBefore<PreSerialize<SignatureFixSystem>>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignatureScopeRestoreSystem, WriteSystem>(SystemUpdatePhase.Serialize);
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
