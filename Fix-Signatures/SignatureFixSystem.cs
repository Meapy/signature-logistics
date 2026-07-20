using Game;
using Game.Agents;
using Game.Buildings;
using Game.Citizens;
using Game.Companies;
using Game.Economy;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SignatureFix
{
    public partial class SignatureFixSystem : GameSystemBase
    {
        private EntityQuery m_SignatureBuildings;
        private EntityQuery m_EconomyParameters;
        private VehicleCapacitySystem m_VehicleCapacitySystem;
        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;

        private const uint BankruptcyGraceFrames = 65536;
        private const int MinimumTruckFillPercent = 75;

        // ponytail: still 4x faster than vanilla company buying; add per-company failure backoff only if profiling justifies its state.
        public override int GetUpdateInterval(SystemUpdatePhase phase) => 64;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SignatureBuildings = GetEntityQuery(
                ComponentType.ReadOnly<Signature>(),
                ComponentType.ReadOnly<Renter>());
            m_EconomyParameters = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_VehicleCapacitySystem = World.GetOrCreateSystemManaged<VehicleCapacitySystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            RequireForUpdate(m_SignatureBuildings);
            RequireForUpdate(m_EconomyParameters);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? SignatureFixSettings.DefaultMaxVehicles;
            int maxStorage = (Mod.Settings?.MaxStorage ?? SignatureFixSettings.DefaultMaxStorage) * SignatureFixSettings.StorageUnitsPerTonne;
            int restockTarget = Mod.Settings?.RestockTarget ?? SignatureFixSettings.DefaultRestockTarget;
            int patchedVehicleCompanies = 0;
            int patchedStorageCompanies = 0;
            int queuedPurchases = 0;
            int protectedTenants = 0;
            long startingResourcesGranted = 0;
            ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true);
            ComponentLookup<ResourceData> resourceDatas = GetComponentLookup<ResourceData>(true);
            BufferLookup<LayoutElement> layouts = GetBufferLookup<LayoutElement>(true);
            DeliveryTruckSelectData truckSelectData = m_VehicleCapacitySystem.GetDeliveryTruckSelectData();
            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();
            int bankruptcyLimit = m_EconomyParameters.GetSingleton<EconomyParameterData>().m_CompanyBankruptcyLimit;

            // ponytail: signature buildings are few; replace this scan with renter-change tracking only if profiling says it matters.
            using NativeArray<Entity> signatureBuildings = m_SignatureBuildings.ToEntityArray(Allocator.Temp);
            foreach (Entity building in signatureBuildings)
            {
                int buildingMaxVehicles = maxVehicles;
                int buildingMaxStorage = maxStorage;
                if (EntityManager.HasComponent<SignatureBuildingLimits>(building))
                {
                    SignatureBuildingLimits limits = EntityManager.GetComponentData<SignatureBuildingLimits>(building);
                    buildingMaxVehicles = Unity.Mathematics.math.clamp(limits.m_MaxVehicles, SignatureFixSettings.MinMaxVehicles, SignatureFixSettings.MaxMaxVehicles);
                    buildingMaxStorage = Unity.Mathematics.math.clamp(limits.m_MaxStorage, SignatureFixSettings.MinMaxStorage, SignatureFixSettings.MaxMaxStorage) * SignatureFixSettings.StorageUnitsPerTonne;
                }

                DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(building, true);
                foreach (Renter renter in renters)
                {
                    Entity company = renter.m_Renter;
                    if (!EntityManager.HasComponent<CompanyData>(company) ||
                        !EntityManager.HasComponent<PrefabRef>(company))
                        continue;

                    Entity companyPrefab = EntityManager.GetComponentData<PrefabRef>(company).m_Prefab;
                    IndustrialProcessData process = EntityManager.HasComponent<IndustrialProcessData>(companyPrefab)
                        ? EntityManager.GetComponentData<IndustrialProcessData>(companyPrefab)
                        : default;
                    bool newTenant = !EntityManager.HasComponent<SignatureCompanyHistory>(building) ||
                        EntityManager.GetComponentData<SignatureCompanyHistory>(building).m_CurrentCompany != company;
                    SignatureCompanyHistory history = ObserveCompany(building, company);
                    if (newTenant)
                        startingResourcesGranted += DoubleStartingResources(company);
                    int companyWorth = GetCompanyWorth(company, process, resourcePrefabs, ref resourceDatas, ref deliveryTrucks, ref layouts);

                    // The game also uses MovingAway for random tax/worker-shortage churn. Preserve the
                    // signature tenant unless its worth has stayed below the real bankruptcy limit past the grace period.
                    if (EntityManager.HasComponent<MovingAway>(company))
                    {
                        if (IsMatureBankruptcy(company, companyWorth, bankruptcyLimit))
                        {
                            history.m_PendingReason = GetBankruptcyReason(company);
                            EntityManager.SetComponentData(building, history);
                        }
                        else
                        {
                            EntityManager.RemoveComponent<MovingAway>(company);
                            if (history.m_PendingReason != CompanyDepartureReason.None)
                            {
                                history.m_PendingReason = CompanyDepartureReason.None;
                                EntityManager.SetComponentData(building, history);
                            }
                            protectedTenants++;
                        }
                    }
                    else
                    {
                        CompanyDepartureReason pendingReason = EntityManager.HasComponent<PropertyRenter>(company)
                            ? CompanyDepartureReason.None
                            : CompanyDepartureReason.PropertyRelocation;
                        if (history.m_PendingReason != pendingReason)
                        {
                            history.m_PendingReason = pendingReason;
                            EntityManager.SetComponentData(building, history);
                        }
                    }

                    if (EntityManager.HasComponent<TransportCompanyData>(companyPrefab))
                    {
                        TransportCompanyData transportCompany = EntityManager.GetComponentData<TransportCompanyData>(companyPrefab);
                        if (transportCompany.m_MaxTransports != buildingMaxVehicles)
                        {
                            transportCompany.m_MaxTransports = buildingMaxVehicles;
                            EntityManager.SetComponentData(companyPrefab, transportCompany);
                            patchedVehicleCompanies++;
                        }
                    }

                    if (EntityManager.HasComponent<StorageLimitData>(companyPrefab))
                    {
                        StorageLimitData storageLimit = EntityManager.GetComponentData<StorageLimitData>(companyPrefab);
                        if (storageLimit.m_Limit != buildingMaxStorage)
                        {
                            storageLimit.m_Limit = buildingMaxStorage;
                            EntityManager.SetComponentData(companyPrefab, storageLimit);
                            patchedStorageCompanies++;
                        }
                    }

                    if (QueueInputPurchase(company, building, companyPrefab, buildingMaxStorage, restockTarget, companyWorth, bankruptcyLimit, resourcePrefabs, truckSelectData, ref resourceDatas, ref deliveryTrucks, ref layouts))
                        queuedPurchases++;
                }
            }

            if (patchedVehicleCompanies > 0 || patchedStorageCompanies > 0)
                Mod.log.Info($"Updated {patchedVehicleCompanies} vehicle and {patchedStorageCompanies} storage limits for signature companies.");

            if (queuedPurchases > 0)
                Mod.log.Debug($"Queued {queuedPurchases} priority input purchase(s) for signature companies.");

            if (protectedTenants > 0)
                Mod.log.Info($"Prevented {protectedTenants} non-bankruptcy signature tenant move-away event(s).");

            if (startingResourcesGranted > 0)
                Mod.log.Info($"Granted {startingResourcesGranted} extra starting resource units to new signature tenant(s).");
        }

        private long DoubleStartingResources(Entity company)
        {
            if (!EntityManager.HasBuffer<Resources>(company))
                return 0;

            DynamicBuffer<Resources> resources = EntityManager.GetBuffer<Resources>(company);
            long granted = 0;
            int resourceCount = resources.Length;
            for (int i = 0; i < resourceCount; i++)
            {
                Resources startingResource = resources[i];
                if (startingResource.m_Resource == Resource.Money || startingResource.m_Resource == Resource.NoResource)
                    continue;

                int bonus = GetStartingResourceBonus(startingResource.m_Amount);
                if (bonus > 0)
                {
                    EconomyUtils.AddResources(startingResource.m_Resource, bonus, resources);
                    granted += bonus;
                }
            }
            return granted;
        }

        internal static int GetStartingResourceBonus(int amount)
        {
            return amount > 0 ? (int)Unity.Mathematics.math.min(amount, (long)int.MaxValue - amount) : 0;
        }

        private bool QueueInputPurchase(
            Entity company,
            Entity building,
            Entity companyPrefab,
            int storageLimit,
            int targetPercent,
            int companyWorth,
            int bankruptcyLimit,
            ResourcePrefabs resourcePrefabs,
            DeliveryTruckSelectData truckSelectData,
            ref ComponentLookup<ResourceData> resourceDatas,
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

            int inputCount = 0;
            int totalInputWeight = 0;
            if (process.m_Input1.m_Resource != Resource.NoResource)
            {
                inputCount++;
                totalInputWeight += Unity.Mathematics.math.max(1, process.m_Input1.m_Amount);
            }
            if (process.m_Input2.m_Resource != Resource.NoResource)
            {
                inputCount++;
                totalInputWeight += Unity.Mathematics.math.max(1, process.m_Input2.m_Amount);
            }
            if (inputCount == 0)
                return false;

            int totalInputTarget = (int)Unity.Mathematics.math.min(
                int.MaxValue,
                (long)storageLimit * inputCount * targetPercent / (storageShares * 100L));
            Resource resource = Resource.NoResource;
            int availableAmount = 0;
            int selectedTarget = 0;
            long incomingAmount = 0;

            // ponytail: two inputs are the native process limit, so a tiny direct comparison is clearer than a collection.
            SelectLowerStockInput(company, process.m_Input1, totalInputTarget, totalInputWeight, ref resource, ref availableAmount, ref selectedTarget, ref incomingAmount, ref deliveryTrucks, ref layouts);
            SelectLowerStockInput(company, process.m_Input2, totalInputTarget, totalInputWeight, ref resource, ref availableAmount, ref selectedTarget, ref incomingAmount, ref deliveryTrucks, ref layouts);
            if (resource == Resource.NoResource)
                return false;

            truckSelectData.GetCapacityRange(resource, out _, out int maxTruckCapacity);
            long occupiedAmount = incomingAmount;
            foreach (Resources storedResource in EntityManager.GetBuffer<Resources>(company, true))
            {
                if (storedResource.m_Resource != Resource.Money && storedResource.m_Resource != Resource.NoResource)
                    occupiedAmount += Unity.Mathematics.math.max(0, storedResource.m_Amount);
            }
            int storageHeadroom = (int)Unity.Mathematics.math.clamp((long)storageLimit - occupiedAmount, 0L, int.MaxValue);
            int amountNeeded = GetFullLoadPurchaseAmount(maxTruckCapacity, storageHeadroom);
            if (amountNeeded == 0)
                return false;

            float unitPrice = EconomyUtils.GetIndustrialPrice(resource, resourcePrefabs, ref resourceDatas);
            // Keep priority restocking from spending the company's remaining bankruptcy cushion.
            if (!IsPriorityPurchaseSafe(companyWorth, bankruptcyLimit, unitPrice, amountNeeded))
            {
                amountNeeded = GetMinimumTruckLoad(maxTruckCapacity);
                if (amountNeeded > storageHeadroom || !IsPriorityPurchaseSafe(companyWorth, bankruptcyLimit, unitPrice, amountNeeded))
                    return false;
            }

            EntityManager.AddComponentData(company, new ResourceBuyer
            {
                m_Payer = company,
                // Priority orders use imports so the requested full load is not clipped by
                // local seller stock changing between pathfinding and the actual sale.
                m_Flags = SetupTargetFlags.Import,
                m_ResourceNeeded = resource,
                m_AmountNeeded = amountNeeded,
                m_Location = EntityManager.GetComponentData<Transform>(building).m_Position
            });
            return true;
        }

        private int GetCompanyWorth(
            Entity company,
            IndustrialProcessData process,
            ResourcePrefabs resourcePrefabs,
            ref ComponentLookup<ResourceData> resourceDatas,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks,
            ref BufferLookup<LayoutElement> layouts)
        {
            if (!EntityManager.HasBuffer<Resources>(company))
                return int.MinValue;

            DynamicBuffer<Resources> resources = EntityManager.GetBuffer<Resources>(company, true);
            bool industrial = !EntityManager.HasComponent<ServiceAvailable>(company);
            if (!EntityManager.HasBuffer<OwnedVehicle>(company))
                return EconomyUtils.GetCompanyTotalWorth(industrial, process, resources, resourcePrefabs, ref resourceDatas);

            DynamicBuffer<OwnedVehicle> vehicles = EntityManager.GetBuffer<OwnedVehicle>(company, true);
            return EconomyUtils.GetCompanyTotalWorth(industrial, process, resources, vehicles, ref layouts, ref deliveryTrucks, resourcePrefabs, ref resourceDatas);
        }

        private bool IsMatureBankruptcy(Entity company, int companyWorth, int bankruptcyLimit)
        {
            if (!EntityManager.HasComponent<CompanyStatisticData>(company))
                return false;

            uint lowIncomeSince = EntityManager.GetComponentData<CompanyStatisticData>(company).m_LastFrameLowIncome;
            return IsMatureBankruptcy(companyWorth, bankruptcyLimit, lowIncomeSince, m_SimulationSystem.frameIndex);
        }

        internal static bool IsMatureBankruptcy(int companyWorth, int bankruptcyLimit, uint lowIncomeSince, uint frameIndex)
        {
            return companyWorth < bankruptcyLimit &&
                lowIncomeSince != 0 &&
                unchecked(frameIndex - lowIncomeSince) > BankruptcyGraceFrames;
        }

        internal static bool IsPriorityPurchaseSafe(int companyWorth, int bankruptcyLimit, float unitPrice, int amount)
        {
            if (amount <= 0)
                return false;

            long purchaseReserve = (long)Unity.Mathematics.math.ceil(Unity.Mathematics.math.max(0f, unitPrice) * amount);
            return companyWorth - purchaseReserve >= bankruptcyLimit;
        }

        internal static int GetMinimumTruckLoad(int maxTruckCapacity)
        {
            return maxTruckCapacity > 0
                ? (int)(((long)maxTruckCapacity * MinimumTruckFillPercent + 99) / 100)
                : 0;
        }

        internal static int GetFullLoadPurchaseAmount(int maxTruckCapacity, int storageHeadroom)
        {
            int amount = Unity.Mathematics.math.min(maxTruckCapacity, Unity.Mathematics.math.max(0, storageHeadroom));
            return amount >= GetMinimumTruckLoad(maxTruckCapacity) ? amount : 0;
        }

        internal static int GetInputTargetAmount(int totalInputTarget, int inputWeight, int totalInputWeight)
        {
            return totalInputTarget > 0 && inputWeight > 0 && totalInputWeight > 0
                ? (int)Unity.Mathematics.math.min(int.MaxValue, (long)totalInputTarget * inputWeight / totalInputWeight)
                : 0;
        }

        internal static bool ShouldSelectInput(int candidateAmount, int candidateTarget, int selectedAmount, int selectedTarget)
        {
            return candidateTarget > 0 && candidateAmount < candidateTarget &&
                (selectedTarget <= 0 || (long)candidateAmount * selectedTarget < (long)selectedAmount * candidateTarget);
        }

        private SignatureCompanyHistory ObserveCompany(Entity building, Entity company)
        {
            if (!EntityManager.HasComponent<SignatureCompanyHistory>(building))
            {
                SignatureCompanyHistory added = new SignatureCompanyHistory(company);
                EntityManager.AddComponentData(building, added);
                return added;
            }

            SignatureCompanyHistory history = EntityManager.GetComponentData<SignatureCompanyHistory>(building);
            SignatureCompanyHistory updated = ObserveCompany(history, company, GetUnexpectedDepartureReason(history.m_CurrentCompany));
            if (history.m_CurrentCompany != updated.m_CurrentCompany ||
                history.m_LastReason != updated.m_LastReason ||
                history.m_PendingReason != updated.m_PendingReason)
                EntityManager.SetComponentData(building, updated);
            return updated;
        }

        internal static SignatureCompanyHistory ObserveCompany(SignatureCompanyHistory history, Entity company, CompanyDepartureReason unexpectedReason)
        {
            if (history.m_CurrentCompany == company)
                return history;

            if (history.m_CurrentCompany != Entity.Null)
            {
                history.m_LastReason = history.m_PendingReason != CompanyDepartureReason.None
                    ? history.m_PendingReason
                    : unexpectedReason;
            }

            history.m_CurrentCompany = company;
            history.m_PendingReason = CompanyDepartureReason.None;
            return history;
        }

        private CompanyDepartureReason GetUnexpectedDepartureReason(Entity previousCompany)
        {
            if (previousCompany != Entity.Null &&
                EntityManager.Exists(previousCompany) &&
                !EntityManager.HasComponent<PropertyRenter>(previousCompany))
                return CompanyDepartureReason.PropertyRelocation;

            return CompanyDepartureReason.ExternalOrLoadReplacement;
        }

        private CompanyDepartureReason GetBankruptcyReason(Entity company)
        {
            if (EntityManager.HasComponent<CompanyNotifications>(company))
            {
                CompanyNotifications notifications = EntityManager.GetComponentData<CompanyNotifications>(company);
                if (notifications.m_NoInputEntity != Entity.Null)
                    return CompanyDepartureReason.BankruptcyMissingInputs;
                if (notifications.m_NoCustomersEntity != Entity.Null)
                    return CompanyDepartureReason.BankruptcyNoCustomers;
            }

            if (EntityManager.HasComponent<WorkProvider>(company))
            {
                WorkProvider workProvider = EntityManager.GetComponentData<WorkProvider>(company);
                if (workProvider.m_EducatedNotificationEntity != Entity.Null)
                    return CompanyDepartureReason.BankruptcyEducatedWorkers;
                if (workProvider.m_UneducatedNotificationEntity != Entity.Null)
                    return CompanyDepartureReason.BankruptcyWorkers;
            }

            return CompanyDepartureReason.Bankruptcy;
        }

        private void SelectLowerStockInput(
            Entity company,
            ResourceStack candidate,
            int totalInputTarget,
            int totalInputWeight,
            ref Resource selected,
            ref int selectedAmount,
            ref int selectedTarget,
            ref long incomingAmount,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks,
            ref BufferLookup<LayoutElement> layouts)
        {
            if (candidate.m_Resource == Resource.NoResource)
                return;

            int targetAmount = GetInputTargetAmount(
                totalInputTarget,
                Unity.Mathematics.math.max(1, candidate.m_Amount),
                totalInputWeight);
            int storedAmount = Unity.Mathematics.math.max(
                0,
                EconomyUtils.GetResources(candidate.m_Resource, EntityManager.GetBuffer<Resources>(company, true)));
            long amount = storedAmount;

            foreach (TripNeeded trip in EntityManager.GetBuffer<TripNeeded>(company, true))
            {
                if ((trip.m_Purpose == Purpose.Shopping || trip.m_Purpose == Purpose.CompanyShopping) &&
                    trip.m_Resource == candidate.m_Resource)
                    amount += Unity.Mathematics.math.max(0, trip.m_Data);
            }

            foreach (OwnedVehicle ownedVehicle in EntityManager.GetBuffer<OwnedVehicle>(company, true))
                amount += GetBuyingTruckCommitment(ownedVehicle.m_Vehicle, candidate.m_Resource, ref deliveryTrucks, ref layouts);

            int availableAmount = (int)Unity.Mathematics.math.min(amount, int.MaxValue);
            incomingAmount += availableAmount - storedAmount;
            if (ShouldSelectInput(availableAmount, targetAmount, selectedAmount, selectedTarget))
            {
                selected = candidate.m_Resource;
                selectedAmount = availableAmount;
                selectedTarget = targetAmount;
            }
        }

        private int GetBuyingTruckCommitment(
            Entity vehicle,
            Resource resource,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks,
            ref BufferLookup<LayoutElement> layouts)
        {
            long amount = 0;
            if (layouts.HasBuffer(vehicle))
            {
                DynamicBuffer<LayoutElement> layout = layouts[vehicle];
                if (layout.Length > 0)
                {
                    foreach (LayoutElement element in layout)
                        amount += GetBuyingTruckUnitCommitment(element.m_Vehicle, resource, ref deliveryTrucks);
                    return (int)Unity.Mathematics.math.min(amount, int.MaxValue);
                }
            }

            return GetBuyingTruckUnitCommitment(vehicle, resource, ref deliveryTrucks);
        }

        private int GetBuyingTruckUnitCommitment(
            Entity vehicle,
            Resource resource,
            ref ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks)
        {
            if (!deliveryTrucks.HasComponent(vehicle))
                return 0;

            int capacity = 0;
            if (EntityManager.HasComponent<PrefabRef>(vehicle))
            {
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;
                if (EntityManager.HasComponent<DeliveryTruckData>(prefab))
                    capacity = EntityManager.GetComponentData<DeliveryTruckData>(prefab).m_CargoCapacity;
            }

            return GetBuyingTruckCommitmentAmount(deliveryTrucks[vehicle], resource, capacity);
        }

        internal static int GetBuyingTruckCommitmentAmount(Game.Vehicles.DeliveryTruck truck, Resource resource, int capacity)
        {
            if (truck.m_Resource != resource || (truck.m_State & DeliveryTruckFlags.Buying) == 0)
                return 0;

            return (truck.m_State & DeliveryTruckFlags.Loaded) != 0
                ? Unity.Mathematics.math.max(0, truck.m_Amount)
                : Unity.Mathematics.math.max(0, capacity);
        }
    }
}
