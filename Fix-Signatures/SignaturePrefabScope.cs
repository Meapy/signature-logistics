using Game.Common;
using Game.Companies;
using Game.Prefabs;
using System.Collections.Generic;
using Unity.Entities;

namespace SignatureFix
{
    /// <summary>
    /// Scopes the mod's vehicle and storage limits to a single signature tenant.
    ///
    /// <para>
    /// <see cref="TransportCompanyData"/> and <see cref="StorageLimitData"/> are prefab-level components:
    /// Game.Prefabs.ProcessingCompany and Game.Prefabs.StorageLimit add them in GetPrefabComponents and set
    /// them once in Initialize. Every vanilla consumer resolves them as ComponentLookup&lt;T&gt;[PrefabRef.m_Prefab]
    /// of a company entity - IndustrialAISystem.UpdateIndustrialJob, BuyingCompanySystem, IndustrialDemandSystem
    /// and TripNeededSystem (through VehicleUtils.GetTransportCompanyAvailableVehicles) all do exactly that.
    /// </para>
    /// <para>
    /// A signature building's tenant is an ordinary company using a stock company prefab, so writing that prefab
    /// raises the limits for every company in the city sharing it. That was the reported bug: settings leaking to
    /// all commercial, office and industrial buildings instead of only signature ones.
    /// </para>
    /// <para>
    /// Instead, each signature tenant gets a private copy of its company prefab and only that company's PrefabRef
    /// is repointed at the copy. The copy carries <see cref="Unity.Entities.Prefab"/>, which excludes it from every
    /// vanilla EntityQuery - the decompiled game never passes EntityQueryOptions.IncludePrefab anywhere - so it can
    /// never be drawn from a company spawn pool such as CommercialSpawnSystem.m_CommercialCompanyPrefabGroup, and
    /// never inflates prefab-side counts. ComponentLookup reads ignore query filters, so the consumers listed above
    /// still resolve the elevated values for the signature tenant alone.
    /// </para>
    /// <para>
    /// Nothing here reaches a save. Copies are never serialized, and PrefabRef is written out through
    /// Game.Serialization.PrefabReferences.Check, which encodes PrefabData.m_Index - identical on the copy and the
    /// authored prefab - so a saved city records the stock prefab and reloads pointing at it. The scope is rebuilt
    /// from scratch after each load.
    /// </para>
    /// </summary>
    internal sealed class SignaturePrefabScope
    {
        internal struct ScopedPrefab
        {
            public Entity m_Source;
            public Entity m_Clone;
            public int m_MaxVehicles;
            public int m_MaxStorage;
            public int m_WorkerMultiplier;
            public int m_ProductionMultiplier;

            /// <summary>
            /// The authored process, captured when the copy was taken. Multipliers are always applied as
            /// <c>base x factor</c> against this rather than read back off the copy, so re-applying cannot ratchet
            /// the values upward.
            /// </summary>
            public bool m_HasProcess;
            public IndustrialProcessData m_BaseProcess;

            /// <summary>
            /// Commercial tenants take their worker ceiling from <c>ServiceCompanyData.m_MaxWorkersPerCell</c>
            /// (<c>CompanyUtils.GetCommercialMaxFittingWorkers</c>) rather than from the process, so it has to be
            /// scaled too or the worker slider does nothing for them.
            /// </summary>
            public bool m_HasService;
            public ServiceCompanyData m_BaseService;
        }

