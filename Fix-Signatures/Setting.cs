using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using System.Collections.Generic;

namespace SignatureFix
{
    [FileLocation(nameof(SignatureFix))]
    [SettingsUIGroupOrder(kVehicleGroup)]
    [SettingsUIShowGroupName(kVehicleGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kVehicleGroup = "Vehicles";
        public const int DefaultMaxVehicles = 5;

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISlider(min = 1, max = 100, step = 1)]
        [SettingsUISection(kSection, kVehicleGroup)]
        public int MaxVehicles { get; set; } = DefaultMaxVehicles;

        public override void SetDefaults()
        {
            MaxVehicles = DefaultMaxVehicles;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Fix Signatures" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVehicleGroup), "Signature building vehicles" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.MaxVehicles)), "Maximum vehicles" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.MaxVehicles)), "Maximum delivery vehicles owned by each signature building. Changes apply to existing and newly placed signature buildings." },
            };
        }

        public void Unload()
        {
        }
    }
}
