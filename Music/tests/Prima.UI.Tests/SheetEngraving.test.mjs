import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import test from "node:test";
import vm from "node:vm";

// The engraver's worker (engraver.js) with the real Verovio, and the rule the sheet stacks a scroll's parts by: a piece drawn a
// part at a time has to come out as the one tall page it used to be drawn as, the same bars on every line and every line where
// it was, or opening a piece without the wait would have changed its engraving.
const verovio = createRequire(import.meta.url)("../../src/Prima.UI/wwwroot/verovio/verovio-toolkit-wasm.js");
const worker = readFileSync(new URL("../../src/Prima.UI/wwwroot/engraver.js", import.meta.url), "utf8");
const sheet = readFileSync(new URL("../../src/Prima.UI/Components/Sheet.razor.js", import.meta.url), "utf8");

await new Promise(resolve => {
    if (verovio.module.calledRun) resolve();
    else verovio.module.onRuntimeInitialized = resolve;
});

// The worker as Sheet.razor.js starts it: its script run where a worker's would be, with what it posts kept, and a way to ask
// it a step and have the answer.
function startWorker() {
    const posted = [];
    const context = vm.createContext({ verovio, importScripts() { }, postMessage: message => posted.push(message) });
    vm.runInContext(worker, context);
    // Verovio is ready already here, and doesn't say so by itself a second time: the worker is told, as it would be.
    verovio.module.onRuntimeInitialized();
    let id = 0;
    const ask = (op, ...args) => {
        context.onmessage({ data: { id: ++id, op, args } });
        const answer = posted.find(m => m.id === id);
        if ("error" in answer) throw new Error(answer.error);
        return answer.result;
    };
    return { posted, ask };
}

// A piece for two hands, its bars of different widths and its notes from well under the staves to well over them, so its
// lines hold different numbers of bars and need different room above and below.
function piece(bars) {
    const names = ["c", "e", "g", "b", "d", "f", "a"];
    const notes = (bar, staff, count) => Array.from({ length: count }, (_, i) => {
        const octave = staff === 1 ? 4 + (bar * 7 + i) % 4 : 1 + (bar * 5 + i) % 3;
        return `<note xml:id="n${bar}-${staff}-${i}" pname="${names[(bar + i * 2) % 7]}" oct="${octave}" dur="${count === 4 ? 4 : count === 8 ? 8 : 16}"/>`;
    }).join("");
    const measures = Array.from({ length: bars }, (_, i) => {
        const bar = i + 1;
        const count = [4, 8, 16, 4, 8][bar % 5];
        const right = count === 4 ? notes(bar, 1, 4) : `<beam>${notes(bar, 1, count / 2)}</beam><beam>${notes(bar, 1, count / 2).replaceAll(`n${bar}-1-`, `n${bar}-1b-`)}</beam>`;
        return `<measure n="${bar}" xml:id="m${bar}"><staff n="1"><layer n="1">${right}</layer></staff><staff n="2"><layer n="1">${notes(bar, 2, 4)}</layer></staff></measure>`;
    }).join("");
    return `<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.0"><meiHead><fileDesc><titleStmt><title>A test piece</title></titleStmt><pubStmt/></fileDesc></meiHead>`
        + `<music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4">`
        + `<staffGrp symbol="brace" bar.thru="true"><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/><staffDef n="2" lines="5" clef.shape="F" clef.line="4"/></staffGrp>`
        + `</scoreDef><section>${measures}</section></score></mdiv></body></music></mei>`;
}

// The paper as the sheet asks for it: the options a scroll's parts are drawn with, and the one tall page it used to be.
const context = vm.createContext({});
vm.runInContext(sheet.replace(/^export /gm, ""), context);
const [partHeight, seam] = [vm.runInContext("partHeight", context), vm.runInContext("seam", context)];
const paper = { font: "Leipzig", scale: 100, pageWidth: 2400, adjustPageHeight: true, breaks: "auto", minLastJustification: 0.8, header: "none", footer: "none", pageMarginLeft: 50, pageMarginRight: 24,
    svgViewBox: true, svgHtml5: true, fontTextLiberation: true, lyricSize: 4.5, lyricWordSpace: 1.2 };
const [top, bottom] = [24, 24];
const parts = { ...paper, pageHeight: partHeight, pageMarginTop: 0, pageMarginBottom: 0 };
const tall = { ...paper, pageHeight: 60000, pageMarginTop: top, pageMarginBottom: bottom };

