import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

// A whole piece's sheet (Sheet.razor.js, "Whole pieces"): its engraver asked a step at a time, a scroll's parts put on the paper
// as they come, Stop and the rest, a song kept out of sight until its words fit, and the engraver let go of when the sheet goes.
// The engraver here is a stand-in that answers when the test says; SheetEngraving.test.mjs has the real one.
const source = readFileSync(new URL("../../src/Prima.UI/Components/Sheet.razor.js", import.meta.url), "utf8");

const settle = async (turns = 20) => {
    for (let i = 0; i < turns; i++) await new Promise(resolve => setImmediate(resolve));
};

// A part's drawing: so many lines, the glyphs it carries, and whether a song's words run together on it.
const drawing = (lines, glyphs = ["E0A4"], crowded = false) =>
    `<svg><defs>${glyphs.map(g => `<g id="${g}-x"><path d="M0 0"/></g>`).join("")}</defs>${`<g class="system"${crowded ? " data-crowded" : ""}></g>`.repeat(lines)}</svg>\n`;

function harness({ words = false, music = "<mei><measure/><measure/><measure/></mei>" } = {}) {
    const [asked, told, errors, frames, workers] = [[], [], [], [], []];
    class Worker {
        constructor(url) {
            Object.assign(this, { url, posted: [], ended: false });
            workers.push(this);
        }
        postMessage(message) { this.posted.push(message); }
        terminate() { this.ended = true; }
    }
    // A timer of the sheet's (half a minute for a worker to start, say) doesn't keep the tests waiting for it.
    const later = (fn, ms) => {
        const timer = setTimeout(fn, ms);
        timer.unref();
        return timer;
    };
    const context = vm.createContext({
        performance, setTimeout: later, clearTimeout, Worker, screen: { width: 1280, height: 800 }, CSS: { escape: text => text },
        requestAnimationFrame: fn => frames.push(fn), cancelAnimationFrame() { }, console: { error: (...said) => errors.push(said.join(" ")) },
    });
    vm.runInContext(source.replace(/^export /gm, ""), context);

    // The paper: what's put on it is kept as text, each part with its style, its lines and its words' places.
    const paper = {
        parts: [], style: {},
        get childElementCount() { return this.parts.length; },
        get childNodes() { return this.parts; },
        get lastElementChild() { return this.parts.at(-1); },
        insertAdjacentHTML(where, html) {
            const lines = (html.match(/class="system"/g) ?? []).length;
            const together = html.includes("data-crowded");
            this.parts.push({
                html, style: {},
                querySelector: () => null,
                querySelectorAll: what => what === ".system" ? Array(lines).fill({}) : what === ".syl text" && together
                    ? [{ getBoundingClientRect: () => ({ left: 0, right: 30, bottom: 10, width: 30 }) }, { getBoundingClientRect: () => ({ left: 20, right: 50, bottom: 10, width: 30 }) }] : [],
            });
        },
        replaceChildren(...parts) { this.parts = parts; },
        querySelectorAll: () => [],
    };
    const element = {
        isConnected: true, clientWidth: 1000, clientHeight: 600, attributes: {},
        setAttribute(name, value) { this.attributes[name] = value; },
        removeAttribute(name) { delete this.attributes[name]; },
        getAttribute(name) { return this.attributes[name] ?? null; },
        querySelector: what => what === ".sheet-music" ? paper : null,
        closest: () => null,
    };
    const state = {
        number: 1, music, kept: new Map(), moves: [], overlay: { replaceChildren() { } }, line: {},
        options: { layout: "scroll", staffMm: 7, font: "Leipzig", words },
        dotnet: { invokeMethodAsync: (...said) => { told.push(said); return Promise.resolve(); } },
    };
    Object.assign(context, { element, state });
    vm.runInContext("sheets.set(element, state)", context);

    // The engraver stood in for: each step is kept, and answered when the test says (or at once, with `answers`).
    const waiting = [];
    context.answer = (op, ...args) => {
        asked.push([op, ...args]);
        return new Promise((resolve, reject) => waiting.push({ op, args, resolve, reject }));
    };
    const standIn = () => vm.runInContext("pieceEngraver = async () => engine; ask = (op, ...args) => answer(op, ...args)", context);
    const give = result => waiting.shift().resolve(result);
    return { context, element, state, paper, asked, told, errors, frames, workers, waiting, standIn, give };
}

