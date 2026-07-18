import React from "react";
import { bindValue, useValue } from "cs2/api";
import { LocalizedFraction, LocalizedNumber, Unit } from "cs2/l10n";
import styles from "./vehicle-details.css";

const vehicleDetails$ = bindValue("SignatureFix", "vehicleDetails", []);
const vehiclesModule = "game-ui/game/components/selected-info-panel/selected-info-sections/shared-sections/vehicles-section/vehicles-section.tsx";

function sameEntity(left, right) {
  return left.index === right.index && left.version === right.version;
}

export default function register(moduleRegistry) {
  console.log("[Fix Signatures] UI module registered.");
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
