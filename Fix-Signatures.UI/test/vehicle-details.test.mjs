import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";

const entity = { index: 42, version: 7 };
const details = [{ entity, resource: "Chemicals", cargo: 12000, capacity: 25000, distance: 1500 }];
const limits = { visible: true, overridden: true, maxVehicles: 20, maxStorage: 600, globalMaxVehicles: 10, globalMaxStorage: 300 };
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
    useValue: (binding) => binding.name === "vehicleDetails" ? details : limits
  },
  "cs2/l10n": {
    LocalizedFraction: "LocalizedFraction",
    LocalizedNumber: "LocalizedNumber",
    Unit: { Weight: "weight", Length: "length" }
  }
};

let extendVehicleItem;
let extendVehiclesSection;
const moduleRegistry = {
  get(path, name) {
    assert.equal(path, "game-ui/common/input/slider/slider.tsx");
    if (name === "Slider") return "Slider";
    if (name === "useStepTransformer") return (step) => ({ step });
    assert.fail(`Unexpected module export ${name}`);
  },
  extend(path, name, extension) {
    assert.match(path, /vehicles-section\.tsx$/);
    if (name === "VehicleItem") extendVehicleItem = extension;
    else if (name === "VehiclesSection") extendVehiclesSection = extension;
    else assert.fail(`Unexpected extension ${name}`);
  }
};

const bundle = await import(`${pathToFileURL("./dist/Fix-Signatures.mjs").href}?test=${Date.now()}`);
bundle.default(moduleRegistry);

const OriginalVehicleItem = () => element("InfoRow", {
  left: element("Name", null, "Delivery Pickup"),
  link: element("Link", null, "Buying")
});
const row = extendVehicleItem(OriginalVehicleItem)({ vehicle: { entity } });
const linkChildren = row.props.link.props.children;

assert.equal(row.props.left.props.children[0].type, "Name");
assert.equal(row.props.left.props.children[1].props.children[1].type, "LocalizedFraction");
assert.equal(linkChildren.props.children[0], "Buying");
assert.equal(linkChildren.props.children[1].props.children[1].type, "LocalizedNumber");

const section = extendVehiclesSection(() => element("OriginalVehiclesSection", null))({});
const controls = section.props.children[1];
const vehicleSlider = controls.props.children[2];
const storageSlider = controls.props.children[4];
const resetButton = controls.props.children[5];

assert.equal(vehicleSlider.type, "Slider");
assert.equal(vehicleSlider.props.value, 20);
assert.equal(storageSlider.props.value, 600);
vehicleSlider.props.onChange(25);
storageSlider.props.onChange(900);
resetButton.props.onClick();
assert.deepEqual(triggers, [
  ["SignatureFix", "setBuildingLimits", 25, 600],
  ["SignatureFix", "setBuildingLimits", 25, 900],
  ["SignatureFix", "resetBuildingLimits"]
]);
