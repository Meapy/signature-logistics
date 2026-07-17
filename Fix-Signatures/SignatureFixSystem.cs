using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Companies;
using Game.Economy;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SignatureFix
{
    public partial class SignatureFixSystem : GameSystemBase
    {
        private EntityQuery m_SignatureBuildings;
        private VehicleCapacitySystem m_VehicleCapacitySystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignatureBuildings = GetEntityQuery(
                ComponentType.ReadOnly<Signature>(),
                ComponentType.ReadOnly<Renter>());
            m_VehicleCapacitySystem = World.GetOrCreateSystemManaged<VehicleCapacitySystem>();
            RequireForUpdate(m_SignatureBuildings);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? Setting.DefaultMaxVehicles;
            int maxStorage = (Mod.Settings?.MaxStorage ?? Setting.DefaultMaxStorage) * Setting.StorageUnitsPerTonne;
            int restockTarget = Mod.Settings?.RestockTarget ?? Setting.DefaultRestockTarget;
            int patchedVehicleCompanies = 0;
            int patchedStorageCompanies = 0;
            int queuedPurchases = 0;
            ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true);
            BufferLookup<LayoutElement> layouts = GetBufferLookup<LayoutElement>(true);
            DeliveryTruckSelectData truckSelectData = m_VehicleCapacitySystem.GetDeliveryTruckSelectData();

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
                    if (EntityManager.HasComponent<TransportCompanyData>(companyPrefab))
                    {
                        TransportCompanyData transportCompany = EntityManager.GetComponentData<TransportCompanyData>(companyPrefab);
                        if (transportCompany.m_MaxTransports != maxVehicles)
                        {
                            transportCompany.m_MaxTransports = maxVehicles;
                            EntityManager.SetComponentData(companyPrefab, transportCompany);
                            patchedVehicleCompanies++;
                        }
                    }

                    if (EntityManager.HasComponent<StorageLimitData>(companyPrefab))
                    {
                        StorageLimitData storageLimit = EntityManager.GetComponentData<StorageLimitData>(companyPrefab);
                        if (storageLimit.m_Limit != maxStorage)
                        {
                            storageLimit.m_Limit = maxStorage;
                            EntityManager.SetComponentData(companyPrefab, storageLimit);
                            patchedStorageCompanies++;
                        }
                    }

                    if (QueueInputPurchase(company, building, companyPrefab, maxStorage, restockTarget, truckSelectData, ref deliveryTrucks, ref layouts))
                        queuedPurchases++;
                }
            }

            if (patchedVehicleCompanies > 0 || patchedStorageCompanies > 0)
                Mod.log.Info($"Updated {patchedVehicleCompanies} vehicle limits to {maxVehicles} and {patchedStorageCompanies} storage limits to {maxStorage / Setting.StorageUnitsPerTonne} t.");

            if (queuedPurchases > 0)
                Mod.log.Debug($"Queued {queuedPurchases} priority input purchase(s) for signature companies.");
        }

        private bool QueueInputPurchase(
            Entity company,
            Entity building,
            Entity companyPrefab,
            int storageLimit,
            int targetPercent,
            DeliveryTruckSelectData truckSelectData,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks,
            ref BufferLookup<LayoutElement> layouts)
        {
            if (EntityManager.HasComponent<ResourceBuyer>(company) ||
                !EntityManager.HasComponent<IndustrialProcessData>(companyPrefab) ||
                !EntityManager.HasBuffer<Resources>(company) ||
                !EntityManager.HasBuffer<TripNeeded>(company) ||
                !EntityManager.HasBuffer<OwnedVehicle>(company) ||
                !EntityManager.HasComponent<Transform>(building))
                return false;

            IndustrialProcessData process = EntityManager.GetComponentData<IndustrialProcessData>(companyPrefab);
            int storageShares = 0;
            if (process.m_Input1.m_Resource != Resource.NoResource) storageShares++;
            if (process.m_Input2.m_Resource != Resource.NoResource) storageShares++;
            if (process.m_Output.m_Resource != Resource.NoResource) storageShares++;
            if (storageShares == 0)
                return false;

            int targetAmount = storageLimit / storageShares * targetPercent / 100;
            Resource resource = Resource.NoResource;
            int availableAmount = int.MaxValue;

            // ponytail: two inputs are the native process limit, so a tiny direct comparison is clearer than a collection.
            SelectLowerStockInput(company, process.m_Input1.m_Resource, ref resource, ref availableAmount, ref deliveryTrucks, ref layouts);
            SelectLowerStockInput(company, process.m_Input2.m_Resource, ref resource, ref availableAmount, ref deliveryTrucks, ref layouts);
            if (resource == Resource.NoResource || availableAmount >= targetAmount)
                return false;

            truckSelectData.GetCapacityRange(resource, out _, out int maxTruckCapacity);
            int amountNeeded = Unity.Mathematics.math.min(targetAmount - availableAmount, maxTruckCapacity);
            if (amountNeeded <= 0)
                return false;

            EntityManager.AddComponentData(company, new ResourceBuyer
            {
                m_Payer = company,
                m_Flags = SetupTargetFlags.Industrial | SetupTargetFlags.Import,
                m_ResourceNeeded = resource,
                m_AmountNeeded = amountNeeded,
                m_Location = EntityManager.GetComponentData<Transform>(building).m_Position
            });
            return true;
        }

        private void SelectLowerStockInput(
            Entity company,
            Resource candidate,
            ref Resource selected,
            ref int selectedAmount,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks,
            ref BufferLookup<LayoutElement> layouts)
        {
            if (candidate == Resource.NoResource)
                return;

            int amount = EconomyUtils.GetResources(candidate, EntityManager.GetBuffer<Resources>(company, true));
            foreach (TripNeeded trip in EntityManager.GetBuffer<TripNeeded>(company, true))
            {
                if ((trip.m_Purpose == Purpose.Shopping || trip.m_Purpose == Purpose.CompanyShopping) && trip.m_Resource == candidate)
                    amount += trip.m_Data;
            }

            foreach (OwnedVehicle ownedVehicle in EntityManager.GetBuffer<OwnedVehicle>(company, true))
                amount += VehicleUtils.GetBuyingTrucksLoad(ownedVehicle.m_Vehicle, candidate, ref deliveryTrucks, ref layouts);

            if (amount < selectedAmount)
            {
                selected = candidate;
                selectedAmount = amount;
            }
        }
    }
}