test("a scroll's parts go on the paper as they come, spaced as one page's lines, and .NET hears how far it has got", async () => {
    const { context, element, state, paper, asked, told, frames, standIn, give, waiting } = harness();
    standIn();
    context.drawPiece(element, state);
    await settle();

    // Laid out as parts with no margins of their own; nothing on the paper yet, which waits.
    const [op, mei, options, measures] = asked[0];
    assert.equal(op, "load");
    assert.equal(mei, state.music);
    assert.equal(measures, null);
    assert.equal(options.pageHeight, vm.runInContext("partHeight", context));
    assert.deepEqual([options.pageMarginTop, options.pageMarginBottom, options.adjustPageHeight], [0, 0, true]);
    assert.equal(element.getAttribute("data-drawn"), null);
    give({ pages: 3, lines: 7 });
    await settle();

    // The first part asked for with the drawing's styles (and the text faces, the page not having them); the next is being
    // drawn while it's put on the paper.
    assert.deepEqual(asked.slice(1), [["page", 1, "all"]]);
    give(drawing(3, ["E0A4", "E050"]));
    await settle();
    assert.deepEqual(asked.slice(2), [["page", 2, "none"]]);
    assert.equal(paper.parts.length, 1);
    assert.equal(element.getAttribute("data-drawn"), "part");
    assert.deepEqual(told.at(-1), ["Progress", 3, 7, "coming"]);

    give(drawing(2, ["E0A4", "E062"]));
    await settle();
    give(drawing(2, ["E050"]));
    await settle();
    assert.deepEqual(asked.slice(3).map(step => step.slice(0, 3)), [["page", 3, "none"], ["timemap"]]);
    assert.equal(element.getAttribute("data-drawn"), "all");
    assert.deepEqual(told.at(-1), ["Progress", 7, 7, "done"]);

    // Over the first part the paper's top margin, between parts the seam, under the last the paper's bottom margin: as shares
    // of the paper's width.
    const width = options.pageWidth;
    const share = units => `${units / width * 100}%`;
    assert.deepEqual(paper.parts.map(part => part.style.marginTop), [share(24), share(vm.runInContext("seam", context)), share(vm.runInContext("seam", context))]);
    assert.deepEqual(paper.parts.map(part => part.style.marginBottom), [undefined, undefined, share(24)]);
    // A glyph's outline comes once, with the part that first has it.
    assert.deepEqual(paper.parts.map(part => [...part.html.matchAll(/<g id="(\w+)-x">/g)].map(m => m[1])), [["E0A4", "E050"], ["E062"], []]);

    // Painted, once the browser has: the scroll is one page to .NET. Kept for this size once its time map has come.
    while (frames.length) frames.shift()();
    assert.deepEqual(told.filter(said => said[0] === "Painted"), [["Painted", 1]]);
    assert.equal(state.kept.size, 0);
    give([{ qstamp: 0 }]);
    await settle();
    assert.equal(state.kept.size, 1);
    assert.equal(waiting.length, 0);

    // Drawn again at the same size, the drawing kept goes back on the paper: the engraver isn't asked.
    const steps = asked.length;
    context.drawPiece(element, state);
    await settle();
    assert.equal(asked.length, steps);
    assert.equal(paper.parts.length, 3);
    assert.equal(element.getAttribute("data-drawn"), "all");
});

test("Stop leaves what's drawn, and the rest is drawn from there when it's asked for", async () => {
    const { context, element, state, paper, asked, told, standIn, give } = harness();
    standIn();
    context.drawPiece(element, state);
    await settle();
    give({ pages: 4, lines: 8 });
    await settle();
    give(drawing(2));
    await settle();
    assert.equal(paper.parts.length, 1);

    context.stop(element);
    give(drawing(2));
    await settle();
    // The part that came after Stop isn't put on the paper; .NET hears where it stopped, and that it's as painted as it will be.
    assert.equal(paper.parts.length, 1);
    assert.equal(element.getAttribute("data-drawn"), "stopped");
    assert.deepEqual(told.at(-1), ["Progress", 2, 8, "stopped"]);

    context.rest(element);
    await settle();
    // The engraver still has the piece laid out: the rest is asked for from the second part on.
    assert.deepEqual(asked.at(-1), ["page", 2, "none"]);
    assert.equal(asked.filter(step => step[0] === "load").length, 1);
    assert.equal(element.getAttribute("data-drawn"), "part");
    for (const lines of [2, 2, 2]) {
        give(drawing(lines));
        await settle();
    }
    assert.equal(paper.parts.length, 4);
    assert.equal(element.getAttribute("data-drawn"), "all");
    assert.deepEqual(told.at(-1), ["Progress", 8, 8, "done"]);
});

