import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../../src/Prima.UI/Components/PdfPages.razor.js", import.meta.url), "utf8");
const context = vm.createContext({});
vm.runInContext(source.replace(/^import .*\r?\n/gm, "").replace(/^pdfjs\.GlobalWorkerOptions.*\r?\n/gm, "").replace(/^export /gm, ""), context);

test("proposes aligned barlines on both staves without requiring ink in the staff gap", () => {
    const rows = [20, 30, 40, 50, 60, 120, 130, 140, 150, 160];
    const lines = [10, 100, 300, 600, 604, 614, 900];
    const dark = (x, y) => rows.includes(y) || lines.includes(x) && (y >= 20 && y <= 60 || y >= 120 && y <= 160) || x === 500 && y >= 20 && y <= 60;
    const result = [...context.barlineCandidates(dark, rows, 1000, 0, 1000)];
    assert.deepEqual(result, [0.01, 0.1, 0.3, 0.602, 0.614, 0.9]);
});

test("does not invent boundaries when five-line staff groups cannot be established", () => {
    const dark = () => true;
    assert.deepEqual([...context.barlineCandidates(dark, [20, 30, 40, 50], 1000, 0, 1000)], []);
    assert.deepEqual([...context.barlineCandidates(dark, [20, 30, 40, 50, 90], 1000, 0, 1000)], []);
});

test("uses connected barlines as evidence to discard aligned but separate accidental strokes", () => {
    const rows = [20, 30, 40, 50, 60, 120, 130, 140, 150, 160];
    const dark = (x, y) => rows.includes(y) || [100, 400, 900].includes(x) && y >= 20 && y <= 160 ||
        x === 200 && (y >= 20 && y <= 60 || y >= 120 && y <= 160);
    assert.deepEqual([...context.barlineCandidates(dark, rows, 1000, 0, 1000)], [0.1, 0.4, 0.9]);
});

test("thick raster staff lines are one line each and coordinates remain page-relative after cropping", () => {
    const rows = [20, 21, 30, 31, 40, 41, 50, 51, 60, 61];
    const dark = (x, y) => rows.includes(y) || x === 300 && y >= 20 && y <= 62 || x === 600 && y >= 20 && y <= 62;
    assert.deepEqual([...context.barlineCandidates(dark, rows, 1000, 200, 800)], [0.3, 0.6]);
});

test("showing a hidden PDF preserves its existing page geometry and canvases", async () => {
    const canvas = {};
    const view = { width: 900, drawnWidth: 900, canvases: new Map([[1, canvas]]) };
    const element = { clientWidth: 0, clientHeight: 0, querySelector() { throw new Error("Hidden drawing must not touch the strip"); } };
    context.element = element;
    context.view = view;
    vm.runInContext("views.set(element, view)", context);
    await context.show(element, 0, 0, false, null, 0, 0);
    assert.equal(view.drawnWidth, 900);
    assert.equal(view.canvases.get(1), canvas);
});
