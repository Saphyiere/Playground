import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

// Exercise the shipped geometry against SVG bounds without a browser or a second copy of the positioning algorithm.
const source = readFileSync(new URL("../../src/Prima.UI/Components/Sheet.razor.js", import.meta.url), "utf8");

function harness() {
    const followed = [];
    const measures = [1, 7, 12].map((number, row) => {
        const top = 110 + row * 300;
        const staves = [
            { left: 70, top, right: 220, bottom: top + 44 },
            { left: 74, top: top + 66, right: 230, bottom: top + 110 },
        ];
        return {
            dataset: { id: `m${number}` },
            staves,
            querySelectorAll: () => staves.map(r => ({ getBoundingClientRect: () => r })),
            getBoundingClientRect: () => ({ left: 70, top, right: 230, bottom: top + 110 }),
            closest: () => ({ scrollIntoView: () => followed.push(number) }),
        };
    });
    const overlay = {
        children: [],
        querySelector() { return this.children[0]; },
        appendChild(node) { this.children.push(node); },
    };
    const element = {
        querySelector: selector => measures.find(m => selector.includes(`"${m.dataset.id}"`)),
        querySelectorAll: () => measures,
        getBoundingClientRect: () => ({ left: 50, top: 80, right: 650, bottom: 1000 }),
        closest: () => null,
    };
    const document = {
        createElementNS() {
            return {
                attributes: {},
                setAttribute(name, value) { this.attributes[name] = value; },
                remove() { overlay.children = overlay.children.filter(node => node !== this); },
            };
        },
    };
    const context = vm.createContext({ element, overlay, document });
    vm.runInContext(source.replace(/^export /gm, ""), context);
    vm.runInContext("sheets.set(element, { overlay });", context);
    return { context, element, overlay, measures, followed };
}

test("bar outlines follow both coordinates and both staves across three wrapped systems", () => {
    const { context, element, overlay, followed } = harness();
    for (const [id, top] of [["m1", 20], ["m7", 320], ["m12", 620]]) {
        context.outline(element, id);
        assert.equal(overlay.children.length, 1);
        const rect = overlay.children[0].attributes;
        assert.equal(rect.x, 16);
        assert.equal(rect.y, top);
        assert.equal(rect.width, 168);
        assert.equal(rect.height, 130);
    }
    assert.deepEqual(followed, [1, 7, 12]);
});

test("repainting a selection reads current bounds and clearing removes its only outline", () => {
    const { context, element, overlay, measures } = harness();
    context.outline(element, "m7");
    for (const r of measures[1].staves) {
        r.left += 180;
        r.right += 200;
        r.top += 300;
        r.bottom += 300;
    }
    context.outline(element, "m7");
    assert.equal(overlay.children.length, 1);
    assert.equal(overlay.children[0].attributes.x, 196);
    assert.equal(overlay.children[0].attributes.y, 620);
    assert.equal(overlay.children[0].attributes.width, 188);
    context.outline(element, null);
    assert.equal(overlay.children.length, 0);
    context.outline(element, "missing");
    assert.equal(overlay.children.length, 0);
});

test("bar hit testing distinguishes repeated horizontal positions and empty space between systems", () => {
    const { context, element } = harness();
    assert.equal(context.measureAt(element, 100, 130).dataset.id, "m1");
    assert.equal(context.measureAt(element, 100, 490).dataset.id, "m7");
    assert.equal(context.measureAt(element, 100, 730).dataset.id, "m12");
    assert.equal(context.measureAt(element, 100, 620), undefined);
    assert.equal(context.measureAt(element, 500, 130), undefined);
});