        /// <summary>The limits and multipliers to give one signature tenant.</summary>
        internal readonly struct ScopeRequest
        {
            public readonly int m_MaxVehicles;
            public readonly int m_MaxStorage;
            public readonly int m_WorkerMultiplier;
            public readonly int m_ProductionMultiplier;

            public ScopeRequest(int maxVehicles, int maxStorage, int workerMultiplier, int productionMultiplier)
            {
                m_MaxVehicles = maxVehicles;
                m_MaxStorage = maxStorage;
                m_WorkerMultiplier = workerMultiplier;
                m_ProductionMultiplier = productionMultiplier;
            }

            public bool Matches(ScopedPrefab scoped)
            {
                return scoped.m_MaxVehicles == m_MaxVehicles &&
                    scoped.m_MaxStorage == m_MaxStorage &&
                    scoped.m_WorkerMultiplier == m_WorkerMultiplier &&
                    scoped.m_ProductionMultiplier == m_ProductionMultiplier;
            }
        }

        private readonly Dictionary<Entity, ScopedPrefab> m_Scoped = new Dictionary<Entity, ScopedPrefab>();
        private readonly List<Entity> m_Expired = new List<Entity>();
        private readonly PrefabSystem m_PrefabSystem;

        internal SignaturePrefabScope(PrefabSystem prefabSystem)
        {
            m_PrefabSystem = prefabSystem;
        }

        internal int ScopedCount => m_Scoped.Count;

        /// <summary>
        /// Ensures <paramref name="company"/> reads its limits from a private prefab copy carrying the requested
        /// values, and returns the prefab entity the caller should treat as this company's prefab. Returns the
        /// authored prefab unchanged when it carries neither limit component, so nothing is cloned needlessly.
        /// </summary>
        internal Entity Apply(EntityManager entityManager, Entity company, ScopeRequest request, ref int scopedCompanies)
        {
            return Apply(entityManager, company, request, ref scopedCompanies, out _);
        }

        /// <summary>
        /// As above, and reports through <paramref name="workerScalingChanged"/> whether the worker multiplier for this
        /// tenant just became effective - either the scope was created or the multiplier value changed.
        ///
        /// <para>
        /// The caller uses that to decide whether to nudge <c>WorkProvider.m_MaxWorkers</c> once. It must be once and
        /// only on change: rewriting it on every pass fights anything else that manages workplaces, which is exactly
        /// how this mod broke the Company Workplaces feature of rcav8tr's Change Company mod - that override was
        /// overwritten within 64 frames and forced up to this mod's scaled ceiling.
        /// </para>
        /// </summary>
        internal Entity Apply(EntityManager entityManager, Entity company, ScopeRequest request, ref int scopedCompanies, out bool workerScalingChanged)
        {
            workerScalingChanged = false;
            if (!entityManager.HasComponent<PrefabRef>(company))
                return Entity.Null;

            Entity current = entityManager.GetComponentData<PrefabRef>(company).m_Prefab;

            // A company whose prefab reference does not resolve has already lost everything the game reads from a
            // prefab - its process, workplaces and storage limit - and cannot be repaired from here, because the
            // identity needed to find the right prefab lived on the entity that is gone. Report it rather than
            // cloning from a dangling reference and compounding the damage.
            if (current == Entity.Null || !entityManager.Exists(current))
            {
                Mod.log.Warn($"Company {company.Index} references a prefab that no longer exists; leaving it untouched. Replacing the building will reseat a healthy tenant.");
                return Entity.Null;
            }

            if (m_Scoped.TryGetValue(company, out ScopedPrefab scoped))
            {
                if (current == scoped.m_Clone && entityManager.Exists(scoped.m_Clone))
                {
                    if (!request.Matches(scoped))
                    {
                        workerScalingChanged = scoped.m_WorkerMultiplier != request.m_WorkerMultiplier;
                        scoped.m_MaxVehicles = request.m_MaxVehicles;
                        scoped.m_MaxStorage = request.m_MaxStorage;
                        scoped.m_WorkerMultiplier = request.m_WorkerMultiplier;
                        scoped.m_ProductionMultiplier = request.m_ProductionMultiplier;
                        WriteScopedValues(entityManager, scoped);
                        m_Scoped[company] = scoped;
                        scopedCompanies++;
                    }
                    return scoped.m_Clone;
                }

                // The game reassigned this company a different prefab, or the copy is gone. Drop the stale scope
                // without touching the PrefabRef the game just wrote.
                Release(entityManager, company, scoped);
                current = entityManager.GetComponentData<PrefabRef>(company).m_Prefab;
            }

            // `current` is now an authored prefab. Only take a copy when there is something to override on it, and
            // only when it actually looks like a registered company prefab - PrefabData is what lets the copy resolve
            // back to the same PrefabBase for the UI, for save serialization, and for recovery in ResolveSource.
            if (!entityManager.HasComponent<PrefabData>(current))
                return current;

            bool hasProcess = entityManager.HasComponent<IndustrialProcessData>(current);
            bool hasService = entityManager.HasComponent<ServiceCompanyData>(current);
            if (!entityManager.HasComponent<TransportCompanyData>(current) &&
                !entityManager.HasComponent<StorageLimitData>(current) &&
                !hasProcess && !hasService)
                return current;

            Entity clone = entityManager.Instantiate(current);
            // Instantiate strips the Prefab tag; adding it back hides the copy from every vanilla query while
            // leaving it fully readable through ComponentLookup.
            entityManager.AddComponent<Unity.Entities.Prefab>(clone);
            if (entityManager.HasComponent<Created>(clone))
                entityManager.RemoveComponent<Created>(clone);
            if (entityManager.HasComponent<Updated>(clone))
                entityManager.RemoveComponent<Updated>(clone);

            ScopedPrefab created = new ScopedPrefab
            {
                m_Source = current,
                m_Clone = clone,
                m_MaxVehicles = request.m_MaxVehicles,
                m_MaxStorage = request.m_MaxStorage,
                m_WorkerMultiplier = request.m_WorkerMultiplier,
                m_ProductionMultiplier = request.m_ProductionMultiplier,
                m_HasProcess = hasProcess,
                // Captured from the authored prefab before anything is scaled, so every later write is base x factor.
                m_BaseProcess = hasProcess ? entityManager.GetComponentData<IndustrialProcessData>(current) : default,
                m_HasService = hasService,
                m_BaseService = hasService ? entityManager.GetComponentData<ServiceCompanyData>(current) : default
            };

            WriteScopedValues(entityManager, created);
            entityManager.SetComponentData(company, new PrefabRef { m_Prefab = clone });
            m_Scoped[company] = created;
            scopedCompanies++;
            workerScalingChanged = true;
            return clone;
        }

