// A PDF piece's pages, drawn with pdf.js. Pages are drawn as they're needed and only a few are kept (a page drawn at the tablet's
// full resolution is tens of megabytes). The view is a window onto a strip of pages, moved by turns or, while scrolling, a system
// at a time. A half-page turn shows the next page's top over this page's bottom, the way forScore does.
import * as pdfjs from "../pdfjs/pdf.min.mjs";

pdfjs.GlobalWorkerOptions.workerSrc = new URL("../pdfjs/pdf.worker.min.mjs", import.meta.url).href;

const views = new WeakMap();

// Opens the document; the number of pages and each page's height for its width, as width ÷ height.
export async function open(element, bytes) {
    close(element);
    const doc = await pdfjs.getDocument({ data: bytes, isEvalSupported: false }).promise;
    const ratios = [];
    for (let n = 1; n <= doc.numPages; n++) {
        const page = await doc.getPage(n);
        const v = page.getViewport({ scale: 1 });
        ratios.push(v.width / v.height);
    }
    views.set(element, { doc, ratios, canvases: new Map(), y: 0 });
    return { pages: doc.numPages, ratios };
}

// A page's rows, for finding its systems: each row's share of dark pixels, drawn small (900 px wide) to be quick.
export async function profile(element, n) {
    const view = views.get(element);
    if (!view) return [];
    const page = await view.doc.getPage(n);
    const base = page.getViewport({ scale: 1 });
    const viewport = page.getViewport({ scale: 900 / base.width });
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.round(viewport.height);
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.fillStyle = "#fff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: context, viewport }).promise;
    const data = context.getImageData(0, 0, canvas.width, canvas.height).data;
    const rows = [];
    for (let y = 0; y < canvas.height; y++) {
        let dark = 0;
        for (let x = 0; x < canvas.width; x++) {
            const i = (y * canvas.width + x) * 4;
            if (data[i] + data[i + 1] + data[i + 2] < 384) dark++;
        }
        rows.push(dark / canvas.width);
    }
    return rows;
}

// A page drawn small, for showing which of its systems will be read: a URL to show it by.
export async function thumbnail(element, n, width) {
    const view = views.get(element);
    if (!view) return null;
    const canvas = await drawn(view, n, width);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
    return URL.createObjectURL(blob);
}

