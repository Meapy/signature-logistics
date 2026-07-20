using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace SignatureFix
{
    [FileLocation(nameof(SignatureFix))]
    [SettingsUIGroupOrder(kLimitsGroup)]
    [SettingsUIShowGroupName(kLimitsGroup)]
    public class SignatureFixSettings : ModSetting
    {
        public const string kSection = "Main";
        public const string kLimitsGroup = "Limits";
        public const int DefaultMaxVehicles = 20;
        public const int DefaultMaxStorage = 500;
        public const int DefaultRestockTarget = 25;
        internal const int MinMaxVehicles = 1;
        internal const int MaxMaxVehicles = 100;
        internal const int MinMaxStorage = 10;
        internal const int MaxMaxStorage = 5000;
        internal const int StorageUnitsPerTonne = 1000;

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

        public override void SetDefaults()
        {
            MaxVehicles = DefaultMaxVehicles;
            MaxStorage = DefaultMaxStorage;
            RestockTarget = DefaultRestockTarget;
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
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxVehicles)), "Maximum delivery vehicles owned by each signature building. Changes apply to existing and newly placed signature buildings." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum storage (tonnes)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum total storage for each signature building, in tonnes. Changes apply during gameplay." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Input restock target (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Keep required inputs at this percentage of their recipe-weighted storage share. Lower production coverage is restocked first, using full or at least 75%-full normal deliveries when possible." },
            };
        }

        public void Unload()
        {
        }
    }
}
