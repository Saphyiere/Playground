import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { drawing } from "./drawing.mjs";
import { script } from "./script.mjs";

// Exercise the shipped geometry against SVG bounds without a browser or a second copy of the positioning algorithm.
const source = script(new URL("../../src/Prima.UI/Components/Sheet.razor.js", import.meta.url));

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
            // Its staff lines run from its staves' left to their right; it has no number printed over it.
            querySelector: selector => selector.endsWith("path") ? { getBoundingClientRect: () => ({ left: Math.min(...staves.map(s => s.left)), right: Math.max(...staves.map(s => s.right)), top: staves[0].top, bottom: staves[0].top + 1 }) } : null,
            getBoundingClientRect: () => ({ left: 70, top, right: 230, bottom: top + 110 }),
            // Its line, which has no octave lines on it.
            closest: () => ({ scrollIntoView: () => followed.push(number), getElementsByClassName: () => [] }),
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

// The owner's bug (28 Nov 2026): under an 8va from bar 21 to 24, a tap on bar 22 chose bar 21. Verovio draws the 8va with bar 21,
// so that bar's box runs on to bar 24, and a tap on the 8va's own line was a tap in bar 21's drawing. On a stand-in for Verovio's
// drawing (drawing.mjs), marks drawn with bar 21 that run on to bar 24: [kind, top, bottom].
test("a tap under a mark drawn with the bar before it, or on the mark, chooses the bar under the finger", () => {
    const context = vm.createContext({ document: {} });
    vm.runInContext(source.replace(/^export /gm, ""), context);
    for (const [kind, top, bottom] of [["octave", 60, 70], ["octave", 262, 272], ["slur", 70, 84], ["slur", 30, 40], ["hairpin", 160, 170], ["pedal", 262, 272]]) {
        const d = drawing({ marks: [[kind, 21, 24, top, bottom]] });
        const name = `${kind} from ${top} to ${bottom}`;
        const mark = d.measures[2].querySelector(`:scope > .${kind} > path`);
        for (const n of [19, 20, 21, 22, 23, 24, 25, 26]) {
            const at = d.at(n), id = `m${n}`;
            assert.equal(context.measureAt(d.sheet, at.middle, at.gap)?.dataset.id, id, `${name}: bar ${n}'s space`);
            assert.equal(context.measureAt(d.sheet, at.left + 2, at.staff)?.dataset.id, id, `${name}: just past bar ${n}'s bar line`);
            // On the paper between the staves, on one of the bar's notes, and on the mark over it.
            assert.equal(context.barTapped(d.sheet, d.svg, at.middle, at.gap)?.dataset.id, id, `${name}: a tap on bar ${n}'s space`);
            assert.equal(context.barTapped(d.sheet, d.measures[n - 19].querySelector(".note"), at.middle, at.staff)?.dataset.id, id, `${name}: a tap on bar ${n}'s note`);
            if (n >= 21 && n <= 24) assert.equal(context.barTapped(d.sheet, mark, at.middle, (top + bottom) / 2)?.dataset.id, id, `${name}: a tap on the mark over bar ${n}`);
        }
        // Off the music, well under the line: no bar.
        assert.equal(context.barTapped(d.sheet, d.svg, d.at(22).middle, 340), null, `${name}: under the line`);
    }
});

// Seen in the preview: an 8vb under one line, and an 8va over the next a little under it. A tap on the 8va's line chose the bar
// over it on the line above, whose 8vb brought it as near, because that bar came first.
test("a tap on an octave line near another line's chooses the bar on its own line", () => {
    const context = vm.createContext({ document: {} });
    vm.runInContext(source.replace(/^export /gm, ""), context);
    // Bars 19 to 22 on a line, its 8vb under bars 21 and 22 down to 272; bars 23 to 26 on the next, from 220 down, its 8va over
    // bars 25 and 26 from 280.
    const d = drawing({ perLine: 4, lineGap: 220, marks: [["octave", 21, 22, 262, 272], ["octave", 25, 26, 60, 70]] });
    const eightva = d.measures[25 - 19].querySelector(":scope > .octave > path");
    for (const n of [25, 26]) {
        const x = d.at(n).middle;
        assert.equal(context.barTapped(d.sheet, eightva, x, 285)?.dataset.id, `m${n}`, `on the 8va over bar ${n}`);
        assert.equal(context.barTapped(d.sheet, d.svg, x, 285)?.dataset.id, `m${n}`, `beside the 8va over bar ${n}`);
        assert.equal(context.barTapped(d.sheet, d.svg, x, 268)?.dataset.id, `m${n - 4}`, `on the 8vb over bar ${n - 4}`);
    }
});
