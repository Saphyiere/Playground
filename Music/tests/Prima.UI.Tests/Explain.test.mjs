import assert from "node:assert/strict";
import test from "node:test";
import vm from "node:vm";
import { script } from "./script.mjs";

// What's this's guard (wwwroot/explain.js): while it's on, a tap on anything says what it is instead of pressing it, caught on
// its way down before the page's own handlers; What's this's own keys and cards, and the music, are left alone.
const source = script(new URL("../../src/Prima.UI/wwwroot/explain.js", import.meta.url)).replace(/^export /gm, "");

class Element {
    constructor(attributes = {}, parent = null) {
        this.attributes = attributes;
        this.parent = parent;
        this.className = attributes.class ?? "";
        this.style = { setProperty: (name, value) => { this.style[name] = value; } };
        this.dataset = {};
    }
    getAttribute(name) { return this.attributes[name] ?? null; }
    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }
    matches(selector) {
        return selector.split(",").map(s => s.trim()).some(s => s.startsWith("[") ? s.slice(1, -1) in this.attributes : this.className.split(" ").includes(s.slice(1)));
    }
    closest(selector) {
        for (let node = this; node; node = node.parent) if (node.matches(selector)) return node;
        return null;
    }
    getBoundingClientRect() { return { left: 10, top: 20, right: 60, bottom: 50 }; }
    get isConnected() { return true; }
}

function page() {
    const listeners = [];
    const said = [];
    const root = new Element();
    root.clientWidth = 1280;
    root.clientHeight = 800;
    const context = {
        Element,
        window: {
            addEventListener: (type, fn, options) => listeners.push({ type, fn, capture: options?.capture }),
            removeEventListener: (type, fn) => listeners.splice(listeners.findIndex(l => l.type === type && l.fn === fn), 1),
        },
        document: { documentElement: root },
    };
    vm.createContext(context);
    vm.runInContext(`${source}\nthis.api = { on, off, place };`, context);
    const dotnet = { invokeMethodAsync: (...args) => { said.push(args); return Promise.resolve(); } };
    // A tap: down the capture listeners first, and on to the page's own (bubbling) handler unless it's stopped.
    const tap = (target, { type = "click", trusted = true } = {}) => {
        const event = {
            type, target, isTrusted: trusted, stopped: false, prevented: false,
            stopPropagation() { this.stopped = true; }, stopImmediatePropagation() { this.stopped = true; }, preventDefault() { this.prevented = true; },
        };
        for (const l of listeners.filter(l => l.type === type && l.capture)) l.fn(event);
        return { pressed: !event.stopped, prevented: event.prevented };
    };
    return { api: context.api, root, listeners, said, dotnet, tap };
}

test("while it's on, a tap on a control says what it is and doesn't press it", () => {
    const { api, root, said, dotnet, tap } = page();
    const bar = new Element({ class: "play-bar" });
    const loop = new Element({ "data-explain": "loop", class: "play-mode-option" }, bar);
    const word = new Element({ class: "loop-word" }, loop);

    assert.deepEqual(tap(word), { pressed: true, prevented: false });
    api.on(dotnet);
    assert.equal(root.getAttribute("data-explaining"), "");
    assert.deepEqual(tap(word), { pressed: false, prevented: true });
    assert.deepEqual(said, [["Tapped", "loop", 10, 20, 60, 50]]);
    // A press and a touch on the way to the tap are stopped too, and say nothing.
    assert.equal(tap(word, { type: "pointerdown" }).pressed, false);
    assert.equal(said.length, 1);
});

test("an unmarked thing says there's nothing yet, a script's tap says nothing, and off it's pressed again", () => {
    const { api, root, said, dotnet, tap } = page();
    const plain = new Element({ class: "lb-row" });
    api.on(dotnet);

    assert.equal(tap(plain).pressed, false);
    assert.deepEqual(said, [["Tapped", null, 10, 20, 60, 50]]);
    // The piano's keys press a button by a script's click: stopped, and nothing said.
    assert.equal(tap(plain, { trusted: false }).pressed, false);
    assert.equal(said.length, 1);
    api.off();
    assert.equal(root.getAttribute("data-explaining"), null);
    assert.equal(tap(plain).pressed, true);
});

test("its own keys and cards, and the music, are left alone", () => {
    const { api, said, dotnet, tap } = page();
    const key = new Element({ "data-explain-keep": "", class: "mu-btn whats-this-key" });
    const card = new Element({ "data-explain-keep": "", class: "explain-card" });
    const choice = new Element({ class: "mu-btn" }, card);
    const sheet = new Element({ class: "sheet piece-sheet" });
    const note = new Element({ class: "notehead" }, sheet);
    api.on(dotnet);

    for (const target of [key, choice, note]) assert.equal(tap(target).pressed, true);
    assert.deepEqual(said, []);
});

test("a card stands under what it's about, or over it near the screen's foot, within the screen", () => {
    const { api } = page();
    const card = new Element();
    Object.assign(card, { offsetWidth: 300, offsetHeight: 160, dataset: {} });

    api.place(card, 100, 100, 140, 120);
    assert.deepEqual([card.style.left, card.style.top, card.dataset.side, card.style.visibility], ["8px", "132px", "below", "visible"]);
    api.place(card, 1200, 700, 1240, 720);
    assert.deepEqual([card.style.left, card.style.top, card.dataset.side], ["972px", "528px", "above"]);
});