test("a song is kept out of sight until all of it is drawn with its words apart", async () => {
    const { context, element, state, paper, asked, told, standIn, give } = harness({ words: true, music: "<mei><measure><syl>a</syl><syl>b</syl></measure><measure><syl>c</syl></measure></mei>" });
    standIn();
    context.drawPiece(element, state);
    await settle();
    give({ pages: 2, lines: 2 });
    await settle();
    give(drawing(1));
    await settle();
    // Its first part is there, out of sight, and the paper still waits: nothing is said of lines.
    assert.equal(paper.style.visibility, "hidden");
    assert.equal(element.getAttribute("data-drawn"), null);
    assert.equal(told.length, 0);

    // Words run together on the second: all of it again, with more room between words.
    give(drawing(1, ["E0A4"], true));
    await settle();
    const loads = asked.filter(step => step[0] === "load");
    assert.equal(loads.length, 2);
    assert.deepEqual([loads[0][2].lyricSize, loads[0][2].lyricWordSpace, loads[1][2].lyricSize, loads[1][2].lyricWordSpace], [3.8, 1.2, 3.8, 2.4]);
    assert.equal(paper.parts.length, 0);
    give({ pages: 2, lines: 2 });
    await settle();
    // What was asked of the first layout and not looked at is left; the new layout's parts come.
    give(drawing(1));
    await settle();
    give(drawing(1));
    await settle();
    give(drawing(1));
    await settle();

    assert.equal(paper.parts.length, 2);
    assert.equal(paper.style.visibility, "");
    assert.equal(element.getAttribute("data-drawn"), "all");
    assert.deepEqual(told.map(said => said.slice(0, 4)), [["Progress", 2, 2, "done"]]);
    // A sonata whose file has a few words under its notes isn't a song: its lines show as they come.
    assert.equal(context.wordy("<measure/><measure/><measure><syl>pp</syl></measure>"), false);
});

test("a sheet that goes while its piece is being laid out ends the engraver's worker, and another is started", async () => {
    const { context, element, state, told, errors, workers } = harness();
    context.drawPiece(element, state);
    await settle();
    assert.equal(workers.length, 1);
    assert.equal(workers[0].url, "_content/Prima.UI/engraver.js");
    workers[0].onmessage({ data: { ready: true } });
    await settle();
    assert.deepEqual(workers[0].posted.map(step => step.op), ["load"]);

    context.dispose(element);
    await settle();

    // Ended in the middle of the layout, with nothing said to .NET or kept as a problem; the next piece has a worker waiting.
    assert.equal(workers[0].ended, true);
    assert.equal(workers.length, 2);
    assert.equal(workers[1].ended, false);
    assert.equal(told.length, 0);
    assert.equal(errors.length, 0);
});

test("a part being drawn isn't worth ending the worker for: the sheet gone, it's simply not looked at", async () => {
    const { context, element, state, paper, workers } = harness();
    context.drawPiece(element, state);
    await settle();
    workers[0].onmessage({ data: { ready: true } });
    await settle();
    workers[0].onmessage({ data: { id: workers[0].posted[0].id, result: { pages: 5, lines: 9 } } });
    await settle();
    assert.equal(workers[0].posted.at(-1).op, "page");

    context.dispose(element);
    workers[0].onmessage({ data: { id: workers[0].posted.at(-1).id, result: drawing(2) } });
    await settle();

    assert.equal(workers.length, 1);
    assert.equal(workers[0].ended, false);
    assert.equal(paper.parts.length, 0);
});

test("where the worker can't be had, the page's own thread takes the same steps, and the problem log says so", async () => {
    const { context, element, state, paper, told, errors, workers } = harness();
    // No worker: its script can't be loaded.
    Object.assign(context, {
        ready: Promise.resolve({}),
        verovio: { toolkit: class {
            setOptions() { }
            loadData() { return true; }
            getPageCount() { return 2; }
            getMEI() { return '<system n="1"></system><system n="2"></system><system n="3"></system>'; }
            renderToSVG(n) { return drawing(n === 1 ? 2 : 1); }
            renderToTimemap() { return []; }
        } },
    });
    vm.runInContext("toolkitPromise = ready", context);
    context.drawPiece(element, state);
    await settle();
    workers[0].onerror({ message: "" });
    for (let i = 0; i < 40 && element.getAttribute("data-drawn") !== "all"; i++) await new Promise(resolve => setTimeout(resolve, 5));

    assert.equal(workers[0].ended, true);
    assert.equal(workers.length, 1);
    assert.equal(errors.length, 1);
    assert.match(errors[0], /couldn't run in a worker \(its script couldn't be loaded\).*page's own thread/);
    assert.equal(paper.parts.length, 2);
    assert.equal(element.getAttribute("data-drawn"), "all");
    assert.deepEqual(told.at(-1), ["Progress", 3, 3, "done"]);
});

test("music that can't be drawn is said, kept as a problem, and the paper stops waiting", async () => {
    const { context, element, state, told, errors, standIn, waiting } = harness();
    standIn();
    context.drawPiece(element, state);
    await settle();
    waiting.shift().reject(new Error("Verovio couldn't read the music."));
    await settle();

    assert.equal(element.getAttribute("data-drawn"), "stopped");
    assert.deepEqual(told.at(-1), ["Progress", 0, 0, "failed"]);
    assert.equal(errors.length, 1);
    assert.match(errors[0], /couldn't draw its music.*couldn't read the music/s);
});