        /// <summary>
        /// Restores and destroys every scope whose company is no longer a live signature tenant.
        /// </summary>
        internal void ReleaseUnlisted(EntityManager entityManager, HashSet<Entity> liveTenants)
        {
            m_Expired.Clear();
            foreach (KeyValuePair<Entity, ScopedPrefab> pair in m_Scoped)
            {
                if (!liveTenants.Contains(pair.Key) || !entityManager.Exists(pair.Key))
                    m_Expired.Add(pair.Key);
            }

            ReleaseExpired(entityManager);
        }

        /// <summary>
        /// Restores every company to its authored prefab and destroys all copies. Used on disable and destroy so the
        /// mod leaves no scoped prefab behind.
        /// </summary>
        internal void ReleaseAll(EntityManager entityManager)
        {
            m_Expired.Clear();
            foreach (KeyValuePair<Entity, ScopedPrefab> pair in m_Scoped)
                m_Expired.Add(pair.Key);

            ReleaseExpired(entityManager);
            m_Scoped.Clear();
        }

        /// <summary>
        /// Drops all bookkeeping without touching entities. Used after a load, when the recorded entities belong to a
        /// world that no longer exists and their indices may already have been reused.
        /// </summary>
        internal void Forget()
        {
            m_Scoped.Clear();
            m_Expired.Clear();
        }

        private void ReleaseExpired(EntityManager entityManager)
        {
            foreach (Entity company in m_Expired)
            {
                if (m_Scoped.TryGetValue(company, out ScopedPrefab scoped))
                    Release(entityManager, company, scoped);
            }
            m_Expired.Clear();
        }