// A page's systems cut out as pictures for Claude to read: the page drawn up to 2,576 px wide (the largest image Claude takes
// without shrinking it), each system cropped to the ink across it. `systems` are the systems found (their tops and bottoms in px at
// 900 wide, halfway between staves); each cut moves to the widest band of white between two systems, so the notes high above or
// low below a staff stay with their own system, and a page's first and last move out to white paper. With each, a URL to show it
// by, and its barlines as shares of its width: columns dark all the way from its top staff line to its bottom one.
export async function cut(element, n, systems) {
    const view = views.get(element);
    if (!view || systems.length === 0) return [];
    const canvas = await drawn(view, n, 2576);
    const context = canvas.getContext("2d", { willReadFrequently: true });
    const width = canvas.width, height = canvas.height;
    const data = context.getImageData(0, 0, width, height).data;
    const dark = (x, y) => {
        const i = (y * width + x) * 4;
        return data[i] + data[i + 1] + data[i + 2] < 384;
    };
    const ink = new Int32Array(height);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) if (dark(x, y)) ink[y]++;
    }
    const scale = width / 900;
    // Each system's staff lines: the rows more than half dark within it.
    const lines = systems.map(s => {
        const rows = [];
        for (let y = Math.max(0, Math.floor(s.top * scale)); y < Math.min(height, Math.ceil(s.bottom * scale)); y++) if (ink[y] > width / 2) rows.push(y);
        return rows;
    });
    // Between two systems, the middle of the longest run of white rows (or the least inky row, when they touch).
    const between = (from, to) => {
        let best = null, run = null;
        for (let y = from; y <= to; y++) {
            if (ink[y] <= 2) run = run ? [run[0], y] : [y, y];
            else run = null;
            if (run && (!best || run[1] - run[0] > best[1] - best[0])) best = [...run];
        }
        if (best) return Math.round((best[0] + best[1]) / 2);
        let least = from;
        for (let y = from; y <= to; y++) if (ink[y] < ink[least]) least = y;
        return least;
    };
    // From a rough edge outward to the first white row, so no note or word is cut through.
    const outward = (y, step) => {
        while (y > 0 && y < height - 1 && ink[y] > 2) y += step;
        return y;
    };
    const tops = [], bottoms = [];
    for (let i = 0; i < systems.length; i++) {
        const own = lines[i];
        if (i === 0) tops.push(outward(Math.max(0, Math.floor(systems[0].top * scale)), -1));
        if (i === systems.length - 1) bottoms.push(outward(Math.min(height - 1, Math.ceil(systems[i].bottom * scale)), 1));
        else {
            const next = lines[i + 1];
            const edge = own.length && next.length ? between(own[own.length - 1] + 1, next[0] - 1) : Math.round(systems[i].bottom * scale);
            bottoms.push(edge);
            tops.push(edge);
        }
    }

    const cuts = [];
    for (let i = 0; i < systems.length; i++) {
        const top = tops[i], bottom = Math.max(top + 1, bottoms[i]);
        let left = width, right = 0;
        for (let y = top; y < bottom; y++) {
            if (ink[y] === 0) continue;
            for (let x = 0; x < width; x++) {
                if (!dark(x, y)) continue;
                if (x < left) left = x;
                if (x > right) right = x;
            }
        }
        const pad = Math.round(width * 0.01);
        left = Math.max(0, Math.min(left, width - 1) - pad);
        right = Math.min(width, Math.max(right, left) + pad + 1);
        const bars = [];
        const own = lines[i];
        if (own.length >= 2) {
            const first = own[0], last = own[own.length - 1];
            let run = null;
            for (let x = left; x < right; x++) {
                let through = true;
                for (let y = first; y <= last && through; y++) through = dark(x, y) || dark(x, Math.min(height - 1, y + 1));
                if (through) run = run ? [run[0], x] : [x, x];
                else if (run) {
                    bars.push(((run[0] + run[1]) / 2 - left) / (right - left));
                    run = null;
                }
            }
            if (run) bars.push(((run[0] + run[1]) / 2 - left) / (right - left));
        }
        const out = document.createElement("canvas");
        out.width = Math.max(1, right - left);
        out.height = bottom - top;
        out.getContext("2d").drawImage(canvas, left, top, out.width, out.height, 0, 0, out.width, out.height);
        const blob = await new Promise(resolve => out.toBlob(resolve, "image/png"));
        cuts.push({ png: new Uint8Array(await blob.arrayBuffer()), url: URL.createObjectURL(blob), bars });
    }
    return cuts;
}

// Lets go of pictures made for showing (thumbnail, cut, the sheet's engrave).
export function release(urls) {
    for (const url of urls ?? []) URL.revokeObjectURL(url);
}

// A page drawn on white, `width` px wide, on a canvas of its own.
async function drawn(view, n, width) {
    const page = await view.doc.getPage(n);
    const base = page.getViewport({ scale: 1 });
    const viewport = page.getViewport({ scale: width / base.width });
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.round(viewport.height);
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.fillStyle = "#fff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: context, viewport }).promise;
    return canvas;
}

// The width pages are drawn at: the view's, or with `fit` (portrait, a page at a time) small enough for a whole page to show.
export function pageWidth(element, fit) {
    const view = views.get(element);
    if (!view) return element.clientWidth;
    return fit ? Math.min(element.clientWidth, element.clientHeight * Math.min(...view.ratios)) : element.clientWidth;
}

