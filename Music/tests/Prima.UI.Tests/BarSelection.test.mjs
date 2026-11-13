import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../../src/Prima.UI/wwwroot/bars.js", import.meta.url), "utf8");

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
