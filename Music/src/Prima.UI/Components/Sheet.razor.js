// The sheet: Verovio engraves the MEI as real printed music, black on paper, and an overlay on top draws the cursor while playing
// and the marks afterwards, found by each note's id. The engraving itself is never changed. Sizes: a printed piano staff is about
// 7 mm tall, and the 13-inch tablet's long side is about 280 mm, so a millimetre is the screen's long side in CSS pixels / 280.

const svgNs = "http://www.w3.org/2000/svg";
const sheets = new WeakMap();
let toolkitPromise;

// Verovio's toolkit, loaded once: a 7 MB script with WebAssembly inside. Called at start so the first sheet doesn't wait.
export function warm() {
    toolkit();
}

function toolkit() {
    toolkitPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "_content/Prima.UI/verovio/verovio-toolkit-wasm.js";
        script.onload = () => {
            const ready = () => resolve(new verovio.toolkit());
            if (verovio.module.calledRun) ready();
            else verovio.module.onRuntimeInitialized = ready;
        };
        script.onerror = reject;
        document.head.appendChild(script);
    });
    return toolkitPromise;
}

// Imported and bundled music in, MEI out: a toolkit of its own, so a conversion never disturbs a sheet on screen. `kind` is the
// file's: "mxl" (zipped MusicXML), "musicxml", "xml", "mei" or "abc". Ids are made from the content, so they're the same every time.
let converter;
export async function toMei(bytes, kind) {
    await toolkit();
    converter ??= new verovio.toolkit();
    converter.setOptions({ xmlIdChecksum: true });
    const loaded = kind === "mxl"
        ? converter.loadZipDataBuffer(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength))
        : converter.loadData(new TextDecoder().decode(bytes));
    return loaded ? converter.getMEI({ removeIds: false }) : null;
}

export function pixelsPerMm() {
    return Math.max(screen.width, screen.height) / 280;
}

// Engraves `mei` into the sheet: options are layout ("line": systems as wide as the sheet; "page": one page the sheet's size),
// staffMm, font, page and header. Redraws by itself when the sheet's width changes, and tells .NET when it has painted.
export async function render(element, mei, options, dotnet) {
    const tk = await toolkit();
    const state = sheets.get(element) ?? {};
    Object.assign(state, { mei, options, dotnet, tk });
    sheets.set(element, state);
    draw(element, state);
    if (!state.observer) {
        state.observer = new ResizeObserver(() => {
            if (Math.abs(element.clientWidth - state.width) > 1 || (state.options.layout === "page" && Math.abs(element.clientHeight - state.height) > 1)) draw(element, state);
        });
        state.observer.observe(element);
        element.addEventListener("click", event => {
            const note = event.target.closest?.(".sheet-music .note[data-id]");
            if (note) state.dotnet?.invokeMethodAsync("NoteTapped", note.dataset.id);
        });
    }
}

function draw(element, state) {
    const { tk, options } = state;
    const music = element.querySelector(".sheet-music");
    const staffPx = options.staffMm * pixelsPerMm();
    // A staff is 72 of Verovio's units at scale 100, so the page is the sheet's width in units at this staff size.
    const unitsPerPx = 72 / staffPx;
    state.width = element.clientWidth;
    state.height = element.clientHeight;
    state.loaded = {
        font: options.font,
        scale: 100,
        pageWidth: Math.max(600, Math.round(state.width * unitsPerPx)),
        pageHeight: options.layout === "page" ? Math.max(600, Math.round(state.height * unitsPerPx)) : 60000,
        adjustPageHeight: options.layout !== "page",
        breaks: "auto",
        minLastJustification: options.layout === "line" ? 0 : 0.8,
        header: options.header ? "auto" : "none",
        footer: "none",
        pageMarginLeft: 24,
        pageMarginRight: 24,
        pageMarginTop: options.layout === "page" ? 40 : 24,
        pageMarginBottom: 24,
        svgViewBox: true,
        svgHtml5: true,
    };
    tk.setOptions(state.loaded);
    load(tk, state, state.mei);
    state.pages = tk.getPageCount();
    music.innerHTML = tk.renderToSVG(Math.min(options.page ?? 1, state.pages));
    // A passage's bars are shared evenly between its systems, so the last one isn't left with a bar on its own.
    const systems = music.querySelectorAll(".system");
    const bars = music.querySelectorAll(".measure").length;
    if (options.layout === "line" && systems.length > 1 && systems[systems.length - 1].querySelectorAll(".measure").length < Math.ceil(bars / systems.length) - 1) {
        tk.setOptions({ breaks: "encoded" });
        load(tk, state, withSystemBreaks(state.mei, Math.ceil(bars / systems.length), firstMeasure(options)));
        music.innerHTML = tk.renderToSVG(1);
    }
    state.timemap = tk.renderToTimemap({ includeMeasures: true });
    const overlay = element.querySelector(".sheet-overlay");
    overlay.replaceChildren();
    state.cursor = null;
    if (state.marks) showMarks(element, state.marks);
    requestAnimationFrame(() => requestAnimationFrame(() => state.dotnet?.invokeMethodAsync("Painted", state.pages)));
}

