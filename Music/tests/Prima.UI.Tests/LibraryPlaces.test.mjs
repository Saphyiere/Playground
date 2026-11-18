import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

// Library.razor.js: where each of the Library's lists was left, by its address, put back when the list is drawn again.
const source = readFileSync(new URL("../../src/Prima.UI/Pages/Library.razor.js", import.meta.url), "utf8");

// A page with Prima's scroller, an address and frames run on demand.
function page() {
    const listeners = [];
    const frames = [];
    const main = { scrollTop: 0, addEventListener: (kind, f) => kind === "scroll" && listeners.push(f) };
    const globals = {
        document: { querySelector: s => (s === ".shell-main" ? main : null) },
        location: { pathname: "/library", search: "" },
        requestAnimationFrame: f => frames.push(f),
    };
    const ctx = vm.createContext(globals);
    vm.runInContext(source.replace(/^export /gm, ""), ctx);
    return {
        ctx,
        main,
        go(address) {
            const [path, query] = address.split("?");
            Object.assign(globals.location, { pathname: path, search: query ? `?${query}` : "" });
        },
        // The page scrolled (by a finger, or by the browser as the screen's content changes), and the next frame drawn.
        scroll(to) {
            main.scrollTop = to;
            for (const f of listeners) f();
            while (frames.length) frames.shift()();
        },
    };
}

test("a list comes back scrolled where it was left, and one not seen before starts at the top", () => {
    const p = page();
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&only=pdf");
    p.ctx.show();
    p.scroll(2380);

    // A piece opened: the address changes before the list goes, and the piece's screen is short.
    p.go("/library/mutopia-beethoven-op13");
    p.scroll(0);
    p.ctx.leave();
    p.scroll(0);

    // The way back: the list is drawn short first, then whole, and put back.
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&only=pdf");
    p.scroll(0);
    p.ctx.show();
    assert.equal(p.main.scrollTop, 2380);

    // Another list, not seen before, starts at the top; the first keeps its place.
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven");
    p.scroll(900);
    p.ctx.show();
    assert.equal(p.main.scrollTop, 0);
    p.go("/library?by=composer&of=Ludwig%20van%20Beethoven&only=pdf");
    p.ctx.show();
    assert.equal(p.main.scrollTop, 2380);
});

test("a new search starts at the top of its results, and the list without it comes back where it was", () => {
    const p = page();
    p.go("/library");
    p.ctx.show();
    p.scroll(1200);
    p.go("/library?q=moonlight");
    p.scroll(0);
    p.ctx.show();
    assert.equal(p.main.scrollTop, 0);
    p.scroll(150);
    p.go("/library");
    p.ctx.show();
    assert.equal(p.main.scrollTop, 1200);
    p.go("/library?q=moonlight");
    p.ctx.show();
    assert.equal(p.main.scrollTop, 150);
});
