import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { LocalizedFraction, LocalizedNumber, Unit } from "cs2/l10n";
import styles from "./vehicle-details.css";

const vehicleDetails$ = bindValue("SignatureFix", "vehicleDetails", []);
const buildingLimits$ = bindValue("SignatureFix", "buildingLimits", {
  visible: false,
  maxVehicles: 20,
  maxStorage: 500,
  workerMultiplier: 1,
  productionMultiplier: 1,
  globalMaxVehicles: 20,
  globalMaxStorage: 500,
  globalWorkerMultiplier: 1,
  globalProductionMultiplier: 1,
  minMultiplier: 1,
  maxMultiplier: 10
});
const companyDeparture$ = bindValue("SignatureFix", "companyDeparture", {
  visible: false,
  reason: "No departure recorded"
});
const vehiclesModule = "game-ui/game/components/selected-info-panel/selected-info-sections/shared-sections/vehicles-section/vehicles-section.tsx";
const selectedInfoSectionsModule = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
const vehiclesSectionType = "Game.UI.InGame.VehiclesSection";
const companySectionType = "Game.UI.InGame.CompanySection";
const sliderModule = "game-ui/common/input/slider/slider.tsx";
const infoSectionModule = "game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx";
const infoRowModule = "game-ui/game/components/selected-info-panel/shared-components/info-row/info-row.tsx";
const buttonModule = "game-ui/common/input/button/button.tsx";
const secondaryButtonThemeModule = "game-ui/common/input/button/themes/paradox-secondary-button.module.scss";
const buildingLimitsMarker = Symbol.for("SignatureLogistics.BuildingLimits");
const companyDepartureMarker = Symbol.for("SignatureLogistics.CompanyDeparture");
const vehicleDetailsMarker = Symbol.for("SignatureLogistics.VehicleDetails");

function sameEntity(left, right) {
  return left.index === right.index && left.version === right.version;
}

