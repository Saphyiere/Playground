import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

// Library.razor.js: where each of the Library's columns was left, put back when the Library is drawn again.
const source = readFileSync(new URL("../../src/Prima.UI/Pages/Library.razor.js", import.meta.url), "utf8");

// A page with the Library's columns, an address and frames run on demand.
function page() {
    const frames = [];
    const columns = [];
    const globals = {
        document: { querySelectorAll: s => (s === ".lib [data-keep]" ? columns : []) },
        location: { pathname: "/library", search: "" },
        requestAnimationFrame: f => frames.push(f),
    };
    const ctx = vm.createContext(globals);
    vm.runInContext(source.replace(/^export /gm, ""), ctx);
    return {
        ctx,
        // A column as the screen draws it: what it keeps, and of what (the address, when it doesn't say).
        column(keep, at) {
            const listeners = [];
            const column = { scrollTop: 0, dataset: at === undefined ? { keep } : { keep, at }, addEventListener: (kind, f) => kind === "scroll" && listeners.push(f) };
            column.scroll = to => {
                column.scrollTop = to;
                for (const f of listeners) f();
                while (frames.length) frames.shift()();
            };
            columns.push(column);
            return column;
        },
        go(address) {
            const [path, query] = address.split("?");
            Object.assign(globals.location, { pathname: path, search: query ? `?${query}` : "" });
        },
    };
}

test("a list comes back scrolled where it was left, and one not seen before starts at the top", () => {
    const p = page();
    const pieces = p.column("pieces");
    p.go("/library?by=level&of=3&only=pdf");
    p.ctx.show();
    pieces.scroll(2380);

    // A piece opened: the address changes before the list goes, and the piece's screen is short.
    p.go("/library/mutopia-beethoven-op13");
    pieces.scroll(0);
    p.ctx.leave();
    pieces.scroll(0);

    // The way back: the list is drawn short first, then whole, and put back.
    p.go("/library?by=level&of=3&only=pdf");
    pieces.scroll(0);
    p.ctx.show();
    assert.equal(pieces.scrollTop, 2380);

    // Another list, not seen before, starts at the top; the first keeps its place.
    p.go("/library?by=level&of=3");
    pieces.scroll(900);
    p.ctx.show();
    assert.equal(pieces.scrollTop, 0);
    p.go("/library?by=level&of=3&only=pdf");
    p.ctx.show();
    assert.equal(pieces.scrollTop, 2380);
});

test("a new search starts at the top of its results, and the list without it comes back where it was", () => {
    const p = page();
    const pieces = p.column("pieces");
    p.go("/library?by=near");
    p.ctx.show();
    pieces.scroll(1200);
    p.go("/library?by=near&q=moonlight");
    pieces.scroll(0);
    p.ctx.show();
    assert.equal(pieces.scrollTop, 0);
    pieces.scroll(150);
    p.go("/library?by=near");
    p.ctx.show();
    assert.equal(pieces.scrollTop, 1200);
    p.go("/library?by=near&q=moonlight");
    p.ctx.show();
    assert.equal(pieces.scrollTop, 150);
});

test("the composers and a composer's works stay where they are while another work is chosen", () => {
    const p = page();
    const list = p.column("list", "composers");
    const works = p.column("works", "Ludwig van Beethoven Sonatas");
    const work = p.column("work");
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&work=w%3Asonata%20no.%208");
    p.ctx.show();
    list.scroll(640);
    works.scroll(410);
    work.scroll(90);

    // Another work chosen: the address changes, its movements start at the top, and the two lists beside them haven't moved.
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&work=w%3Asonata%20no.%2014");
    p.ctx.show();
    assert.deepEqual([list.scrollTop, works.scrollTop, work.scrollTop], [640, 410, 0]);

    // Back to the first: its movements are where they were left.
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&work=w%3Asonata%20no.%208");
    p.ctx.show();
    assert.deepEqual([list.scrollTop, works.scrollTop, work.scrollTop], [640, 410, 90]);
});
