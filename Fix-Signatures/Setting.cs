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
        public const int DefaultMaxVehicles = 10;
        public const int DefaultMaxStorage = 300;
        public const int DefaultRestockTarget = 75;
        internal const int StorageUnitsPerTonne = 1000;

        public SignatureFixSettings(IMod mod) : base(mod)
        {
        }

        [SettingsUISlider(min = 1, max = 100, step = 1)]
        [SettingsUISection(kSection, kLimitsGroup)]
        public int MaxVehicles { get; set; } = DefaultMaxVehicles;

        [SettingsUISlider(min = 10, max = 5000, step = 10)]
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
                { m_Setting.GetSettingsLocaleID(), "Fix Signatures" },
                { m_Setting.GetOptionTabLocaleID(SignatureFixSettings.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(SignatureFixSettings.kLimitsGroup), "Signature building limits" },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.MaxVehicles)), "Maximum vehicles" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxVehicles)), "Maximum delivery vehicles owned by each signature building. Changes apply to existing and newly placed signature buildings." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum storage (tonnes)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.MaxStorage)), "Maximum total storage for each signature building, in tonnes. Changes apply during gameplay." },
                { m_Setting.GetOptionLabelLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Input restock target (%)" },
                { m_Setting.GetOptionDescLocaleID(nameof(SignatureFixSettings.RestockTarget)), "Keep each required input at this percentage of its storage share by requesting normal local deliveries or imports. Higher values order sooner and use more vehicles." },
            };
        }

        public void Unload()
        {
        }
    }
}