// Loads music into the toolkit, and when the sheet shows only some bars of it ("6-9", by position from 1), selects them: the key,
// clefs and metre in force there are drawn at their start.
function load(tk, state, mei) {
    tk.loadData(mei);
    if (state.options.measures) {
        tk.select({ measureRange: state.options.measures });
        tk.redoLayout();
    }
    tk.current = state;
}

function firstMeasure(options) {
    return options.measures ? parseInt(options.measures, 10) - 1 : 0;
}

// The MEI with a system break before every `perSystem`th bar, counting from the first shown.
function withSystemBreaks(mei, perSystem, from = 0) {
    const ns = "http://www.music-encoding.org/ns/mei";
    const doc = new DOMParser().parseFromString(mei, "application/xml");
    const measures = [...doc.getElementsByTagNameNS(ns, "measure")];
    for (let i = from + perSystem; i < measures.length; i += perSystem) measures[i].parentNode.insertBefore(doc.createElementNS(ns, "sb"), measures[i]);
    return new XMLSerializer().serializeToString(doc);
}

// The toolkit is shared by every sheet: before asking it about this sheet's music, make sure it's this sheet's that's loaded.
function ensureLoaded(state) {
    if (state.tk.current === state) return;
    state.tk.setOptions(state.loaded);
    load(state.tk, state, state.mei);
}

// The page a note is on (1 for the first), or 0 if it isn't in the music.
export function pageOf(element, id) {
    const state = sheets.get(element);
    if (!state) return 0;
    ensureLoaded(state);
    return state.tk.getPageWithElement(id);
}

// Which of these notes are on the page showing.
export function onPage(element, ids) {
    return ids.filter(id => element.querySelector(`.sheet-music [data-id="${id}"]`));
}

// The bar a note is in, as a pale box behind it, as on a part a teacher has marked; none when the note isn't on this page.
export function highlight(element, id) {
    const state = sheets.get(element);
    if (!state) return;
    element.querySelector(".sheet-bar")?.remove();
    const note = id ? element.querySelector(`.sheet-music [data-id="${id}"]`) : null;
    const measure = note?.closest(".measure");
    if (!measure) return;
    const box = element.getBoundingClientRect();
    const staves = [...measure.querySelectorAll(".staff")].map(s => s.getBoundingClientRect());
    const r = measure.getBoundingClientRect();
    const top = Math.min(...staves.map(s => s.top)) - box.top - 10;
    const bottom = Math.max(...staves.map(s => s.bottom)) - box.top + 10;
    const rect = document.createElementNS(svgNs, "rect");
    rect.setAttribute("class", "sheet-bar");
    rect.setAttribute("x", r.left - box.left - 2);
    rect.setAttribute("y", top);
    rect.setAttribute("width", r.width + 4);
    rect.setAttribute("height", bottom - top);
    element.querySelector(".sheet-overlay").prepend(rect);
}