// Shows the strip from `y` (CSS px from the top of page 1, at the drawn width), drawing the pages that are in sight. With `half`
// (a page number), the page before is covered down to `split` px, and that page's top shows there down to `shown` px.
export async function show(element, y, ms, fit, half, split, shown) {
    const view = views.get(element);
    if (!view) return;
    const strip = element.querySelector(".pdf-strip");
    const width = pageWidth(element, fit);
    strip.style.width = `${width}px`;
    const heights = view.ratios.map(r => width / r);
    view.width = width;
    // The strip holds one placeholder per page, the page's size; drawn pages fill theirs.
    if (strip.childElementCount !== heights.length || view.drawnWidth !== width) {
        strip.replaceChildren(...heights.map((h, i) => {
            const slot = document.createElement("div");
            slot.className = "pdf-page";
            slot.style.height = `${h}px`;
            slot.dataset.page = i + 1;
            return slot;
        }));
        view.canvases.clear();
        view.drawnWidth = width;
    }
    const tops = heights.reduce((acc, h, i) => [...acc, (acc[i] ?? 0) + (i ? heights[i - 1] : 0)], []).slice(0, heights.length);
    const viewHeight = element.clientHeight;
    const wanted = new Set();
    for (let i = 0; i < heights.length; i++) {
        if (tops[i] + heights[i] > y - viewHeight && tops[i] < y + 2 * viewHeight) wanted.add(i + 1);
    }
    if (half) wanted.add(half);
    for (const n of wanted) await draw(view, strip, n, width);
    // Only the pages in sight stay drawn.
    for (const [n, canvas] of view.canvases) {
        if (!wanted.has(n)) {
            canvas.remove();
            view.canvases.delete(n);
        }
    }

    element.querySelector(".pdf-half")?.remove();
    if (half && view.canvases.has(half)) {
        // The next page's top, laid over this page's bottom up to the split.
        const top = document.createElement("div");
        top.className = "pdf-half";
        top.style.height = `${split}px`;
        top.style.width = `${width}px`;
        top.appendChild(view.canvases.get(half).cloneNode(false));
        const source = view.canvases.get(half);
        const copy = top.firstChild;
        copy.getContext("2d").drawImage(source, 0, 0);
        // Below the next page's last whole system, blank paper down to the line.
        if (shown < split) {
            const blank = document.createElement("div");
            blank.className = "pdf-half-blank";
            blank.style.top = `${shown}px`;
            top.appendChild(blank);
        }
        element.appendChild(top);
    }

    strip.style.transition = ms ? `transform ${ms}ms ease-in-out` : "none";
    strip.style.transform = `translateY(${-y}px)`;
    view.y = y;
}

async function draw(view, strip, n, width) {
    if (view.canvases.has(n)) return;
    const page = await view.doc.getPage(n);
    const base = page.getViewport({ scale: 1 });
    const dpr = Math.min(window.devicePixelRatio || 1, 2.5);
    const viewport = page.getViewport({ scale: width / base.width * dpr });
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(viewport.width);
    canvas.height = Math.round(viewport.height);
    canvas.style.width = `${width}px`;
    canvas.style.height = `${viewport.height / dpr}px`;
    view.canvases.set(n, canvas);
    const context = canvas.getContext("2d");
    context.fillStyle = "#fff";
    context.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: context, viewport }).promise;
    strip.querySelector(`[data-page="${n}"]`)?.appendChild(canvas);
}

// The system being played, while scrolling: a pale band across it.
export function band(element, top, bottom) {
    const view = views.get(element);
    element.querySelector(".pdf-band")?.remove();
    if (!view || top == null) return;
    const band = document.createElement("div");
    band.className = "pdf-band";
    band.style.top = `${top - view.y}px`;
    band.style.height = `${bottom - top}px`;
    element.appendChild(band);
}

// pdf.js 6 lets a document go through the task that loaded it; a failure there mustn't stop the page closing.
export function close(element) {
    const view = views.get(element);
    if (!view) return;
    views.delete(element);
    Promise.resolve(view.doc.loadingTask?.destroy()).catch(() => {});
}
