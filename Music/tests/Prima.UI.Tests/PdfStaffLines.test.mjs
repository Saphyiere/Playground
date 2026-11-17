import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const source = readFileSync(new URL("../../src/Prima.UI/Components/PdfPages.razor.js", import.meta.url), "utf8");
const context = vm.createContext({});
vm.runInContext(source.replace(/^import .*\r?\n/gm, "").replace(/^pdfjs\.GlobalWorkerOptions.*\r?\n/gm, "").replace(/^export /gm, ""), context);

// A page 1000 px wide and 300 rows tall with one grand staff: lines every 10 rows from 100 and from 200, from x 100 to 899.
const staffLines = [100, 110, 120, 130, 140, 200, 210, 220, 230, 240];
const onStaff = (x, y) => staffLines.includes(y) && x >= 100 && x < 900;
// Beams all the way along a row, each 60 px long with 20 px between them: 600 px of ink, more than half the page.
const beam = x => x >= 100 && x < 900 && (x - 100) % 80 < 60;

test("a row of beams is more than half ink without one stroke crossing half the page", () => {
    const { total, stroke } = context.rowInk((x, y) => onStaff(x, y) || y === 99 && beam(x), 1000, 300, 2);
    assert.equal(total[100], 800);
    assert.equal(stroke[100], 800);
    assert.equal(total[99], 600);
    assert.equal(stroke[99], 60);
});

test("a scan's pinholes don't break a staff line, and a wider break does", () => {
    // A hole every ninth pixel, and five pixels missing at 500.
    const dark = (x, y) => y === 5 && x >= 100 && x < 900 && x % 9 !== 0 && (x < 500 || x >= 505);
    assert.equal(context.rowInk(dark, 1000, 10, 2).stroke[5], 400);
    assert.equal(context.rowInk(dark, 1000, 10, 0).stroke[5], 8);
});

test("beams lying along a staff's lines don't count as lines, and ink far from any staff counts as it did", () => {
    // Beams just above the top line and between the second and third lines; a row of ties well below the staves.
    const dark = (x, y) => onStaff(x, y) || [97, 98, 99, 114, 115, 116, 280].includes(y) && beam(x);
    const { total, stroke } = context.rowInk(dark, 1000, 300, 2);
    const shares = context.lineShares(total, stroke, 1000);
    for (const y of staffLines) assert.equal(shares[y], 0.8);
    for (const y of [97, 98, 99, 114, 115, 116]) assert.equal(shares[y], 0.06);
    assert.equal(shares[280], 0.6);
    assert.deepEqual([...shares.flatMap((share, y) => share > 0.5 ? [y] : [])], [...staffLines, 280]);
    assert.deepEqual([...context.staffRows(total, stroke, 80, 260, 1000)], staffLines);
});

test("a scan whose staff lines are too broken to follow is still found by its ink", () => {
    // Every line broken each 100 px by a gap of 10: strokes 90 px long, 720 px of ink.
    const { total, stroke } = context.rowInk((x, y) => onStaff(x, y) && (x - 100) % 100 < 90, 1000, 300, 2);
    assert.equal(stroke[100], 90);
    const shares = context.lineShares(total, stroke, 1000);
    for (const y of staffLines) assert.equal(shares[y], 0.72);
    assert.deepEqual([...context.staffRows(total, stroke, 80, 260, 1000)], staffLines);
});

test("where one staff's lines are whole and the other's are broken, the system keeps both", () => {
    const dark = (x, y) => onStaff(x, y) && (y < 200 || (x - 100) % 100 < 90);
    const { total, stroke } = context.rowInk(dark, 1000, 300, 2);
    const shares = context.lineShares(total, stroke, 1000);
    assert.deepEqual([...shares.flatMap((share, y) => share > 0.5 ? [y] : [])], staffLines);
    assert.deepEqual([...context.staffRows(total, stroke, 80, 260, 1000)], staffLines);
});

test("a line that fits no staff is passed over when proposing barlines", () => {
    // A pedal bracket under the staves, at row 185.
    const rows = [20, 30, 40, 50, 60, 120, 130, 140, 150, 160, 185];
    const dark = (x, y) => rows.includes(y) && x >= 100 && x <= 900 || [100, 400, 900].includes(x) && y >= 20 && y <= 160;
    assert.deepEqual([...context.barlineCandidates(dark, rows, 1000, 90, 910).bars], [0.1, 0.4, 0.9]);
});

test("rows that make no sense as staves propose nothing", () => {
    const rows = [20, 30, 40, 50, 60, 75, 83, 97, 120, 130, 140, 150, 160];
    const dark = (x, y) => rows.includes(y) && x >= 100 && x <= 900 || [100, 400, 900].includes(x) && y >= 20 && y <= 160;
    const detected = context.barlineCandidates(dark, rows, 1000, 90, 910);
    assert.deepEqual([...detected.bars], []);
    assert.equal(detected.staffStart, null);
    assert.equal(detected.staffEnd, null);
});
