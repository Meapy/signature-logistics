import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";

const entity = { index: 42, version: 7 };
const details = [{ entity, resource: "Chemicals", cargo: 12000, capacity: 25000, distance: 1500 }];

function element(type, props, ...children) {
  return { type, props: { ...props, children: children.length > 1 ? children : children[0] } };
}

globalThis.window = {
  React: {
    Fragment: Symbol("Fragment"),
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
    bindValue: () => ({}),
    useValue: () => details
  },
  "cs2/l10n": {
    LocalizedFraction: "LocalizedFraction",
    LocalizedNumber: "LocalizedNumber",
    Unit: { Weight: "weight", Length: "length" }
  }
};

let extendVehicleItem;
const moduleRegistry = {
  extend(path, name, extension) {
    assert.match(path, /vehicles-section\.tsx$/);
    assert.equal(name, "VehicleItem");
    extendVehicleItem = extension;
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
