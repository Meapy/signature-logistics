using Game;
using Game.Buildings;
using Game.Companies;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SignatureFix
{
    public partial class SignatureFixSystem : GameSystemBase
    {
        private EntityQuery m_SignatureBuildings;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignatureBuildings = GetEntityQuery(
                ComponentType.ReadOnly<Signature>(),
                ComponentType.ReadOnly<Renter>());
            RequireForUpdate(m_SignatureBuildings);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? Setting.DefaultMaxVehicles;
            int patchedCompanies = 0;

            // ponytail: signature buildings are few; replace this scan with renter-change tracking only if profiling says it matters.
            using NativeArray<Entity> signatureBuildings = m_SignatureBuildings.ToEntityArray(Allocator.Temp);
            foreach (Entity building in signatureBuildings)
            {
                DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(building, true);
                foreach (Renter renter in renters)
                {
                    Entity company = renter.m_Renter;
                    if (!EntityManager.HasComponent<CompanyData>(company) ||
                        !EntityManager.HasComponent<PrefabRef>(company))
                        continue;

                    Entity companyPrefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
                    if (!EntityManager.HasComponent<TransportCompanyData>(companyPrefab))
                        continue;

                    TransportCompanyData transportCompany = EntityManager.GetComponentData<TransportCompanyData>(companyPrefab);
                    if (transportCompany.m_MaxTransports == maxVehicles)
                        continue;

                    transportCompany.m_MaxTransports = maxVehicles;
                    EntityManager.SetComponentData(companyPrefab, transportCompany);
                    patchedCompanies++;
                }
            }

            if (patchedCompanies > 0)
                Mod.log.Info($"Set maximum vehicles to {maxVehicles} for {patchedCompanies} signature building company prefabs.");
        }
    }
}
