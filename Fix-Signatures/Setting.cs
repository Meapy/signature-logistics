using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace SignatureFix
{
    [FileLocation(nameof(SignatureFix))]
    [SettingsUIGroupOrder(kLimitsGroup, kOutputGroup)]
    [SettingsUIShowGroupName(kLimitsGroup, kOutputGroup)]
    public class SignatureFixSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kLimitsGroup = "Limits";
        public const string kOutputGroup = "Output";
        public const int DefaultMaxVehicles = 20;
        public const int DefaultMaxStorage = 500;
        public const int DefaultRestockTarget = 25;
        public const int DefaultWorkerMultiplier = 1;
        public const int DefaultProductionMultiplier = 1;
        internal const int MinMaxVehicles = 1;
        internal const int MaxMaxVehicles = 100;
        internal const int MinMaxStorage = 10;
        internal const int MaxMaxStorage = 5000;
        internal const int StorageUnitsPerTonne = 1000;
        internal const int MinMultiplier = 1;
        internal const int MaxMultiplier = 10;

        public SignatureFixSettings(IMod mod) : base(mod)
        {
        }

        [SettingsUISlider(min = MinMaxVehicles, max = MaxMaxVehicles, step = 1)]
        [SettingsUISection(kSection, kLimitsGroup)]
        public int MaxVehicles { get; set; } = DefaultMaxVehicles;

        [SettingsUISlider(min = MinMaxStorage, max = MaxMaxStorage, step = 10)]
        [SettingsUISection(kSection, kLimitsGroup)]
        public int MaxStorage { get; set; } = DefaultMaxStorage;

        [SettingsUISlider(min = 25, max = 100, step = 5)]
        [SettingsUISection(kSection, kLimitsGroup)]
        public int RestockTarget { get; set; } = DefaultRestockTarget;

        [SettingsUISlider(min = MinMultiplier, max = MaxMultiplier, step = 1)]
        [SettingsUISection(kSection, kOutputGroup)]
        public int WorkerMultiplier { get; set; } = DefaultWorkerMultiplier;

        [SettingsUISlider(min = MinMultiplier, max = MaxMultiplier, step = 1)]
        [SettingsUISection(kSection, kOutputGroup)]
        public int ProductionMultiplier { get; set; } = DefaultProductionMultiplier;

        public override void SetDefaults()
        {
            MaxVehicles = DefaultMaxVehicles;
            MaxStorage = DefaultMaxStorage;
            RestockTarget = DefaultRestockTarget;
            WorkerMultiplier = DefaultWorkerMultiplier;
            ProductionMultiplier = DefaultProductionMultiplier;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly SignatureFixSettings m_Setting;

        public LocaleEN(SignatureFixSettings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Signature Logistics" },
                { m_Setting.GetOptionTabLocaleID(SignatureFixSettings.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(SignatureFixSettings.kLimitsGroup), "Signature building limits" },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.MaxVehicles)), "Maximum vehicles" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxVehicles)), "Maximum delivery vehicles owned by each signature building. Changes apply to existing and newly placed signature buildings. Ordinary zoned commercial, office, and industrial companies are never affected." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum storage (tonnes)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum total storage for each signature building, in tonnes. Changes apply during gameplay. Ordinary zoned commercial, office, and industrial companies are never affected." },
                { m_Setting.GetOptionGroupLocaleID(SignatureFixSettings.kOutputGroup), "Workers and production" },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.WorkerMultiplier)), "Worker capacity multiplier" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.WorkerMultiplier)), "Multiplies how many workers a signature building can employ, from 1x to 10x. This raises the ceiling only; the game still hires up to what your city's labour pool and education levels can supply, and more workers also mean more output. Ordinary zoned companies are never affected." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.ProductionMultiplier)), "Production multiplier" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.ProductionMultiplier)), "Multiplies a signature building's throughput from 1x to 10x. Inputs scale with output, so a 10x factory needs 10x the materials delivered and enough storage to hold them. Combines with the worker multiplier: 10x workers and 10x production is roughly 100x output." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Input restock target (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Keep required inputs at this percentage of their recipe-weighted storage share. Lower production coverage is restocked first, using full or at least 75%-full priority imports when possible." },
            };
        }

        public void Unload()
        {
        }
    }
}
