using Game;
using Game.Companies;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SignatureFix
{
    public partial class SignatureFixSystem : GameSystemBase
    {
        private EntityQuery m_SignatureCompanies;
        private int m_AppliedMaxVehicles = -1;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignatureCompanies = GetEntityQuery(
                ComponentType.ReadOnly<SignatureBuildingData>(),
                ComponentType.ReadWrite<TransportCompanyData>());
            RequireForUpdate(m_SignatureCompanies);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? Setting.DefaultMaxVehicles;
            if (maxVehicles == m_AppliedMaxVehicles)
                return;

            using NativeArray<Entity> signatureCompanies = m_SignatureCompanies.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in signatureCompanies)
            {
                TransportCompanyData company = EntityManager.GetComponentData<TransportCompanyData>(entity);
                company.m_MaxTransports = maxVehicles;
                EntityManager.SetComponentData(entity, company);
            }

            m_AppliedMaxVehicles = maxVehicles;
            Mod.log.Info($"Set maximum vehicles to {maxVehicles} for {signatureCompanies.Length} signature building prefabs.");
        }
    }
}
