using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SignatureFix
{
    public enum CompanyDepartureReason
    {
        None,
        Bankruptcy,
        BankruptcyMissingInputs,
        BankruptcyNoCustomers,
        BankruptcyEducatedWorkers,
        BankruptcyWorkers,
        PropertyRelocation,
        ExternalOrLoadReplacement
    }

    public struct SignatureCompanyHistory : IComponentData, ISerializable
    {
        public Entity m_CurrentCompany;
        public CompanyDepartureReason m_LastReason;
        public CompanyDepartureReason m_PendingReason;

        public SignatureCompanyHistory(Entity currentCompany)
        {
            m_CurrentCompany = currentCompany;
            m_LastReason = CompanyDepartureReason.None;
            m_PendingReason = CompanyDepartureReason.None;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_CurrentCompany);
            writer.Write((int)m_LastReason);
            writer.Write((int)m_PendingReason);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_CurrentCompany);
            reader.Read(out int lastReason);
            reader.Read(out int pendingReason);
            m_LastReason = ParseReason(lastReason);
            m_PendingReason = ParseReason(pendingReason);
        }

        private static CompanyDepartureReason ParseReason(int value)
        {
            return value >= (int)CompanyDepartureReason.None && value <= (int)CompanyDepartureReason.ExternalOrLoadReplacement
                ? (CompanyDepartureReason)value
                : CompanyDepartureReason.None;
        }
    }
}
