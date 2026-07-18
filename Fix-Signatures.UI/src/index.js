import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { LocalizedFraction, LocalizedNumber, Unit } from "cs2/l10n";
import styles from "./vehicle-details.css";

const vehicleDetails$ = bindValue("SignatureFix", "vehicleDetails", []);
const buildingLimits$ = bindValue("SignatureFix", "buildingLimits", { visible: false });
const vehiclesModule = "game-ui/game/components/selected-info-panel/selected-info-sections/shared-sections/vehicles-section/vehicles-section.tsx";
const sliderModule = "game-ui/common/input/slider/slider.tsx";

function sameEntity(left, right) {
  return left.index === right.index && left.version === right.version;
}

export default function register(moduleRegistry) {
  console.log("[Fix Signatures] UI module registered.");
  const Slider = moduleRegistry.get(sliderModule, "Slider");
  const useStepTransformer = moduleRegistry.get(sliderModule, "useStepTransformer");

  moduleRegistry.extend(vehiclesModule, "VehiclesSection", (OriginalVehiclesSection) => (props) => {
    const limits = useValue(buildingLimits$);
    const [maxVehicles, setMaxVehicles] = React.useState(limits.maxVehicles ?? 10);
    const [maxStorage, setMaxStorage] = React.useState(limits.maxStorage ?? 300);
    const vehiclesRef = React.useRef(maxVehicles);
    const storageRef = React.useRef(maxStorage);
    const vehicleSteps = useStepTransformer(1);
    const storageSteps = useStepTransformer(10);

    React.useEffect(() => {
      vehiclesRef.current = limits.maxVehicles ?? 10;
      storageRef.current = limits.maxStorage ?? 300;
      setMaxVehicles(vehiclesRef.current);
      setMaxStorage(storageRef.current);
    }, [limits.maxVehicles, limits.maxStorage, limits.overridden]);

    const changeVehicles = (value) => {
      vehiclesRef.current = value;
      setMaxVehicles(value);
      trigger("SignatureFix", "setBuildingLimits", value, storageRef.current);
    };
    const changeStorage = (value) => {
      storageRef.current = value;
      setMaxStorage(value);
      trigger("SignatureFix", "setBuildingLimits", vehiclesRef.current, value);
    };
    const reset = () => {
      vehiclesRef.current = limits.globalMaxVehicles;
      storageRef.current = limits.globalMaxStorage;
      setMaxVehicles(limits.globalMaxVehicles);
      setMaxStorage(limits.globalMaxStorage);
      trigger("SignatureFix", "resetBuildingLimits");
    };

    return React.createElement(
      React.Fragment,
      null,
      React.createElement(OriginalVehiclesSection, props),
      limits.visible && React.createElement(
        "div",
        { className: styles.buildingLimits },
        React.createElement(
          "div",
          { className: styles.limitsHeader },
          React.createElement("span", null, "BUILDING LIMITS"),
          React.createElement("span", { className: limits.overridden ? styles.custom : styles.global }, limits.overridden ? "Custom" : "Using global defaults")
        ),
        React.createElement(
          "div",
          { className: styles.limitRow },
          React.createElement("span", null, "Max vehicles"),
          React.createElement("span", { className: styles.limitValue }, maxVehicles)
        ),
        React.createElement(Slider, {
          value: maxVehicles,
          start: 1,
          end: 100,
          valueTransformer: vehicleSteps,
          onChange: changeVehicles
        }),
        React.createElement(
          "div",
          { className: styles.limitRow },
          React.createElement("span", null, "Max storage"),
          React.createElement(LocalizedNumber, { className: styles.limitValue, value: maxStorage * 1000, unit: Unit.Weight })
        ),
        React.createElement(Slider, {
          value: maxStorage,
          start: 10,
          end: 5000,
          valueTransformer: storageSteps,
          onChange: changeStorage
        }),
        limits.overridden && React.createElement("button", { className: styles.resetButton, onClick: reset }, "Use global defaults")
      )
    );
  });

  moduleRegistry.extend(vehiclesModule, "VehicleItem", (OriginalVehicleItem) => (props) => {
    const vehicleDetails = useValue(vehicleDetails$);
    const detail = vehicleDetails.find((item) => sameEntity(item.entity, props.vehicle.entity));
    const originalRow = OriginalVehicleItem(props);

    if (!detail || detail.capacity <= 0)
      return originalRow;

    const originalLink = originalRow.props.link;
    const cargo = React.createElement(
      "span",
      { className: styles.cargo },
      detail.resource && React.createElement("img", {
        className: styles.resourceIcon,
        src: `Media/Game/Resources/${detail.resource}.svg`
      }),
      React.createElement(LocalizedFraction, {
        value: detail.cargo,
        total: detail.capacity,
        unit: Unit.Weight
      })
    );
    const vehicleAndCargo = React.createElement(
      "span",
      { className: styles.vehicleAndCargo },
      originalRow.props.left,
      cargo
    );
    const enhancedLink = React.cloneElement(
      originalLink,
      undefined,
      React.createElement(
        "span",
        { className: styles.status },
        originalLink.props.children,
        detail.distance >= 0 && React.createElement(
          "span",
          { className: styles.distance },
          React.createElement("span", { className: styles.separator }, "\u00b7"),
          React.createElement(LocalizedNumber, { value: detail.distance, unit: Unit.Length })
        )
      )
    );

    return React.cloneElement(originalRow, { left: vehicleAndCargo, link: enhancedLink });
  });
}
