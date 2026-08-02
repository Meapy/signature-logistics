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
        }

        private readonly Dictionary<Entity, ScopedPrefab> m_Scoped = new Dictionary<Entity, ScopedPrefab>();
        private readonly List<Entity> m_Expired = new List<Entity>();

        internal int ScopedCount => m_Scoped.Count;

        /// <summary>
        /// Ensures <paramref name="company"/> reads its limits from a private prefab copy carrying the requested
        /// values, and returns the prefab entity the caller should treat as this company's prefab. Returns the
        /// authored prefab unchanged when it carries neither limit component, so nothing is cloned needlessly.
        /// </summary>
        internal Entity Apply(EntityManager entityManager, Entity company, int maxVehicles, int maxStorage, ref int scopedCompanies)
        {
            if (!entityManager.HasComponent<PrefabRef>(company))
                return Entity.Null;

            Entity current = entityManager.GetComponentData<PrefabRef>(company).m_Prefab;

            if (m_Scoped.TryGetValue(company, out ScopedPrefab scoped))
            {
                if (current == scoped.m_Clone && entityManager.Exists(scoped.m_Clone))
                {
                    if (scoped.m_MaxVehicles != maxVehicles || scoped.m_MaxStorage != maxStorage)
                    {
                        WriteLimits(entityManager, scoped.m_Clone, maxVehicles, maxStorage);
                        scoped.m_MaxVehicles = maxVehicles;
                        scoped.m_MaxStorage = maxStorage;
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

            // `current` is now an authored prefab. Only take a copy when there is something to override on it.
            if (!entityManager.HasComponent<TransportCompanyData>(current) &&
                !entityManager.HasComponent<StorageLimitData>(current))
                return current;

            Entity clone = entityManager.Instantiate(current);
            // Instantiate strips the Prefab tag; adding it back hides the copy from every vanilla query while
            // leaving it fully readable through ComponentLookup.
            entityManager.AddComponent<Unity.Entities.Prefab>(clone);
            if (entityManager.HasComponent<Created>(clone))
                entityManager.RemoveComponent<Created>(clone);
            if (entityManager.HasComponent<Updated>(clone))
                entityManager.RemoveComponent<Updated>(clone);

            WriteLimits(entityManager, clone, maxVehicles, maxStorage);
            entityManager.SetComponentData(company, new PrefabRef { m_Prefab = clone });
            m_Scoped[company] = new ScopedPrefab
            {
                m_Source = current,
                m_Clone = clone,
                m_MaxVehicles = maxVehicles,
                m_MaxStorage = maxStorage
            };
            scopedCompanies++;
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
            // Only restore when the company still points at our copy, so a prefab the game assigned meanwhile is
            // never clobbered.
            if (entityManager.Exists(company) &&
                entityManager.HasComponent<PrefabRef>(company) &&
                entityManager.GetComponentData<PrefabRef>(company).m_Prefab == scoped.m_Clone &&
                entityManager.Exists(scoped.m_Source))
                entityManager.SetComponentData(company, new PrefabRef { m_Prefab = scoped.m_Source });

            if (entityManager.Exists(scoped.m_Clone))
                entityManager.DestroyEntity(scoped.m_Clone);

            m_Scoped.Remove(company);
        }

        private static void WriteLimits(EntityManager entityManager, Entity prefab, int maxVehicles, int maxStorage)
        {
            if (entityManager.HasComponent<TransportCompanyData>(prefab))
            {
                TransportCompanyData transportCompany = entityManager.GetComponentData<TransportCompanyData>(prefab);
                if (transportCompany.m_MaxTransports != maxVehicles)
                {
                    transportCompany.m_MaxTransports = maxVehicles;
                    entityManager.SetComponentData(prefab, transportCompany);
                }
            }

            if (entityManager.HasComponent<StorageLimitData>(prefab))
            {
                StorageLimitData storageLimit = entityManager.GetComponentData<StorageLimitData>(prefab);
                if (storageLimit.m_Limit != maxStorage)
                {
                    storageLimit.m_Limit = maxStorage;
                    entityManager.SetComponentData(prefab, storageLimit);
                }
            }
        }
    }
}