// Where each moment of the music is on the sheet: for every onset in Verovio's time map that's on this page, its time in quarter
// notes, the left edge of its first note head and its system's top and bottom, in px from the sheet's corner.
function positions(element, state) {
    const box = element.getBoundingClientRect();
    const points = [];
    // Onsets given by .NET (a stretch of a real piece, where Verovio's time map is the whole piece's) take its place.
    if (state.options.onsets) {
        for (const [id, q] of state.options.onsets) {
            const head = element.querySelector(`.sheet-music [data-id="${id}"] .notehead`) ?? element.querySelector(`.sheet-music [data-id="${id}"]`);
            if (!head) continue;
            const system = head.closest(".system");
            const h = head.getBoundingClientRect();
            const s = system.getBoundingClientRect();
            points.push({ q, x: h.left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6, system });
        }
        return points;
    }
    for (const entry of state.timemap) {
        const id = (entry.on ?? []).find(i => element.querySelector(`.sheet-music [data-id="${i}"] .notehead, .sheet-music .rest[data-id="${i}"]`));
        if (!id) continue;
        const head = element.querySelector(`.sheet-music [data-id="${id}"] .notehead`) ?? element.querySelector(`.sheet-music [data-id="${id}"]`);
        const system = head.closest(".system");
        const h = head.getBoundingClientRect();
        const s = system.getBoundingClientRect();
        points.push({ q: entry.qstamp, x: h.left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6, system });
    }
    return points;
}

// Moves a cursor in the look's accent with the music: bar 1 starts `msUntilStart` from now, at `msPerQuarter`, from quarter
// `startQuarter` of the music (later than 0 after a pickup). A guide only: nothing is scored from it.
export function cursorStart(element, msUntilStart, msPerQuarter, startQuarter, windowQuarters) {
    const state = sheets.get(element);
    if (!state) return;
    cursorStop(element);
    const points = positions(element, state);
    if (points.length === 0) return;
    const bars = windowQuarters ? measures(element, state) : null;
    const overlay = element.querySelector(".sheet-overlay");
    const line = document.createElementNS(svgNs, "rect");
    line.setAttribute("class", "sheet-cursor");
    line.setAttribute("width", "3");
    overlay.appendChild(line);
    state.cursor = line;
    const start = performance.now() + msUntilStart;
    const frame = now => {
        const q = startQuarter + (now - start) / msPerQuarter;
        let i = 0;
        while (i < points.length - 1 && points[i + 1].q <= q) i++;
        const here = points[i];
        const next = points[i + 1];
        const x = next && next.system === here.system && q > here.q ? here.x + (next.x - here.x) * (q - here.q) / (next.q - here.q) : here.x;
        line.setAttribute("x", x);
        line.setAttribute("y", here.top);
        line.setAttribute("height", here.bottom - here.top);
        if (bars) for (const bar of bars) bar.cover.style.display = bar.end <= q - 0.05 || bar.start > q + windowQuarters ? "" : "none";
        state.raf = requestAnimationFrame(frame);
    };
    state.raf = requestAnimationFrame(frame);
}

export function cursorStop(element) {
    const state = sheets.get(element);
    if (!state) return;
    cancelAnimationFrame(state.raf);
    state.cursor?.remove();
    state.cursor = null;
    element.querySelectorAll(".sheet-cover").forEach(c => c.remove());
}

// Read ahead: each bar with its span in quarters and a cover the colour of the paper, drawn over it when it's hidden.
function measures(element, state) {
    const box = element.getBoundingClientRect();
    const overlay = element.querySelector(".sheet-overlay");
    const starts = state.timemap.filter(e => e.measureOn).map(e => ({ id: e.measureOn, start: e.qstamp }));
    const last = Math.max(...state.timemap.map(e => e.qstamp));
    return starts.map((m, i) => {
        const g = element.querySelector(`.sheet-music [data-id="${m.id}"]`);
        if (!g) return null;
        const r = g.getBoundingClientRect();
        const cover = document.createElementNS(svgNs, "rect");
        cover.setAttribute("class", "sheet-cover");
        cover.setAttribute("x", r.left - box.left);
        cover.setAttribute("y", r.top - box.top - 8);
        cover.setAttribute("width", r.width + 1);
        cover.setAttribute("height", r.height + 16);
        cover.style.display = "none";
        overlay.appendChild(cover);
        return { start: m.start, end: starts[i + 1]?.start ?? last + 4, cover };
    }).filter(Boolean);
}