        private void Release(EntityManager entityManager, Entity company, ScopedPrefab scoped)
        {
            m_Scoped.Remove(company);

            bool companyStillPointsAtCopy =
                entityManager.Exists(company) &&
                entityManager.HasComponent<PrefabRef>(company) &&
                entityManager.GetComponentData<PrefabRef>(company).m_Prefab == scoped.m_Clone;

            if (companyStillPointsAtCopy)
            {
                Entity source = ResolveSource(entityManager, scoped);
                if (source == Entity.Null)
                {
                    // The authored prefab is gone and could not be re-resolved. Destroying the copy here would leave
                    // the company pointing at a destroyed entity, which strips every prefab-derived value it has -
                    // process, workplaces, storage limit - and leaves a building with no employees, no storage and
                    // zero efficiency. Leak the copy instead; it carries Unity.Entities.Prefab, so nothing queries it.
                    return;
                }

                entityManager.SetComponentData(company, new PrefabRef { m_Prefab = source });
            }

            if (entityManager.Exists(scoped.m_Clone))
                entityManager.DestroyEntity(scoped.m_Clone);
        }

        /// <summary>
        /// Returns the authored prefab entity to hand a company back. Prefab entities are not stable across a load:
        /// <c>PrefabSystem</c> can rebuild them and have <c>ReplacePrefabSystem</c> swap the old entity out
        /// (Game.Prefabs/PrefabSystem.cs:798-805), which invalidates a source recorded earlier. When that has
        /// happened the current entity is recovered through <c>PrefabData.m_Index</c>, which the copy shares with
        /// the prefab it was taken from.
        /// </summary>
        private Entity ResolveSource(EntityManager entityManager, ScopedPrefab scoped)
        {
            if (entityManager.Exists(scoped.m_Source))
                return scoped.m_Source;

            if (m_PrefabSystem == null || !entityManager.Exists(scoped.m_Clone) ||
                !entityManager.HasComponent<PrefabData>(scoped.m_Clone))
                return Entity.Null;

            PrefabData prefabData = entityManager.GetComponentData<PrefabData>(scoped.m_Clone);
            if (!m_PrefabSystem.TryGetPrefab(prefabData, out PrefabBase prefabBase) || prefabBase == null)
                return Entity.Null;

            return m_PrefabSystem.TryGetEntity(prefabBase, out Entity entity) && entityManager.Exists(entity)
                ? entity
                : Entity.Null;
        }

        /// <summary>
        /// Writes the scoped limits and multipliers onto the copy. Every multiplied value is derived from
        /// <see cref="ScopedPrefab.m_BaseProcess"/> rather than read back off the copy, so calling this repeatedly is
        /// idempotent and lowering a slider returns the value to its authored figure instead of compounding.
        /// </summary>
        private static void WriteScopedValues(EntityManager entityManager, ScopedPrefab scoped)
        {
            Entity prefab = scoped.m_Clone;

            if (entityManager.HasComponent<TransportCompanyData>(prefab))
            {
                TransportCompanyData transportCompany = entityManager.GetComponentData<TransportCompanyData>(prefab);
                if (transportCompany.m_MaxTransports != scoped.m_MaxVehicles)
                {
                    transportCompany.m_MaxTransports = scoped.m_MaxVehicles;
                    entityManager.SetComponentData(prefab, transportCompany);
                }
            }

            if (entityManager.HasComponent<StorageLimitData>(prefab))
            {
                StorageLimitData storageLimit = entityManager.GetComponentData<StorageLimitData>(prefab);
                if (storageLimit.m_Limit != scoped.m_MaxStorage)
                {
                    storageLimit.m_Limit = scoped.m_MaxStorage;
                    entityManager.SetComponentData(prefab, storageLimit);
                }
            }

            if (scoped.m_HasService && entityManager.HasComponent<ServiceCompanyData>(prefab))
            {
                ServiceCompanyData scaledService = scoped.m_BaseService;
                scaledService.m_MaxWorkersPerCell = scoped.m_BaseService.m_MaxWorkersPerCell *
                    Unity.Mathematics.math.clamp(scoped.m_WorkerMultiplier, SignatureFixSettings.MinMultiplier, SignatureFixSettings.MaxMultiplier);

                ServiceCompanyData currentService = entityManager.GetComponentData<ServiceCompanyData>(prefab);
                if (currentService.m_MaxWorkersPerCell != scaledService.m_MaxWorkersPerCell)
                    entityManager.SetComponentData(prefab, scaledService);
            }

            if (!scoped.m_HasProcess || !entityManager.HasComponent<IndustrialProcessData>(prefab))
                return;

            IndustrialProcessData scaled = ScaleProcess(scoped.m_BaseProcess, scoped.m_WorkerMultiplier, scoped.m_ProductionMultiplier);
            IndustrialProcessData currentProcess = entityManager.GetComponentData<IndustrialProcessData>(prefab);
            if (!ProcessEquals(currentProcess, scaled))
                entityManager.SetComponentData(prefab, scaled);
        }

