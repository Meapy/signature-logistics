using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SignatureFix
{
    public struct SignatureBuildingLimits : IComponentData, ISerializable
    {
        public int m_MaxVehicles;
        public int m_MaxStorage;

        public SignatureBuildingLimits(int maxVehicles, int maxStorage)
        {
            m_MaxVehicles = maxVehicles;
            m_MaxStorage = maxStorage;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(m_MaxVehicles);
            writer.Write(m_MaxStorage);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_MaxVehicles);
            reader.Read(out m_MaxStorage);
        }
    }
}
