import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";

const sheet = readFileSync(new URL("../../src/Prima.UI/Components/Sheet.razor.js", import.meta.url), "utf8");
const pdf = readFileSync(new URL("../../src/Prima.UI/Components/PdfPages.razor.js", import.meta.url), "utf8")
    .replace(/^import .*\r?\n/gm, "").replace(/^pdfjs\.GlobalWorkerOptions.*\r?\n/gm, "");

function context(source, globals = {}) {
    const result = vm.createContext(globals);
    vm.runInContext(source.replace(/^export /gm, ""), result);
    return result;
}

test("a disposed or detached sheet cannot resume rendering after the toolkit loads", async () => {
    const ready = Promise.withResolvers();
    const ctx = context(sheet, { ready: ready.promise });
    vm.runInContext("toolkitPromise = ready", ctx);
    const disposed = { isConnected: true, querySelector() { throw new Error("Disposed sheet was queried"); } };
    const detached = { isConnected: true, querySelector() { throw new Error("Detached sheet was queried"); } };
    const first = ctx.render(disposed, "mei", {}, null, "disposed-sheet");
    const second = ctx.render(detached, "mei", {}, null);
    ctx.dispose(null, "disposed-sheet");
    detached.isConnected = false;
    ready.resolve({});
    await Promise.all([first, second]);
    await ctx.render(null, "mei", {}, null);
});

test("queued sheet paint callbacks lose their disposed owner", () => {
    const frames = [];
    let painted = 0;
    const ctx = context(sheet, { requestAnimationFrame: fn => frames.push(fn), cancelAnimationFrame() {}, clearTimeout() {} });
    const element = { isConnected: true };
    const state = { overlay: { replaceChildren() {} }, line: {}, dotnet: { invokeMethodAsync() { painted++; } }, pages: 1 };
    Object.assign(ctx, { element, state });
    vm.runInContext("sheets.set(element, state); renderOwners.set('paint-owner', element); finish(element, state)", ctx);
    ctx.dispose(null, "paint-owner");
    while (frames.length) frames.shift()();
    assert.equal(painted, 0);
    assert.equal(state.dotnet, null);
});

test("a late PDF open cannot resurrect a closed document or replace a newer document", async () => {
    const first = Promise.withResolvers();
    let destroyed = 0;
    const doc = { numPages: 1, async getPage() { return { getViewport: () => ({ width: 3, height: 4 }) }; } };
    const tasks = [{ promise: first.promise, async destroy() { destroyed++; } }, { promise: Promise.resolve(doc), async destroy() {} }];
    const ctx = context(pdf, { pdfjs: { getDocument: () => tasks.shift() }, URL: { revokeObjectURL() {} } });
    const element = { isConnected: true };
    const old = ctx.open(element, [1]);
    const fresh = await ctx.open(element, [2]);
    first.resolve(doc);
    assert.equal(await old, null);
    assert.equal(fresh.pages, 1);
    assert.equal(destroyed, 1);
    ctx.element = element;
    assert.equal(vm.runInContext("views.get(element).doc", ctx), doc);
});

test("PDF close revokes owned URLs and late previews cannot allocate another URL", () => {
    const revoked = [];
    let created = 0;
    const ctx = context(pdf, { URL: { createObjectURL: () => "blob:" + ++created, revokeObjectURL: url => revoked.push(url) } });
    const element = {};
    const view = { element, ownerId: "preview-owner", urls: new Set(), closed: false, task: { async destroy() {} } };
    Object.assign(ctx, { element, view });
    vm.runInContext("views.set(element, view); documents.set('preview-owner', view)", ctx);
    assert.equal(ctx.imageUrl(view, {}), "blob:1");
    assert.equal(ctx.imageUrl(view, {}), "blob:2");
    ctx.close(null, "preview-owner");
    assert.deepEqual(revoked, ["blob:1", "blob:2"]);
    assert.equal(ctx.imageUrl(view, {}), null);
    assert.equal(created, 2);
});

test("thumbnail completion after close discards its blob without creating a URL", async () => {
    const requested = Promise.withResolvers();
    let finishBlob;
    let created = 0;
    const canvas = {
        getContext: () => ({ fillRect() {} }),
        toBlob(callback) { finishBlob = callback; requested.resolve(); },
    };
    const doc = { async getPage() { return { getViewport: () => ({ width: 3, height: 4 }), render: () => ({ promise: Promise.resolve() }) }; } };
    const ctx = context(pdf, { document: { createElement: () => canvas }, URL: { createObjectURL() { created++; }, revokeObjectURL() {} } });
    const element = {};
    const view = { doc, urls: new Set(), closed: false, task: { async destroy() {} } };
    Object.assign(ctx, { element, view });
    vm.runInContext("views.set(element, view)", ctx);
    const thumbnail = ctx.thumbnail(element, 1, 400);
    await requested.promise;
    ctx.close(element);
    finishBlob({});
    assert.equal(await thumbnail, null);
    assert.equal(created, 0);
});

test("active PDF and toolkit failures still propagate", async () => {
    const error = new Error("Active document failed");
    const pdfContext = context(pdf, { pdfjs: { getDocument: () => ({ promise: Promise.reject(error), async destroy() {} }) } });
    await assert.rejects(pdfContext.open({ isConnected: true }, [1]), error);
    const sheetContext = context(sheet, { ready: Promise.reject(error) });
    vm.runInContext("toolkitPromise = ready", sheetContext);
    await assert.rejects(sheetContext.render({ isConnected: true }, "mei", {}, null), error);
});

test("visible PDF drawing suppresses only a render cancelled by its closed document", async () => {
    for (const closed of [true, false]) {
        const rendering = Promise.withResolvers();
        const started = Promise.withResolvers();
        let appended = 0;
        const strip = { style: {}, childElementCount: 1, querySelector: () => ({ appendChild() { appended++; } }) };
        const element = { isConnected: true, clientWidth: 300, clientHeight: 400, querySelector: selector => selector === ".pdf-strip" ? strip : null };
        const canvas = { style: {}, getContext: () => ({ fillRect() {} }) };
        const page = { getViewport: () => ({ width: 300, height: 400 }), render() { started.resolve(); return { promise: rendering.promise }; } };
        const view = { doc: { async getPage() { return page; } }, task: { async destroy() {} }, ratios: [0.75], drawnWidth: 300, canvases: new Map(), urls: new Set(), closed: false };
        const ctx = context(pdf, { window: { devicePixelRatio: 1 }, document: { createElement: () => canvas }, URL: { revokeObjectURL() {} }, element, view });
        vm.runInContext("views.set(element, view)", ctx);
        const drawing = ctx.show(element, 0, 0, false, null, 0, 0);
        await started.promise;
        if (closed) ctx.close(element);
        const error = new Error("Render interrupted");
        rendering.reject(error);
        if (closed) await drawing;
        else await assert.rejects(drawing, error);
        assert.equal(appended, 0);
    }
});
