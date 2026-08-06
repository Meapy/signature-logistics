using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SignatureFix
{
    /// <summary>
    /// Per-building overrides for the signature logistics limits and multipliers.
    ///
    /// <para>
    /// This replaces the earlier <c>SignatureBuildingLimits</c>, which shipped without a version field and so could
    /// not gain the two multiplier fields without every existing save throwing
    /// <c>ComponentSerializerException: Data size mismatch</c>. Renaming the type is the escape hatch: the old name no
    /// longer resolves, so stale data is skipped rather than misread. The cost is that per-building overrides saved
    /// before this version are lost and those buildings fall back to the global defaults.
    /// </para>
    /// <para>
    /// The version field is written first from the outset. New fields go on the end and are read conditionally.
    /// </para>
    /// </summary>
    public struct SignatureBuildingSettings : IComponentData, ISerializable
    {
        /// <summary>Bump when fields are appended, and read them only when the stored version is at least this.</summary>
        public const int CurrentVersion = 1;

        public int m_MaxVehicles;
        public int m_MaxStorage;
        public int m_WorkerMultiplier;
        public int m_ProductionMultiplier;

        public SignatureBuildingSettings(int maxVehicles, int maxStorage, int workerMultiplier, int productionMultiplier)
        {
            m_MaxVehicles = maxVehicles;
            m_MaxStorage = maxStorage;
            m_WorkerMultiplier = workerMultiplier;
            m_ProductionMultiplier = productionMultiplier;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(CurrentVersion);
            writer.Write(m_MaxVehicles);
            writer.Write(m_MaxStorage);
            writer.Write(m_WorkerMultiplier);
            writer.Write(m_ProductionMultiplier);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out m_MaxVehicles);
            reader.Read(out m_MaxStorage);

            if (version >= 1)
            {
                reader.Read(out m_WorkerMultiplier);
                reader.Read(out m_ProductionMultiplier);
            }
            else
            {
                m_WorkerMultiplier = SignatureFixSettings.DefaultWorkerMultiplier;
                m_ProductionMultiplier = SignatureFixSettings.DefaultProductionMultiplier;
            }
        }
    }
}
