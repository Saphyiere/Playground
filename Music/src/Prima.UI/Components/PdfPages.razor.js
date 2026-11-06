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