// Read ahead's look: only the opening view shows, so the passage can't be learned before it's hidden.
export function showOpening(element, windowQuarters) {
    const state = sheets.get(element);
    if (!state) return;
    element.querySelectorAll(".sheet-cover").forEach(c => c.remove());
    for (const bar of measures(element, state)) bar.cover.style.display = bar.start > windowQuarters ? "" : "none";
}

// Marks notes after the passage. Each mark: id, kind ("wrong", "missed", "early", "late"), and for a wrong note what was played
// ("G4") and how many staff positions away it is, so a small grey note head shows it on the staff.
export function showMarks(element, marks) {
    const state = sheets.get(element);
    if (!state) return;
    state.marks = marks;
    clearDrawnMarks(element);
    const box = element.getBoundingClientRect();
    const overlay = element.querySelector(".sheet-overlay");
    for (const mark of marks) {
        const note = element.querySelector(`.sheet-music [data-id="${mark.id}"]`);
        if (!note) continue;
        note.classList.add(`mark-${mark.kind}`);
        const head = note.querySelector(".notehead") ?? note;
        const h = head.getBoundingClientRect();
        const cx = h.left - box.left + h.width / 2;
        const cy = h.top - box.top + h.height / 2;
        if (mark.kind === "wrong" && mark.played) {
            const space = staffSpace(note) ?? h.height;
            const gy = cy - mark.steps * space / 2;
            const ghost = document.createElementNS(svgNs, "ellipse");
            ghost.setAttribute("class", "sheet-ghost");
            ghost.setAttribute("cx", cx + h.width * 1.3);
            ghost.setAttribute("cy", gy);
            ghost.setAttribute("rx", h.width * 0.5);
            ghost.setAttribute("ry", space * 0.42);
            ghost.setAttribute("transform", `rotate(-20 ${cx + h.width * 1.3} ${gy})`);
            overlay.appendChild(ghost);
            label(overlay, mark.played, cx + h.width * 2.1, gy + space * 0.4);
        }
        if (mark.kind === "early" || mark.kind === "late") {
            const top = note.closest(".staff")?.getBoundingClientRect().top - box.top ?? h.top - box.top;
            label(overlay, mark.kind === "early" ? "←" : "→", cx, Math.min(top, h.top - box.top) - 6, "sheet-arrow");
        }
    }
}

export function clearMarks(element) {
    const state = sheets.get(element);
    if (!state) return;
    state.marks = null;
    clearDrawnMarks(element);
}

function clearDrawnMarks(element) {
    element.querySelectorAll(".sheet-music [class*='mark-']").forEach(n => n.classList.remove("mark-wrong", "mark-missed", "mark-early", "mark-late", "mark-heat1", "mark-heat2", "mark-heat3"));
    element.querySelectorAll(".sheet-ghost, .sheet-label, .sheet-arrow").forEach(n => n.remove());
}

function label(overlay, text, x, y, kind = "sheet-label") {
    const t = document.createElementNS(svgNs, "text");
    t.setAttribute("class", kind);
    t.setAttribute("x", x);
    t.setAttribute("y", y);
    if (kind === "sheet-arrow") t.setAttribute("text-anchor", "middle");
    t.textContent = text;
    overlay.appendChild(t);
}

// The distance between a staff's lines, in px, from its five line paths.
function staffSpace(note) {
    const lines = [...(note.closest(".staff")?.querySelectorAll(":scope > path") ?? [])].map(p => p.getBoundingClientRect().top).sort((a, b) => a - b);
    return lines.length >= 5 ? (lines[lines.length - 1] - lines[0]) / (lines.length - 1) : null;
}

export function dispose(element) {
    const state = sheets.get(element);
    if (!state) return;
    cancelAnimationFrame(state.raf);
    state.observer?.disconnect();
    sheets.delete(element);
}