// A page's lines, from its drawing: each one's top and bottom staff lines (in the drawing's own tenths of a unit, from the
// page's top) and how many bars it holds; and the page's height.
function lines(svg) {
    const height = Number(svg.match(/<svg viewBox="0 0 \d+ (\d+)"/)[1]) * 10;
    const down = Number(svg.match(/class="page-margin" transform="translate\(\d+, (\d+)\)"/)[1]);
    const systems = svg.split(/<g [^>]*class="system"[^>]*>/).slice(1).map(system => {
        // A staff's lines run the width of their bar; a ledger line is a few note heads wide.
        const staffLines = [...system.matchAll(/<path d="M(\d+) (\d+) L(\d+) (\d+)" stroke-width="\d+" \/>/g)].filter(m => m[2] === m[4] && m[3] - m[1] > 800).map(m => Number(m[2]) + down);
        return { top: Math.min(...staffLines), bottom: Math.max(...staffLines), bars: (system.match(/ class="measure"/g) ?? []).length };
    });
    return { height, systems };
}

test("the worker lays a piece out, counts its lines, and draws each part with only the styles asked for", () => {
    const { posted, ask } = startWorker();
    assert.equal(JSON.stringify(posted), '[{"ready":true}]');

    const mei = piece(60);
    const laid = ask("load", mei, parts, null);
    assert.ok(laid.pages > 3, `a scroll of 60 bars comes in several parts, not ${laid.pages}`);

    const drawn = Array.from({ length: laid.pages }, (_, i) => ask("page", i + 1, i === 0 ? "rules" : "none"));
    assert.equal(laid.lines, drawn.reduce((sum, svg) => sum + lines(svg).systems.length, 0));
    // The first part brings the drawing's own rules, without the text faces; the others no style sheet at all.
    assert.match(drawn[0], /<style type="text\/css">#\w+ g\.ending/);
    assert.doesNotMatch(drawn[0], /@font-face/);
    for (const svg of drawn.slice(1)) assert.doesNotMatch(svg, /<style/);
    assert.match(ask("page", 1, "all"), /@font-face[^}]*"Liberation"/);
    // Every part is the same drawing's: the first part's rules, by its id, hold for them all.
    const ids = new Set(drawn.map(svg => svg.match(/<svg [^>]*id="(\w+)"/)[1]));
    assert.equal(ids.size, 1);

    assert.equal(ask("pageOf", "m1"), 1);
    assert.equal(ask("pageOf", "m60"), laid.pages);
    const map = ask("timemap");
    assert.equal(map.filter(entry => entry.measureOn).length, 60);

    // Music it can't read is an answer, not the worker's end.
    assert.throws(() => ask("load", "<not-mei/>", parts, null), /couldn't read the music/);
    // Only some of its bars: one part, its lines not counted.
    assert.equal(JSON.stringify(ask("load", mei, parts, "6-9")), '{"pages":1,"lines":0}');
});

test("what's asked before Verovio is ready is answered once it is, in order", () => {
    const posted = [];
    const waiting = { module: { calledRun: false }, toolkit: verovio.toolkit };
    const context = vm.createContext({ verovio: waiting, importScripts() { }, postMessage: message => posted.push(message) });
    vm.runInContext(worker, context);
    context.onmessage({ data: { id: 1, op: "load", args: [piece(8), parts, null] } });
    context.onmessage({ data: { id: 2, op: "pageOf", args: ["m8"] } });
    assert.equal(posted.length, 0);

    waiting.module.onRuntimeInitialized();

    assert.deepEqual(posted.map(m => m.id ?? "ready"), ["ready", 1, 2]);
    assert.equal(posted[2].result, posted[1].result.pages);
});

test("a scroll's parts, stacked as the sheet stacks them, are the one tall page it used to be drawn as", () => {
    const { ask } = startWorker();
    for (const bars of [40, 137]) {
        const mei = piece(bars);
        ask("load", mei, tall, null);
        const before = lines(ask("page", 1, "none")).systems;

        const laid = ask("load", mei, parts, null);
        const now = Array.from({ length: laid.pages }, (_, i) => lines(ask("page", i + 1, "none")));
        assert.equal(now.flatMap(part => part.systems).length, before.length);
        assert.deepEqual(now.flatMap(part => part.systems.map(system => system.bars)), before.map(system => system.bars), "the same bars on every line");

        // Each part goes under the one before with the seam between them, the first under the paper's top margin.
        let [y, line, seams] = [top * 10, 0, 0];
        for (const part of now) {
            for (const system of part.systems) {
                // A page's height is cut to a whole unit, so each seam can be out by under one (ten of these tenths), no more.
                const off = Math.abs(y + system.top - before[line].top);
                assert.ok(off <= seams * 6 + 1, `line ${line + 1} of ${bars} bars is ${off / 10} units from where it was, after ${seams} seams`);
                assert.equal(system.bottom - system.top, before[line].bottom - before[line].top);
                line++;
            }
            y += part.height + seam * 10;
            seams++;
        }
        assert.ok(seams > 2);
    }
});
