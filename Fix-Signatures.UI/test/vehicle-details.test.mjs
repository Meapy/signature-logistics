import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";

const entity = { index: 42, version: 7 };
const details = [{ entity, resource: "Chemicals", cargo: 12000, capacity: 25000, distance: 1500 }];
const limits = {
  visible: true,
  overridden: true,
  maxVehicles: 20,
  maxStorage: 600,
  workerMultiplier: 3,
  productionMultiplier: 4,
  globalMaxVehicles: 20,
  globalMaxStorage: 500,
  globalWorkerMultiplier: 1,
  globalProductionMultiplier: 1,
  minMultiplier: 1,
  maxMultiplier: 10
};
const departure = { visible: true, reason: "Bankruptcy: Missing materials" };
const triggers = [];

function element(type, props, ...children) {
  return { type, props: { ...props, children: children.length > 1 ? children : children[0] } };
}

globalThis.window = {
  React: {
    Fragment: Symbol("Fragment"),
    useEffect(effect) { effect(); },
    useRef(value) { return { current: value }; },
    useState(value) { return [value, () => {}]; },
    createElement: element,
    cloneElement(node, props, ...children) {
      return {
        ...node,
        props: {
          ...node.props,
          ...props,
          children: children.length ? (children.length > 1 ? children : children[0]) : node.props.children
        }
      };
    }
  },
  "cs2/api": {
    bindValue: (_group, name) => ({ name }),
    trigger: (...args) => triggers.push(args),
    useValue: (binding) => binding.name === "vehicleDetails" ? details : binding.name === "buildingLimits" ? limits : departure
  },
  "cs2/l10n": {
    LocalizedFraction: "LocalizedFraction",
    LocalizedNumber: "LocalizedNumber",
    Unit: { Weight: "weight", Length: "length" }
  }
};

let extendVehicleItem;
let extendSelectedInfoSections;
const moduleRegistry = {
  get(path, name) {
    if (path === "game-ui/common/input/slider/slider.tsx" && name === "Slider") return "Slider";
    if (path === "game-ui/common/input/slider/slider.tsx" && name === "useStepTransformer") return (step) => ({ step });
    if (path.endsWith("info-section.tsx") && name === "InfoSection") return "InfoSection";
    if (path.endsWith("info-row.tsx") && name === "InfoRow") return "InfoRow";
    if (path.endsWith("button.tsx") && name === "Button") return "Button";
    if (path.endsWith("paradox-secondary-button.module.scss") && name === "classes") return "SecondaryButtonTheme";
    assert.fail(`Unexpected module export ${name}`);
  },
  extend(path, name, extension) {
    if (name === "VehicleItem") {
      assert.match(path, /vehicles-section\.tsx$/);
      extendVehicleItem = extension;
    }
    else if (name === "selectedInfoSectionComponents") {
      assert.match(path, /selected-info-sections\.tsx$/);
      extendSelectedInfoSections = extension;
    }
    else assert.fail(`Unexpected extension ${name}`);
  }
};

const bundle = await import(`${pathToFileURL("./dist/Fix-Signatures.mjs").href}?test=${Date.now()}`);
bundle.default(moduleRegistry);

const OriginalVehicleItem = () => element("InfoRow", {
  left: element("Name", null, "Delivery Pickup"),
  link: element("Link", null, "Buying")
});
const VehicleItemWithDetails = extendVehicleItem(OriginalVehicleItem);
assert.equal(extendVehicleItem(VehicleItemWithDetails), VehicleItemWithDetails);
const row = VehicleItemWithDetails({ vehicle: { entity } });
const linkChildren = row.props.link.props.children;

assert.equal(row.props.left.props.children[0].type, "Name");
assert.equal(row.props.left.props.children[1].props.children[1].type, "LocalizedFraction");
assert.equal(linkChildren.props.children[0], "Buying");
assert.equal(linkChildren.props.children[1].props.children[1].type, "LocalizedNumber");

const OriginalVehiclesSection = () => element("OriginalVehiclesSection", null);
const OriginalCompanySection = () => element("OriginalCompanySection", null);
const sectionComponents = extendSelectedInfoSections({
  "Game.UI.InGame.VehiclesSection": OriginalVehiclesSection,
  "Game.UI.InGame.CompanySection": OriginalCompanySection
});
const wrappedVehiclesSection = sectionComponents["Game.UI.InGame.VehiclesSection"];
const wrappedCompanySection = sectionComponents["Game.UI.InGame.CompanySection"];
extendSelectedInfoSections(sectionComponents);
assert.equal(sectionComponents["Game.UI.InGame.VehiclesSection"], wrappedVehiclesSection);
assert.equal(sectionComponents["Game.UI.InGame.CompanySection"], wrappedCompanySection);
const section = sectionComponents["Game.UI.InGame.VehiclesSection"]({});
const controls = section.props.children[0];
const vehicleSlider = controls.props.children[2].props.children;
const storageSlider = controls.props.children[4].props.children;
const workerSlider = controls.props.children[6].props.children;
const productionSlider = controls.props.children[8].props.children;
const resetRow = controls.props.children[9];
const resetButton = resetRow.props.right;

assert.equal(controls.type, "InfoSection");
assert.equal(controls.props.children[0].props.left, "BUILDING LOGISTICS");
assert.equal(section.props.children[1].type, OriginalVehiclesSection);
assert.equal(vehicleSlider.type, "Slider");
assert.equal(vehicleSlider.props.value, 20);
assert.equal(storageSlider.props.value, 600);

assert.equal(controls.props.children[5].props.left, "Worker capacity");
assert.equal(controls.props.children[5].props.right.props.children, "3x");
assert.equal(workerSlider.type, "Slider");
assert.equal(workerSlider.props.value, 3);
assert.equal(workerSlider.props.start, 1);
assert.equal(workerSlider.props.end, 10);

assert.equal(controls.props.children[7].props.left, "Production");
assert.equal(controls.props.children[7].props.right.props.children, "4x");
assert.equal(productionSlider.type, "Slider");
assert.equal(productionSlider.props.value, 4);
assert.equal(productionSlider.props.start, 1);
assert.equal(productionSlider.props.end, 10);

assert.equal(resetRow.props.left, "Building override");
assert.equal(resetButton.props.children, "Use global");

// The header collapses the section the way VEHICLES IN USE does.
const header = controls.props.children[0];
assert.equal(header.props.expanded, true);
assert.equal(typeof header.props.onToggleExpanded, "function");
assert.equal(typeof header.props.onSelect, "function");
const companySection = wrappedCompanySection({});
const departureSection = companySection.props.children[1];
assert.equal(companySection.props.children[0].type, OriginalCompanySection);
assert.equal(departureSection.props.children.props.left, "Previous company left");
assert.equal(departureSection.props.children.props.right, "Bankruptcy: Missing materials");
// Each slider sends the full set, so a change to one never resets the other three.
vehicleSlider.props.onChange(25);
storageSlider.props.onChange(900);
workerSlider.props.onChange(7);
productionSlider.props.onChange(9);
resetButton.props.onSelect();
assert.deepEqual(triggers, [
  ["SignatureFix", "setBuildingLimits", 25, 600, 3, 4],
  ["SignatureFix", "setBuildingLimits", 25, 900, 3, 4],
  ["SignatureFix", "setBuildingLimits", 25, 900, 7, 4],
  ["SignatureFix", "setBuildingLimits", 25, 900, 7, 9],
  ["SignatureFix", "resetBuildingLimits"]
]);