export default function register(moduleRegistry) {
  console.log("[Signature Logistics] UI module registered.");
  const Slider = moduleRegistry.get(sliderModule, "Slider");
  const useStepTransformer = moduleRegistry.get(sliderModule, "useStepTransformer");
  const InfoSection = moduleRegistry.get(infoSectionModule, "InfoSection");
  const InfoRow = moduleRegistry.get(infoRowModule, "InfoRow");
  const Button = moduleRegistry.get(buttonModule, "Button");
  const secondaryButtonTheme = moduleRegistry.get(secondaryButtonThemeModule, "classes");

  const withBuildingLimits = (OriginalVehiclesSection) => (props) => {
    const limits = useValue(buildingLimits$);
    const minMultiplier = limits.minMultiplier ?? 1;
    const maxMultiplier = limits.maxMultiplier ?? 10;
    const [maxVehicles, setMaxVehicles] = React.useState(limits.maxVehicles ?? 20);
    const [maxStorage, setMaxStorage] = React.useState(limits.maxStorage ?? 500);
    const [workers, setWorkers] = React.useState(limits.workerMultiplier ?? 1);
    const [production, setProduction] = React.useState(limits.productionMultiplier ?? 1);
    const vehiclesRef = React.useRef(maxVehicles);
    const storageRef = React.useRef(maxStorage);
    const workersRef = React.useRef(workers);
    const productionRef = React.useRef(production);
    const vehicleSteps = useStepTransformer(1);
    const storageSteps = useStepTransformer(10);
    const multiplierSteps = useStepTransformer(1);
    const [expanded, setExpanded] = React.useState(true);
    const toggleExpanded = () => setExpanded(!expanded);

    React.useEffect(() => {
      vehiclesRef.current = limits.maxVehicles ?? 20;
      storageRef.current = limits.maxStorage ?? 500;
      workersRef.current = limits.workerMultiplier ?? 1;
      productionRef.current = limits.productionMultiplier ?? 1;
      setMaxVehicles(vehiclesRef.current);
      setMaxStorage(storageRef.current);
      setWorkers(workersRef.current);
      setProduction(productionRef.current);
    }, [limits.maxVehicles, limits.maxStorage, limits.workerMultiplier, limits.productionMultiplier, limits.overridden]);

    // Every slider sends the full set, because the managed side stores all four in one component.
    const push = () => trigger(
      "SignatureFix",
      "setBuildingLimits",
      vehiclesRef.current,
      storageRef.current,
      workersRef.current,
      productionRef.current
    );

    const changeVehicles = (value) => {
      vehiclesRef.current = value;
      setMaxVehicles(value);
      push();
    };
    const changeStorage = (value) => {
      storageRef.current = value;
      setMaxStorage(value);
      push();
    };
    const changeWorkers = (value) => {
      workersRef.current = value;
      setWorkers(value);
      push();
    };
    const changeProduction = (value) => {
      productionRef.current = value;
      setProduction(value);
      push();
    };
    const reset = () => {
      vehiclesRef.current = limits.globalMaxVehicles;
      storageRef.current = limits.globalMaxStorage;
      workersRef.current = limits.globalWorkerMultiplier ?? 1;
      productionRef.current = limits.globalProductionMultiplier ?? 1;
      setMaxVehicles(vehiclesRef.current);
      setMaxStorage(storageRef.current);
      setWorkers(workersRef.current);
      setProduction(productionRef.current);
      trigger("SignatureFix", "resetBuildingLimits");
    };

    return React.createElement(
      React.Fragment,
      null,
      limits.visible && React.createElement(
        InfoSection,
        { className: styles.buildingLimits },
        // The header collapses the section the way VEHICLES IN USE does. InfoRow drives its own chevron from
        // `expanded`; `onToggleExpanded` is the prop the vanilla expandable rows use, and `onSelect` is passed as
        // well so the whole row stays clickable. The children below are gated on our own state regardless, so the
        // section still collapses even if only one of those handlers is wired up by the game's component.
        React.createElement(
          InfoRow,
          {
            uppercase: true,
            disableFocus: true,
            expanded: expanded,
            onToggleExpanded: toggleExpanded,
            onSelect: toggleExpanded,
            left: "BUILDING LOGISTICS",
            right: React.createElement("span", { className: limits.overridden ? styles.custom : styles.global }, limits.overridden ? "CUSTOM" : "GLOBAL")
          }
        ),
        expanded && React.createElement(
          InfoRow,
          {
            disableFocus: true,
            left: "Vehicle limit",
            right: React.createElement("span", { className: styles.limitValue }, maxVehicles)
          }
        ),
        expanded && React.createElement("div", { className: styles.sliderRow }, React.createElement(Slider, {
            value: maxVehicles,
            start: 1,
            end: 100,
            valueTransformer: vehicleSteps,
            onChange: changeVehicles
          })
        ),
        expanded && React.createElement(
          InfoRow,
          {
            disableFocus: true,
            left: "Storage limit",
            right: React.createElement(LocalizedNumber, { className: styles.limitValue, value: maxStorage * 1000, unit: Unit.Weight })
          }
        ),
        expanded && React.createElement("div", { className: styles.sliderRow }, React.createElement(Slider, {
            value: maxStorage,
            start: 10,
            end: 5000,
            valueTransformer: storageSteps,
            onChange: changeStorage
          })
        ),
        expanded && React.createElement(
          InfoRow,
          {
            disableFocus: true,
            left: "Worker capacity",
            right: React.createElement("span", { className: styles.limitValue }, workers + "x")
          }
        ),
        expanded && React.createElement("div", { className: styles.sliderRow }, React.createElement(Slider, {
            value: workers,
            start: minMultiplier,
            end: maxMultiplier,
            valueTransformer: multiplierSteps,
            onChange: changeWorkers
          })
        ),
        expanded && React.createElement(
          InfoRow,
          {
            disableFocus: true,
            left: "Production",
            right: React.createElement("span", { className: styles.limitValue }, production + "x")
          }
        ),
        expanded && React.createElement("div", { className: styles.sliderRow }, React.createElement(Slider, {
            value: production,
            start: minMultiplier,
            end: maxMultiplier,
            valueTransformer: multiplierSteps,
            onChange: changeProduction
          })
        ),
        expanded && limits.overridden && React.createElement(
          InfoRow,
          {
            disableFocus: true,
            left: "Building override",
            right: React.createElement(
              Button,
              { theme: secondaryButtonTheme, className: styles.resetButton, onSelect: reset },
              "Use global"
            )
          }
        )
      ),
      React.createElement(OriginalVehiclesSection, props)
    );
  };

  const withCompanyDeparture = (OriginalCompanySection) => (props) => {
    const departure = useValue(companyDeparture$);
    return React.createElement(
      React.Fragment,
      null,
      React.createElement(OriginalCompanySection, props),
      departure.visible && React.createElement(
        InfoSection,
        null,
        React.createElement(InfoRow, {
          disableFocus: true,
          left: "Previous company left",
          right: departure.reason
        })
      )
    );
  };

  moduleRegistry.extend(selectedInfoSectionsModule, "selectedInfoSectionComponents", (components) => {
    if (!components[vehiclesSectionType][buildingLimitsMarker]) {
      components[vehiclesSectionType] = withBuildingLimits(components[vehiclesSectionType]);
      components[vehiclesSectionType][buildingLimitsMarker] = true;
    }
    if (!components[companySectionType][companyDepartureMarker]) {
      components[companySectionType] = withCompanyDeparture(components[companySectionType]);
      components[companySectionType][companyDepartureMarker] = true;
    }
    return components;
  });

  moduleRegistry.extend(vehiclesModule, "VehicleItem", (OriginalVehicleItem) => {
    if (OriginalVehicleItem[vehicleDetailsMarker])
      return OriginalVehicleItem;

    const VehicleItemWithDetails = (props) => {
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
    };

    VehicleItemWithDetails[vehicleDetailsMarker] = true;
    return VehicleItemWithDetails;
  });
}
