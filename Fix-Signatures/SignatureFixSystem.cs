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
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace SignatureFix
{
    public partial class SignatureFixSystem : GameSystemBase, Game.Serialization.IPreSerialize
    {
        private EntityQuery m_SignatureBuildings;
        private EntityQuery m_PropertyTenants;
        private EntityQuery m_EconomyParameters;
        private VehicleCapacitySystem m_VehicleCapacitySystem;
        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;
        private SignaturePrefabScope m_PrefabScope;
        private HashSet<Entity> m_LiveTenants;

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
            // Renter buffers are not serialized: Game.Serialization.RenterSystem rebuilds them from each company's
            // PropertyRenter during deserialization. A load-time pass keyed on Renter can therefore run before the
            // buffers exist and find nothing. PropertyRenter is the serialized source of truth, so the load pass
            // starts from tenants and resolves the building through PropertyRenter.m_Property instead.
            m_PropertyTenants = GetEntityQuery(
                ComponentType.ReadOnly<CompanyData>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Game.Common.Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>());
            m_EconomyParameters = GetEntityQuery(ComponentType.ReadOnly<EconomyParameterData>());
            m_VehicleCapacitySystem = World.GetOrCreateSystemManaged<VehicleCapacitySystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabScope = new SignaturePrefabScope(World.GetOrCreateSystemManaged<PrefabSystem>());
            m_LiveTenants = new HashSet<Entity>();
            RequireForUpdate(m_SignatureBuildings);
            RequireForUpdate(m_EconomyParameters);
        }

        [Preserve]
        protected override void OnGamePreload(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // The world is about to be cleared and refilled. Entities recorded for the outgoing city would leave
            // dangling indices that a new entity could reuse, so the scope is dropped and rebuilt from scratch.
            m_PrefabScope.Forget();
            m_LiveTenants.Clear();
        }

        [Preserve]
        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (!mode.IsGame())
                return;

            // Scoped limits must exist before the first simulation update, not one update interval later.
            //
            // ProcessingCompanySystem derives its production amount from the prefab's storage limit
            // (Game.Simulation/ProcessingCompanySystem.cs:220-224):
            //
            //     int x = storageLimitData.m_Limit - num8;   // limit minus storage already used
            //     num = math.min(x, num);                    // never clamped at zero
            //     EconomyUtils.AddResources(output.m_Resource, num, resources);
            //
            // A tenant loaded holding more than the prefab's limit therefore produces a *negative* amount and
            // deletes its own output stock. Saves restore PrefabRef to the authored prefab with its much lower
            // vanilla limit, so every signature tenant stocked above it was emptied - and then went bankrupt as
            // its worth collapsed - in the window before OnUpdate first ran.
            ApplyScopedLimits("OnGameLoadingComplete");
        }

        /// <summary>
        /// Hands every signature tenant back to its authored company prefab immediately before the city is written.
        ///
        /// <para>
        /// This is not optional bookkeeping - it is what keeps the save valid. Entity fields are written through
        /// <c>BinaryWriter.Write(Entity)</c> (Colossal.Serialization.Entities/BinaryWriter.cs:128-141), which remaps
        /// the entity through <c>m_EntityTable</c> - the table of entities actually being serialized - and writes
        /// <c>-1</c> for anything absent from it. A scoped prefab copy carries <c>Unity.Entities.Prefab</c>, so it is
        /// excluded from the queries that build that table. A tenant saved while pointing at a copy therefore has its
        /// <c>PrefabRef</c> written as <c>-1</c>, and <c>BinaryReader.Read(out Entity)</c> turns <c>-1</c> back into
        /// <c>Entity.Null</c> (BinaryReader.cs:146-157). The company reloads with no prefab at all: no process, no
        /// workplaces, no storage limit, zero efficiency.
        /// </para>
        /// <para>
        /// <c>SignatureScopeRestoreSystem</c> re-applies the scopes later in the same Serialize phase, after
        /// <c>WriteSystem</c>, so no simulation frame ever runs with the vanilla limits in place.
        /// </para>
        /// </summary>
        public void PreSerialize(Colossal.Serialization.Entities.Context context)
        {
            m_PrefabScope.ReleaseAll(EntityManager);
            m_LiveTenants.Clear();
        }

        /// <summary>
        /// Re-establishes the scopes released by <see cref="PreSerialize"/>. Called from
        /// <see cref="SignatureScopeRestoreSystem"/> once the city has been written.
        /// </summary>
        internal void RestoreScopedLimitsAfterSerialize()
        {
            ApplyScopedLimits("PostSerialize");
        }

        // Deliberately no OnGameLoaded override. That hook fires from LoadGameSystem.onOnSaveGameLoaded, in the middle
        // of the load pipeline, before prefab references have been resolved back into entities and while PrefabSystem
        // may still rebuild and replace prefab entities. Cloning from a PrefabRef read there copies the wrong entity
        // or an unresolved one. OnGameLoadingComplete runs after all of that has settled.

        /// <summary>
        /// Gives every current signature tenant its scoped company prefab and releases the rest. This is the part
        /// of <see cref="OnUpdate"/> that must not wait for an update interval.
        /// </summary>
        private void ApplyScopedLimits(string hook)
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? SignatureFixSettings.DefaultMaxVehicles;
            int maxStorage = (Mod.Settings?.MaxStorage ?? SignatureFixSettings.DefaultMaxStorage) * SignatureFixSettings.StorageUnitsPerTonne;
            int scopedCompanies = 0;
            int buildingCount = 0;
            int tenantCount = 0;

            try
            {
                m_LiveTenants.Clear();

                // Walk tenants rather than buildings: PropertyRenter survives the save, the building's Renter buffer
                // is only reconstructed later during deserialization.
                using NativeArray<Entity> tenants = m_PropertyTenants.ToEntityArray(Allocator.Temp);
                foreach (Entity company in tenants)
                {
                    if (!EntityManager.Exists(company))
                        continue;

                    Entity building = EntityManager.GetComponentData<PropertyRenter>(company).m_Property;
                    if (building == Entity.Null ||
                        !EntityManager.Exists(building) ||
                        !EntityManager.HasComponent<Signature>(building))
                        continue;

                    buildingCount++;
                    tenantCount++;
                    ResolveBuildingLimits(building, maxVehicles, maxStorage, out int buildingMaxVehicles, out int buildingMaxStorage);
                    m_LiveTenants.Add(company);
                    m_PrefabScope.Apply(EntityManager, company, buildingMaxVehicles, buildingMaxStorage, ref scopedCompanies);
                }

                m_PrefabScope.ReleaseUnlisted(EntityManager, m_LiveTenants);
            }
            catch (System.Exception exception)
            {
                // GameSystemBase.GameLoadingComplete swallows exceptions from this path and only logs them to the
                // game log, so report here too - otherwise a failed load-time pass looks identical to one that
                // simply found nothing to do.
                Mod.log.Error(exception, $"{hook}: scoped limit pass failed after {buildingCount} building(s), {tenantCount} tenant(s).");
                return;
            }

            // Info rather than Debug: this runs once per load, not per update, and it is the line that shows whether
            // signature tenants were protected before the first simulation update.
            Mod.log.Info($"{hook}: {tenantCount} signature tenant(s) across {buildingCount} building(s), {scopedCompanies} newly scoped, {m_PrefabScope.ScopedCount} active. Limits {maxVehicles} vehicles / {maxStorage} storage units.");
        }

        private void ResolveBuildingLimits(Entity building, int globalMaxVehicles, int globalMaxStorage, out int maxVehicles, out int maxStorage)
        {
            maxVehicles = globalMaxVehicles;
            maxStorage = globalMaxStorage;
            if (!EntityManager.HasComponent<SignatureBuildingLimits>(building))
                return;

            SignatureBuildingLimits limits = EntityManager.GetComponentData<SignatureBuildingLimits>(building);
            maxVehicles = Unity.Mathematics.math.clamp(limits.m_MaxVehicles, SignatureFixSettings.MinMaxVehicles, SignatureFixSettings.MaxMaxVehicles);
            maxStorage = Unity.Mathematics.math.clamp(limits.m_MaxStorage, SignatureFixSettings.MinMaxStorage, SignatureFixSettings.MaxMaxStorage) * SignatureFixSettings.StorageUnitsPerTonne;
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            m_PrefabScope.ReleaseAll(EntityManager);
            base.OnStopRunning();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            if (World != null && World.IsCreated)
                m_PrefabScope.ReleaseAll(EntityManager);
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            int maxVehicles = Mod.Settings?.MaxVehicles ?? SignatureFixSettings.DefaultMaxVehicles;
            int maxStorage = (Mod.Settings?.MaxStorage ?? SignatureFixSettings.DefaultMaxStorage) * SignatureFixSettings.StorageUnitsPerTonne;
            int restockTarget = Mod.Settings?.RestockTarget ?? SignatureFixSettings.DefaultRestockTarget;
            int scopedCompanies = 0;
            int queuedPurchases = 0;
            int protectedTenants = 0;
            long startingResourcesGranted = 0;
            ComponentLookup<Game.Vehicles.DeliveryTruck> deliveryTrucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(true);
            ComponentLookup<ResourceData> resourceDatas = GetComponentLookup<ResourceData>(true);
            BufferLookup<LayoutElement> layouts = GetBufferLookup<LayoutElement>(true);
            DeliveryTruckSelectData truckSelectData = m_VehicleCapacitySystem.GetDeliveryTruckSelectData();
            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();
            int bankruptcyLimit = m_EconomyParameters.GetSingleton<EconomyParameterData>().m_CompanyBankruptcyLimit;

            m_LiveTenants.Clear();

            // ponytail: signature buildings are few; replace this scan with renter-change tracking only if profiling says it matters.
            using NativeArray<Entity> signatureBuildings = m_SignatureBuildings.ToEntityArray(Allocator.Temp);
            foreach (Entity building in signatureBuildings)
            {
                // The query result is a snapshot and the loop below makes structural changes, so a building may
                // already be gone by the time it is reached.
                if (!EntityManager.Exists(building) || !EntityManager.HasBuffer<Renter>(building))
                    continue;

                ResolveBuildingLimits(building, maxVehicles, maxStorage, out int buildingMaxVehicles, out int buildingMaxStorage);

                // Copy the renters out: the body makes structural changes, which invalidate a live DynamicBuffer.
                using NativeArray<Renter> renters = EntityManager.GetBuffer<Renter>(building, true).ToNativeArray(Allocator.Temp);
                foreach (Renter renter in renters)
                {
                    Entity company = renter.m_Renter;
                    if (!EntityManager.Exists(company) ||
                        !EntityManager.HasComponent<CompanyData>(company) ||
                        !EntityManager.HasComponent<PrefabRef>(company))
                        continue;

                    m_LiveTenants.Add(company);

                    // Give this tenant a private copy of its company prefab carrying the requested limits, and read
                    // everything below from that copy. Writing the shared prefab instead is what leaked the settings
                    // to every commercial, office and industrial company using the same prefab.
                    Entity companyPrefab = m_PrefabScope.Apply(EntityManager, company, buildingMaxVehicles, buildingMaxStorage, ref scopedCompanies);
                    if (companyPrefab == Entity.Null)
                        continue;

                    // This loop makes structural changes, which invalidate cached lookups. Refresh before every use.
                    deliveryTrucks.Update(this);
                    resourceDatas.Update(this);
                    layouts.Update(this);

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

                    if (QueueInputPurchase(company, building, companyPrefab, buildingMaxStorage, restockTarget, companyWorth, bankruptcyLimit, resourcePrefabs, truckSelectData, ref resourceDatas, ref deliveryTrucks, ref layouts))
                        queuedPurchases++;
                }
            }

            // Hand back the prefab copies of tenants that have moved out or been deleted.
            m_PrefabScope.ReleaseUnlisted(EntityManager, m_LiveTenants);

            // These run on a 64-frame interval. They are Debug rather than Info so a busy city does not write to the
            // log file several times a second: every write is an opportunity to hit the Colossal.Logging reopen defect
            // described in Mod.CreateLogger. Raise the log's effectiveness level to see them.
            if (scopedCompanies > 0)
                Mod.log.Debug($"Applied scoped vehicle and storage limits to {scopedCompanies} signature company prefab copies ({m_PrefabScope.ScopedCount} active).");

            if (queuedPurchases > 0)
                Mod.log.Debug($"Queued {queuedPurchases} priority input purchase(s) for signature companies.");

            if (protectedTenants > 0)
                Mod.log.Debug($"Prevented {protectedTenants} non-bankruptcy signature tenant move-away event(s).");

            if (startingResourcesGranted > 0)
                Mod.log.Debug($"Granted {startingResourcesGranted} extra starting resource units to new signature tenant(s).");
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