        /// <summary>
        /// Applies the worker and production multipliers to an authored process.
        ///
        /// <para>
        /// Workers scale through <c>m_MaxWorkersPerCell</c>, which is what
        /// <c>CompanyUtils.GetIndustrialAndOfficeFittingWorkers</c> (Game.Simulation/CompanyUtils.cs:42) multiplies by
        /// lot size and level to get the employment ceiling. This raises the ceiling only - the AI still hires against
        /// the city's labour supply and the company's profitability.
        /// </para>
        /// <para>
        /// Production scales all three resource amounts together. <c>ProcessingCompanySystem</c> derives input
        /// consumption as <c>input.m_Amount / output.m_Amount</c> (:187, :193), so scaling output alone would make the
        /// recipe cheaper rather than faster. Scaling every stack keeps the ratio identical and multiplies throughput,
        /// which means a 10x factory genuinely needs 10x the materials delivered.
        /// </para>
        /// </summary>
        internal static IndustrialProcessData ScaleProcess(IndustrialProcessData authored, int workerMultiplier, int productionMultiplier)
        {
            int workers = Unity.Mathematics.math.clamp(workerMultiplier, SignatureFixSettings.MinMultiplier, SignatureFixSettings.MaxMultiplier);
            int production = Unity.Mathematics.math.clamp(productionMultiplier, SignatureFixSettings.MinMultiplier, SignatureFixSettings.MaxMultiplier);

            IndustrialProcessData scaled = authored;
            scaled.m_MaxWorkersPerCell = authored.m_MaxWorkersPerCell * workers;
            scaled.m_Input1.m_Amount = ScaleAmount(authored.m_Input1.m_Amount, production);
            scaled.m_Input2.m_Amount = ScaleAmount(authored.m_Input2.m_Amount, production);
            scaled.m_Output.m_Amount = ScaleAmount(authored.m_Output.m_Amount, production);
            return scaled;
        }

        private static int ScaleAmount(int authoredAmount, int multiplier)
        {
            if (authoredAmount <= 0)
                return authoredAmount;

            return (int)Unity.Mathematics.math.min((long)authoredAmount * multiplier, int.MaxValue);
        }

        private static bool ProcessEquals(IndustrialProcessData a, IndustrialProcessData b)
        {
            return a.m_MaxWorkersPerCell == b.m_MaxWorkersPerCell &&
                a.m_Input1.m_Amount == b.m_Input1.m_Amount &&
                a.m_Input2.m_Amount == b.m_Input2.m_Amount &&
                a.m_Output.m_Amount == b.m_Output.m_Amount;
        }
    }
}
