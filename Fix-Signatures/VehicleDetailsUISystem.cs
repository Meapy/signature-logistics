using Colossal.UI.Binding;
using Game.Buildings;
using Game.Economy;
using Game.Objects;
using Game.Prefabs;
using Game.UI;
using Game.UI.InGame;
using Game.Vehicles;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;
using DeliveryTruck = Game.Vehicles.DeliveryTruck;

namespace SignatureFix
{
    public partial class VehicleDetailsUISystem : UISystemBase
    {
        private const string BindingGroup = "SignatureFix";
        private const int UpdateEveryFrames = 15;

        private SelectedInfoUISystem m_SelectedInfo;
        private int m_Frame;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            AddUpdateBinding(new RawValueBinding(BindingGroup, "vehicleDetails", WriteVehicleDetails));
        }

        [Preserve]
        protected override void OnUpdate()
        {
            // ponytail: four UI refreshes per second at 60 FPS are smooth enough; make this time-based only if variable-rate UI becomes visible.
            if (++m_Frame >= UpdateEveryFrames)
            {
                m_Frame = 0;
                base.OnUpdate();
            }
        }

        private void WriteVehicleDetails(IJsonWriter writer)
        {
            Entity owner = m_SelectedInfo.selectedEntity;
            if (!EntityManager.HasBuffer<OwnedVehicle>(owner) &&
                !CompanyUIUtils.HasCompany(EntityManager, owner, m_SelectedInfo.selectedPrefab, out owner))
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                return;
            }

            if (!EntityManager.HasBuffer<OwnedVehicle>(owner))
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                return;
            }

            DynamicBuffer<OwnedVehicle> vehicles = EntityManager.GetBuffer<OwnedVehicle>(owner, true);
            int deliveryVehicleCount = 0;
            foreach (OwnedVehicle ownedVehicle in vehicles)
            {
                if (EntityManager.HasComponent<DeliveryTruck>(ownedVehicle.m_Vehicle))
                    deliveryVehicleCount++;
            }

            writer.ArrayBegin(deliveryVehicleCount);
            foreach (OwnedVehicle ownedVehicle in vehicles)
            {
                Entity vehicle = ownedVehicle.m_Vehicle;
                if (!EntityManager.HasComponent<DeliveryTruck>(vehicle))
                    continue;

                GetCargo(vehicle, out Resource resource, out int cargo, out int capacity);
                float distance = GetDistanceToDestination(vehicle);

                writer.TypeBegin("SignatureFix.VehicleDetail");
                writer.PropertyName("entity");
                UnityWriters.Write(writer, vehicle);
                writer.PropertyName("resource");
                writer.Write(resource == Resource.NoResource ? string.Empty : resource.ToString());
                writer.PropertyName("cargo");
                writer.Write(cargo);
                writer.PropertyName("capacity");
                writer.Write(capacity);
                writer.PropertyName("distance");
                writer.Write(distance);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private void GetCargo(Entity vehicle, out Resource resource, out int cargo, out int capacity)
        {
            resource = Resource.NoResource;
            cargo = 0;
            capacity = 0;

            if (EntityManager.HasBuffer<LayoutElement>(vehicle))
            {
                DynamicBuffer<LayoutElement> layout = EntityManager.GetBuffer<LayoutElement>(vehicle, true);
                if (layout.Length > 0)
                {
                    foreach (LayoutElement element in layout)
                        AddCargoUnit(element.m_Vehicle, ref resource, ref cargo, ref capacity);
                    return;
                }
            }

            AddCargoUnit(vehicle, ref resource, ref cargo, ref capacity);
        }

        private void AddCargoUnit(Entity vehicle, ref Resource resource, ref int cargo, ref int capacity)
        {
            if (!EntityManager.HasComponent<DeliveryTruck>(vehicle))
                return;

            DeliveryTruck truck = EntityManager.GetComponentData<DeliveryTruck>(vehicle);
            if (resource == Resource.NoResource)
                resource = truck.m_Resource;
            else if (truck.m_Resource != Resource.NoResource && truck.m_Resource != resource)
                resource = Resource.NoResource;

            if ((truck.m_State & DeliveryTruckFlags.Loaded) != 0)
                cargo += truck.m_Amount;

            if (!EntityManager.HasComponent<PrefabRef>(vehicle))
                return;

            Entity prefab = EntityManager.GetComponentData<PrefabRef>(vehicle).m_Prefab;
            if (EntityManager.HasComponent<DeliveryTruckData>(prefab))
                capacity += EntityManager.GetComponentData<DeliveryTruckData>(prefab).m_CargoCapacity;
        }

        private float GetDistanceToDestination(Entity vehicle)
        {
            Entity destination = VehicleUIUtils.GetDestination(EntityManager, vehicle);
            if (destination == Entity.Null ||
                !EntityManager.HasComponent<Transform>(vehicle) ||
                !EntityManager.HasComponent<Transform>(destination))
                return -1f;

            float3 vehiclePosition = EntityManager.GetComponentData<Transform>(vehicle).m_Position;
            float3 destinationPosition = EntityManager.GetComponentData<Transform>(destination).m_Position;
            return math.distance(vehiclePosition, destinationPosition);
        }
    }
}
