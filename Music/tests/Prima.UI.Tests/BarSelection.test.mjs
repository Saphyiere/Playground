import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { drawing } from "./drawing.mjs";
import { script } from "./script.mjs";

const source = script(new URL("../../src/Prima.UI/wwwroot/bars.js", import.meta.url));

class Element {
    constructor() { this.children = []; this.parts = []; this.listeners = new Map(); this.attributes = {}; this.style = {}; }
    set innerHTML(html) {
        this.parts = [];
        const stack = [this];
        for (const [, closing, , attributes] of html.matchAll(/<(\/?)(div|span|button)\b([^>]*)>/g)) {
            if (closing) { stack.pop(); continue; }
            const child = new Element();
            child.attributes = Object.fromEntries([...attributes.matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
            child.className = child.attributes.class ?? "";
            child.parent = stack.at(-1);
            child.parent.parts.push(child);
            stack.push(child);
        }
    }
    querySelectorAll(selector) {
        const cls = selector.match(/\.([\w-]+)$/)?.[1];
        return [...this.parts, ...this.children].flatMap(e => [...(e.className?.split(" ").includes(cls) ? [e] : []), ...e.querySelectorAll(selector)]);
    }
    querySelector(selector) { return this.querySelectorAll(selector)[0]; }
    getAttribute(name) { return this.attributes[name]; }
    setAttribute(name, value) { this.attributes[name] = value; }
    appendChild(child) { child.parent = this; this.children.push(child); }
    replaceChildren(...children) { this.children = children; }
    addEventListener(name, fn) { this.listeners.set(name, fn); }
    blur() { this.blurred = true; }
    click() {
        const event = { currentTarget: this, stopped: false, stopPropagation() { this.stopped = true; } };
        for (let node = this; node && !event.stopped; node = node.parent) {
            event.currentTarget = node;
            node.listeners.get("click")?.(event);
        }
    }
}

function harness() {
    const calls = [];
    const sheet = new Element();
    const state = { choice: { from: 2, to: null, lit: true }, dotnet: { invokeMethodAsync(...args) { calls.push(args); return Promise.resolve(); } } };
    const context = vm.createContext({ document: { createElement: () => new Element() } });
    vm.runInContext(source.replace(/^export /gm, ""), context);
    return { context, sheet, state, calls };
}

test("clear sends its own action without selecting the bar underneath or triggering Loop", () => {
    const { context, sheet, state, calls } = harness();
    sheet.addEventListener("click", () => calls.push(["Chose", 2]));
    const layer = context.marksLayer(state, sheet);
    const clear = layer.querySelector(".bar-tab-clear");
    assert.ok(clear);
    assert.equal(clear.getAttribute("data-piano"), "off");
    clear.click();
    assert.deepEqual(calls, [["ClearSelection"]]);
    assert.equal(clear.blurred, true);
});

test("an unselected choice removes single-bar and loop bands, tabs and handles", () => {
    for (const loop of [false, true]) {
        const { context, sheet, state } = harness();
        Object.assign(context, { sheet, list: [{ n: 2, line: 1, left: 20, right: 200, top: 50, bottom: 150 }] });
        vm.runInContext("drawn = () => ({ sheet, list }); placeTab = () => {};", context);
        sheet.clientWidth = 800;
        state.choice.to = loop ? 2 : null;
        context.drawMarks(state);
        const layer = sheet.children[0];
        assert.equal(layer.querySelector(".bar-bands").children.length, 1);
        assert.equal(layer.querySelector(".bar-tab").hidden, false);
        state.choice = { from: 1, to: null, lit: false };
        context.drawMarks(state);
        assert.equal(layer.querySelector(".bar-bands").children.length, 0);
        assert.equal(layer.querySelector(".bar-tab").hidden, true);
        assert.ok(layer.querySelectorAll(".bar-handle").every(h => h.hidden));
    }
});

// A long piece's lines come a part at a time. The line the piece was left at may not be drawn yet when the bars are first shown,
// nor, once it is, enough music under it for the frame to scroll that far: put back too soon, the piece opened a line short.
test("the line a piece was left at is waited for, and for the room under it, while its lines are still coming", () => {
    const { context, sheet, state } = harness();
    const bars = (from, to) => Array.from({ length: to - from + 1 }, (_, i) => ({ n: from + i, line: Math.floor((from + i - 1) / 4), left: 0, right: 100, top: Math.floor((from + i - 1) / 4) * 200 + 40, bottom: Math.floor((from + i - 1) / 4) * 200 + 160 }));
    // The frame goes no further down than what's drawn lets it.
    const frame = { furthest: 0, top: 0, get scrollTop() { return this.top; }, set scrollTop(to) { this.top = Math.min(to, this.furthest); } };
    Object.assign(state, { frame, at: 21 });
    Object.assign(context, { sheet, list: bars(1, 12) });
    vm.runInContext("drawn = () => ({ sheet, list }); drawMarks = () => {}; drawStrip = () => {};", context);
    sheet.offsetTop = 0;

    // Its line isn't drawn yet.
    sheet.setAttribute("data-drawn", "part");
    context.drawAll(state);
    assert.equal(state.at, 21);
    assert.equal(frame.scrollTop, 0);

    // Its line is drawn, the last so far: the frame can't bring it to the top yet, and goes as far as it can.
    context.list = bars(1, 24);
    frame.furthest = 700;
    context.drawAll(state);
    assert.equal(frame.scrollTop, 700);
    assert.equal(state.at, 21);

    // More under it: it's at the top (a little under it), and that's the end of it.
    context.list = bars(1, 40);
    frame.furthest = 1800;
    context.drawAll(state);
    assert.equal(frame.scrollTop, 5 * 200 + 40 - 24);
    assert.equal(state.at, null);

    // A bar the music doesn't have is given up once the music is all there.
    Object.assign(state, { at: 99 });
    context.drawAll(state);
    assert.equal(state.at, 99);
    sheet.setAttribute("data-drawn", "all");
    context.drawAll(state);
    assert.equal(state.at, null);
});

// bars.js on a stand-in for Verovio's drawing (drawing.mjs), bars 19 on, with `choice` chosen; what it tells .NET is kept. What it
// draws isn't looked at here, only where it finds the bars.
function onMusic(d, choice) {
    const calls = [];
    const context = vm.createContext({ document: { createElement: () => new Element() }, requestAnimationFrame: () => 0, cancelAnimationFrame: () => { } });
    vm.runInContext(source.replace(/^export /gm, ""), context);
    vm.runInContext("drawMarks = () => {}; drawStrip = () => {}; bubble = () => {};", context);
    const state = {
        frame: d.frame, number: new Map(d.ids.map((id, i) => [id, 19 + i])),
        choice: { firstBar: 19, ids: d.ids, lit: true, playing: false, explain: false, ...choice },
        dotnet: { invokeMethodAsync(...args) { calls.push(args); return Promise.resolve(); } },
    };
    return { context, state, calls };
}

const pointer = (clientX, clientY) => ({ pointerId: 1, clientX, clientY, preventDefault() { }, stopPropagation() { } });

// A loop's handle on the music, held by a finger.
function handleOf(end) {
    const listeners = new Map();
    return {
        dataset: { end }, classList: { add() { }, remove() { } }, setPointerCapture() { },
        addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name),
        fire: (name, event) => listeners.get(name)?.(event),
    };
}

// Marks drawn with bar 21 that run on to bar 24, each where it's printed: [kind, top, bottom].
const marks = {
    "an 8va over the top staff": ["octave", 60, 70],
    "an 8vb under the lower staff": ["octave", 262, 272],
    "a slur over the notes": ["slur", 70, 84],
    "a hairpin between the staves": ["hairpin", 160, 170],
    "a pedal line under the staves": ["pedal", 262, 272],
    "a trill's line": ["trill", 64, 74],
};

// The owner's bug (28 Nov 2026): under an 8va from bar 21 to 24, choosing bar 21 lit 21 to 24, and a loop's end went from 21
// straight to 25. Verovio draws the 8va with bar 21, so that bar's box ran on to bar 24, and every bar after it on the line was
// found inside bar 21 first. The same with any mark drawn with a bar that runs on past it.
test("a bar under a mark drawn with the bar before it is measured, found and lit by its own staff lines", () => {
    for (const [name, [kind, top, bottom]] of [["nothing", [null]], ...Object.entries(marks)]) {
        const d = drawing({ marks: kind ? [[kind, 21, 24, top, bottom]] : [] });
        const { context, state } = onMusic(d, { from: 21, to: null });
        const { list } = context.drawn(state);
        assert.deepEqual(Array.from(list, b => b.n), [19, 20, 21, 22, 23, 24, 25, 26], name);
        for (const b of list) {
            const at = d.at(b.n);
            assert.deepEqual([b.left, b.right], [at.start, at.right], `${name}: bar ${b.n}'s ends`);
            assert.equal(context.barAt(list, at.middle, at.gap), b.n, `${name}: a finger on bar ${b.n}`);
            assert.equal(context.barAt(list, at.left + 2, at.staff), b.n, `${name}: a finger just past bar ${b.n}'s bar line`);
        }
        const [band] = context.bands(list, 21, 21);
        assert.deepEqual([band.first, band.last, band.left, band.right], [21, 21, d.at(21).left - 3, d.at(21).right + 3], `${name}: bar 21 lit`);
    }
});

// Seen in the preview: measured by its staff lines alone, a line's first bar was lit from its staff, the band's edge through the
// bar's number printed a little to the left of it. The number is the bar's own.
test("a line's first bar is lit with its number", () => {
    const d = drawing({ perLine: 4, marks: [["octave", 21, 24, 60, 70]] });
    const { context, state } = onMusic(d, { from: 23, to: null });
    const { list } = context.drawn(state);
    for (const n of [19, 23]) {
        const [band] = context.bands(list, n, n + 1);
        assert.deepEqual([band.left, band.right], [d.at(n).left - 6 - 3, d.at(n + 1).right + 3], `bars ${n} and ${n + 1}`);
    }
});

test("a loop's end dragged under an 8va stops at the bar under the finger, on the 8va's line or the next", () => {
    // Bars 19 to 26 on one line, or 19 to 22 and 23 to 26: the 8va from 21 runs on to the next line.
    for (const perLine of [8, 4]) {
        const d = drawing({ perLine, marks: [["octave", 21, 24, 60, 70]] });
        for (const to of [22, 23, 24, 25]) {
            const { context, state, calls } = onMusic(d, { from: 21, to: 21 });
            const handle = handleOf("to"), from = d.at(21), there = d.at(to);
            context.dragHandle(state, pointer(from.right, from.staff), handle);
            handle.fire("pointermove", pointer(there.middle, there.gap));
            handle.fire("pointerup", pointer(there.middle, there.gap));
            assert.deepEqual(calls, [["Looped", 21, to]], `${perLine} bars a line, dragged to bar ${to}`);
        }
        // And its start, dragged back from bar 24 under the 8va.
        const { context, state, calls } = onMusic(d, { from: 24, to: 24 });
        const handle = handleOf("from"), there = d.at(22);
        context.dragHandle(state, pointer(d.at(24).left, d.at(24).staff), handle);
        handle.fire("pointermove", pointer(there.middle, there.gap));
        handle.fire("pointerup", pointer(there.middle, there.gap));
        assert.deepEqual(calls, [["Looped", 22, 24]], `${perLine} bars a line, its start dragged to bar 22`);
    }
});

// The lit band reaches a little over and under the staves: an octave line printed just there had the band's edge run along it,
// hiding its dashes. A band over a bar under an octave line takes the line in; one over a bar without, the same as ever.
test("a band over bars under an octave line takes the line in, on its line and the next", () => {
    for (const [top, bottom] of [[60, 70], [262, 272]]) {
        for (const perLine of [8, 4]) {
            const d = drawing({ perLine, marks: [["octave", 21, 24, top, bottom]] });
            const { context, state } = onMusic(d, { from: 21, to: null });
            const { list } = context.drawn(state);
            for (const n of [21, 22, 23, 24]) {
                const [band] = context.bands(list, n, n), y = d.at(n).y;
                assert.ok(band.top <= y + top - 12 && band.bottom >= y + bottom + 12, `${perLine} bars a line, the 8v${top < 100 ? "a" : "b"} over bar ${n}: band ${band.top}–${band.bottom}`);
            }
            for (const n of [19, 20, 25, 26]) {
                const [band] = context.bands(list, n, n), y = d.at(n).y;
                assert.deepEqual([band.top, band.bottom], [y + 85 - 12, y + 252 + 12], `${perLine} bars a line, bar ${n}, no 8v`);
            }
        }
    }
});
