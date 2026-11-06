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
    // Marks belong to the music they were made on: new music starts clean (a passage's note ids can be the last one's). What
    // Verovio is given is the music without its verses.
    if (state.mei !== mei) Object.assign(state, { marks: null, music: withoutVerses(mei) });
    Object.assign(state, { mei, options, dotnet, tk });
    sheets.set(element, state);
    element.classList.toggle("sheet-explain", !!options.explain);
    draw(element, state);
    if (!state.observer) {
        state.observer = new ResizeObserver(() => {
            if (Math.abs(element.clientWidth - state.width) > 1 || (state.options.layout === "page" && Math.abs(element.clientHeight - state.height) > 1)) draw(element, state);
        });
        state.observer.observe(element);
        element.addEventListener("click", event => {
            if (state.options.explain) {
                explainTap(state, event.target);
                return;
            }
            const note = event.target.closest?.(".sheet-music .note[data-id]");
            if (note) state.dotnet?.invokeMethodAsync("NoteTapped", note.dataset.id);
            // The bar tapped: on a note or anywhere in its space. A scroll is never a click, so it's never a tap.
            const measure = event.target.closest?.(".sheet-music .measure[data-id]") ?? measureAt(element, event.clientX, event.clientY);
            if (measure) state.dotnet?.invokeMethodAsync("BarTapped", measure.dataset.id);
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
    load(tk, state, state.music);
    state.pages = tk.getPageCount();
    music.innerHTML = tk.renderToSVG(Math.min(options.page ?? 1, state.pages));
    // A passage's bars are shared evenly between its systems, so the last one isn't left with a bar on its own.
    const systems = music.querySelectorAll(".system");
    const bars = music.querySelectorAll(".measure").length;
    if (options.layout === "line" && systems.length > 1 && systems[systems.length - 1].querySelectorAll(".measure").length < Math.ceil(bars / systems.length) - 1) {
        tk.setOptions({ breaks: "encoded" });
        load(tk, state, withSystemBreaks(state.music, Math.ceil(bars / systems.length), firstMeasure(options)));
        state.pages = tk.getPageCount();
        music.innerHTML = tk.renderToSVG(1);
    }
    state.timemap = tk.renderToTimemap({ includeMeasures: true });
    const overlay = element.querySelector(".sheet-overlay");
    overlay.replaceChildren();
    state.cursor = null;
    state.points = null;
    if (state.marks) showMarks(element, state.marks);
    drawBars(element, state);
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

// The MEI without a song's verses, where an import kept them as words under the music (a whole verse to a direction): they'd
// lie over each other and stretch their bars to fit, and they're not for playing. The directions a player reads (dolce,
// cresc., rit., D.C. al Fine) are short, and stay.
function withoutVerses(mei) {
    if (!mei.includes("<dir")) return mei;
    const ns = "http://www.music-encoding.org/ns/mei";
    const doc = new DOMParser().parseFromString(mei, "application/xml");
    const verses = [...doc.getElementsByTagNameNS(ns, "dir")].filter(d => d.textContent.trim().length > 60);
    if (verses.length === 0) return mei;
    for (const verse of verses) verse.remove();
    return new XMLSerializer().serializeToString(doc);
}

// The MEI with a system break before every `perSystem`th bar, counting from the first shown.
function withSystemBreaks(mei, perSystem, from = 0) {
    const ns = "http://www.music-encoding.org/ns/mei";
    const doc = new DOMParser().parseFromString(mei, "application/xml");
    // Only these: the file's own line and page breaks (an engraving's) would split it again, a page break onto a page not shown.
    for (const old of [...doc.getElementsByTagNameNS(ns, "sb"), ...doc.getElementsByTagNameNS(ns, "pb")]) old.remove();
    const measures = [...doc.getElementsByTagNameNS(ns, "measure")];
    for (let i = from + perSystem; i < measures.length; i += perSystem) measures[i].parentNode.insertBefore(doc.createElementNS(ns, "sb"), measures[i]);
    return new XMLSerializer().serializeToString(doc);
}

// The toolkit is shared by every sheet: before asking it about this sheet's music, make sure it's this sheet's that's loaded.
function ensureLoaded(state) {
    if (state.tk.current === state) return;
    state.tk.setOptions(state.loaded);
    load(state.tk, state, state.music);
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
// The bars chosen on a piece, behind the music: the bar a run starts at, or a loop's first to last bar, a band on each line they
// cover. Kept, so a redraw (turning the tablet, another page) draws them again.
export function highlightBars(element, first, last) {
    const state = sheets.get(element);
    if (!state) return;
    state.bars = first ? [first, last ?? first] : null;
    drawBars(element, state);
}

function drawBars(element, state) {
    element.querySelectorAll(".sheet-bar").forEach(r => r.remove());
    if (!state.bars) return;
    const measures = [...element.querySelectorAll(".sheet-music .measure[data-id]")];
    const from = measures.findIndex(m => m.dataset.id === state.bars[0]);
    const to = measures.findIndex(m => m.dataset.id === state.bars[1]);
    if (from < 0) return;
    const chosen = measures.slice(from, (to < from ? from : to) + 1);
    const box = element.getBoundingClientRect();
    const overlay = element.querySelector(".sheet-overlay");
    for (const system of new Set(chosen.map(m => m.closest(".system")))) {
        const line = chosen.filter(m => m.closest(".system") === system);
        const staves = line.flatMap(m => [...m.querySelectorAll(".staff")].map(s => s.getBoundingClientRect()));
        const left = line[0].getBoundingClientRect().left - box.left - 2;
        const right = line[line.length - 1].getBoundingClientRect().right - box.left + 2;
        const top = Math.min(...staves.map(s => s.top)) - box.top - 10;
        const bottom = Math.max(...staves.map(s => s.bottom)) - box.top + 10;
        const rect = document.createElementNS(svgNs, "rect");
        rect.setAttribute("class", "sheet-bar");
        rect.setAttribute("x", left);
        rect.setAttribute("y", top);
        rect.setAttribute("width", right - left);
        rect.setAttribute("height", bottom - top);
        overlay.prepend(rect);
    }
}

// The bar whose space a point is in: between its bar lines, and from a little over its top staff to a little under its lowest.
function measureAt(element, x, y) {
    return [...element.querySelectorAll(".sheet-music .measure[data-id]")].find(m => {
        const staves = [...m.querySelectorAll(":scope > .staff")].map(s => s.getBoundingClientRect());
        if (staves.length === 0) return false;
        const r = m.getBoundingClientRect();
        return x >= r.left && x <= r.right && y >= Math.min(...staves.map(s => s.top)) - 20 && y <= Math.max(...staves.map(s => s.bottom)) + 20;
    });
}

// Where each moment of the music is on the sheet: for every onset in Verovio's time map that's on this page, its time in quarter
// notes, the left edge of its first note head and its system's top and bottom, in px from the sheet's corner; and each system's
// end, the barline closing its last bar at the time that bar ends.
function positions(element, state, onsets = state.options.onsets) {
    const box = element.getBoundingClientRect();
    const points = [];
    // Onsets given by .NET take its place: a stretch of a real piece, where Verovio's time map is the whole piece's, or a run's
    // own (Verovio's map plays repeats its own way, and a run can start anywhere). A note played twice has two.
    if (onsets) {
        for (const [id, q] of onsets) {
            const head = element.querySelector(`.sheet-music [data-id="${id}"] .notehead`) ?? element.querySelector(`.sheet-music [data-id="${id}"]`);
            if (!head) continue;
            const system = head.closest(".system");
            const h = head.getBoundingClientRect();
            const s = system.getBoundingClientRect();
            points.push({ q, x: h.left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6, system });
        }
    } else {
        for (const entry of state.timemap) {
            const id = (entry.on ?? []).find(i => element.querySelector(`.sheet-music [data-id="${i}"] .notehead, .sheet-music .rest[data-id="${i}"]`));
            if (!id) continue;
            const head = element.querySelector(`.sheet-music [data-id="${id}"] .notehead`) ?? element.querySelector(`.sheet-music [data-id="${id}"]`);
            const system = head.closest(".system");
            const h = head.getBoundingClientRect();
            const s = system.getBoundingClientRect();
            points.push({ q: entry.qstamp, x: h.left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6, system });
        }
    }

    // The systems' ends, so the cursor carries on through a system's last note to its closing barline, reaching it as the bar
    // ends, instead of stopping on the note. Bar times are the time map's, moved onto the stretch's times for a stretch.
    const starts = state.timemap.filter(e => e.measureOn).map(e => ({ id: e.measureOn, q: e.qstamp }));
    let shift = 0;
    if (onsets) {
        const known = onsets.map(([id, q]) => ({ q, entry: state.timemap.find(e => e.on?.includes(id)) })).find(o => o.entry);
        if (!known) return points;
        shift = known.q - known.entry.qstamp;
    }
    for (const system of new Set(points.map(p => p.system))) {
        const bars = system.querySelectorAll(".measure");
        const last = bars[bars.length - 1];
        const i = starts.findIndex(m => m.id === last?.dataset.id);
        if (i < 0) continue;
        const r = ([...last.querySelectorAll(".barLine")].pop() ?? last).getBoundingClientRect();
        const first = points.find(p => p.system === system);
        const q = (i + 1 < starts.length ? starts[i + 1].q : Math.max(...state.timemap.map(e => e.qstamp))) + shift;
        points.push({ q, x: r.left - box.left - 3, top: first.top, bottom: first.bottom, system, end: true });
    }
    // In time order, a system's end before the next system's first note at the same moment.
    return points.sort((a, b) => a.q - b.q || (b.end ? 1 : 0) - (a.end ? 1 : 0));
}

// Moves a cursor in the look's accent with the music: bar 1 starts `msUntilStart` from now, at `msPerQuarter`, from quarter
// `startQuarter` of the music (later than 0 after a pickup). Until then (the count-in) it waits at the first note and pulses
// with the click: a beat every `beatMs`, one of them `msUntilBeat` from now. Given the run's own onsets ([id, quarter]), it goes
// from note to note of those, through repeats or from wherever the run starts. A guide only: nothing is scored from it.
export function cursorStart(element, msUntilStart, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat, onsets) {
    const state = sheets.get(element);
    if (!state) return;
    cursorStop(element);
    const points = positions(element, state, onsets ?? state.options.onsets);
    state.points = points;
    if (points.length === 0) return;
    const bars = windowQuarters ? measures(element, state) : null;
    const overlay = element.querySelector(".sheet-overlay");
    const line = document.createElementNS(svgNs, "rect");
    line.setAttribute("class", "sheet-cursor");
    line.setAttribute("width", "3");
    overlay.appendChild(line);
    state.cursor = line;
    const start = performance.now() + msUntilStart;
    const beat = performance.now() + (msUntilBeat ?? msUntilStart);
    let shown = null;
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
        if (here.system !== shown) follow(element, shown = here.system);
        // The count-in: bright on each click, fading towards the next.
        if (now < start && beatMs > 0) {
            const phase = (((now - beat) / beatMs) % 1 + 1) % 1;
            line.style.opacity = String(0.3 + 0.7 * (1 - phase) * (1 - phase));
        } else line.style.opacity = "";
        if (bars) for (const bar of bars) bar.cover.style.display = bar.end <= q - 0.05 || bar.start > q + windowQuarters ? "" : "none";
        state.raf = requestAnimationFrame(frame);
    };
    state.raf = requestAnimationFrame(frame);
}

// Wait for me's cursor: still, just before a note (the first of the notes waited for), its system kept in view.
export function cursorAt(element, id) {
    const state = sheets.get(element);
    const head = element.querySelector(`.sheet-music [data-id="${id}"] .notehead`) ?? element.querySelector(`.sheet-music [data-id="${id}"]`);
    if (!state || !head) return;
    cancelAnimationFrame(state.raf);
    if (!state.cursor) {
        state.cursor = document.createElementNS(svgNs, "rect");
        state.cursor.setAttribute("class", "sheet-cursor");
        state.cursor.setAttribute("width", "3");
        element.querySelector(".sheet-overlay").appendChild(state.cursor);
    }
    const box = element.getBoundingClientRect();
    const system = head.closest(".system");
    const s = system.getBoundingClientRect();
    state.cursor.style.opacity = "";
    state.cursor.setAttribute("x", head.getBoundingClientRect().left - box.left - 6);
    state.cursor.setAttribute("y", s.top - box.top - 6);
    state.cursor.setAttribute("height", s.height + 12);
    follow(element, system);
}

// Keeps the system being played in view. In a frame of its own that scrolls (marked data-follow, as a piece's is), it goes to
// the frame's upper third, so the next line shows below it; elsewhere the screen moves only if the system isn't all in view.
function follow(element, system) {
    const frame = element.closest("[data-follow]");
    if (!frame) {
        system.scrollIntoView({ block: "nearest", behavior: "smooth" });
        return;
    }
    const top = system.getBoundingClientRect().top - frame.getBoundingClientRect().top + frame.scrollTop;
    frame.scrollTo({ top: Math.max(0, top - frame.clientHeight / 3), behavior: "smooth" });
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

// What's this: a tap names the nearest symbol under it, the innermost (an accidental before its note, a note before its beam).
const symbols = ["accid", "artic", "dots", "stem", "flag", "fing", "dynam", "hairpin", "dir", "tempo", "fermata", "trill", "mordent", "turn",
    "arpeg", "gliss", "bTrem", "fTrem", "breath", "caesura", "slur", "phrase", "tie", "pedal", "octave", "ending", "repeatMark", "tuplet",
    "note", "rest", "mRest", "multiRest", "chord", "beam", "ledgerLines", "clef", "keySig", "meterSig", "barLine", "mNum", "grpSym", "reh",
    "harm"];
const symbolSelector = symbols.map(s => `.sheet-music .${s}`).join(", ");
// The glyphs that tell a clef's shape: SMuFL's G, F and C clefs.
const clefGlyphs = { E050: "G", E052: "G", E062: "F", E064: "F", E05C: "C" };

export function explain(element, on) {
    const state = sheets.get(element);
    if (!state) return;
    state.options.explain = on;
    element.classList.toggle("sheet-explain", on);
}

function explainTap(state, target) {
    const symbol = target.closest?.(symbolSelector);
    if (!symbol) return;
    let kind = symbols.find(s => symbol.classList.contains(s));
    if (kind === "clef") {
        const glyph = symbol.querySelector("use")?.getAttribute("xlink:href") ?? symbol.querySelector("use")?.getAttribute("href") ?? "";
        const shape = Object.entries(clefGlyphs).find(([code]) => glyph.includes(code))?.[1];
        if (shape) kind += ":" + shape;
    }
    // A time signature's glyphs: common or cut time, or its two numbers, a row each (12 over 8 is three digits in two rows).
    if (kind === "meterSig") {
        const glyphs = [...symbol.querySelectorAll("use")].map(u => ({ code: (u.getAttribute("xlink:href") ?? u.getAttribute("href") ?? "").match(/E08([0-9A-F])/)?.[1], y: u.getBoundingClientRect().top }));
        if (glyphs.some(g => g.code === "A")) kind += ":common";
        else if (glyphs.some(g => g.code === "B")) kind += ":cut";
        else if (glyphs.length > 1 && glyphs.every(g => /[0-9]/.test(g.code ?? ""))) {
            const top = Math.min(...glyphs.map(g => g.y));
            const row = upper => glyphs.filter(g => (Math.abs(g.y - top) < 2) === upper).map(g => g.code).join("");
            if (row(false)) kind += `:${row(true)}/${row(false)}`;
        }
    }
    // A repeat sign's bar line has its two dots.
    if (kind === "barLine" && symbol.querySelector("ellipse, circle")) kind += ":rpt";
    const within = symbol.parentElement?.closest(".sheet-music .note[data-id], .sheet-music .chord[data-id], .sheet-music .measure[data-id]")?.dataset.id ?? null;
    state.dotnet?.invokeMethodAsync("SymbolTapped", symbol.dataset.id ?? "", kind, within);
}

// Marks notes after the passage, all at once. Each mark: id, kind ("right", "wrong", "missed", "early", "late", "extra", or a
// heat-map shade), for a wrong note what was played ("G4") and how many staff positions away it is, so a red note head shows it
// there, and the written note's name ("F4") when it's to be named; and for an early or late one `q`, when it was played in
// quarters, so a ring shows it there.
export function showMarks(element, marks) {
    const state = sheets.get(element);
    if (!state) return;
    state.marks = marks;
    clearDrawnMarks(element);
    for (const mark of marks) drawMark(element, state, mark);
}

// Marks notes during a run, as each is judged: a note marked again (a slip put right) loses its earlier mark.
export function markLive(element, marks) {
    const state = sheets.get(element);
    if (!state) return;
    for (const mark of marks) drawMark(element, state, mark);
}

const markKinds = ["mark-right", "mark-wrong", "mark-missed", "mark-early", "mark-late", "mark-heat1", "mark-heat2", "mark-heat3", "mark-picked"];

function drawMark(element, state, mark) {
    const note = element.querySelector(`.sheet-music [data-id="${mark.id}"]`);
    if (!note) return;
    // An extra key is drawn beside the note before it, which keeps its own mark.
    if (mark.kind !== "extra") {
        note.classList.remove(...markKinds);
        element.querySelectorAll(`.sheet-overlay [data-for="${mark.id}"]`).forEach(n => n.remove());
        note.classList.add(`mark-${mark.kind}`);
    }
    const box = element.getBoundingClientRect();
    const overlay = element.querySelector(".sheet-overlay");
    const head = note.querySelector(".notehead") ?? note;
    const h = head.getBoundingClientRect();
    const cx = h.left - box.left + h.width / 2;
    const cy = h.top - box.top + h.height / 2;
    const space = staffSpace(note) ?? h.height;
    // Words go above the note, or above its staff when it's lower.
    const above = Math.min((note.closest(".staff")?.getBoundingClientRect().top ?? h.top) - box.top, h.top - box.top) - 6;
    // The key played: a red head beside the note, that many positions away, named in red above it. A wrong note's written name
    // goes beside that in grey, over the note (grey too), so the pair reads left to right as the heads do: meant, then played.
    if ((mark.kind === "wrong" || mark.kind === "extra") && mark.played) {
        const gy = cy - mark.steps * space / 2;
        const gx = cx + h.width * 1.3;
        overlay.appendChild(ghost(mark.id, gx, gy, h.width, space, "sheet-ghost sheet-played"));
        const y = Math.min(above, gy - space - 6);
        if (mark.kind === "wrong" && mark.written) label(overlay, [[mark.written, "sheet-meant"], [mark.played, "sheet-heard"]], (cx + gx) / 2, y, "sheet-label", mark.id);
        else label(overlay, mark.played, gx, y, "sheet-label", mark.id);
    }
    // Where it was played, when that's given: a ring on the note's line at that moment's place along the system (on a green note
    // too, when it was a little off).
    const at = mark.q == null ? null : xAt(element, state, mark.q, note.closest(".system"));
    if (at != null && Math.abs(at - cx) > h.width * 0.6) overlay.appendChild(ghost(mark.id, at, cy, h.width, space, "sheet-ghost sheet-when"));
    if (mark.kind === "early" || mark.kind === "late") label(overlay, mark.kind === "early" ? "←" : "→", cx, above, "sheet-arrow", mark.id);
}

function ghost(id, cx, cy, headWidth, space, kind = "sheet-ghost") {
    const g = document.createElementNS(svgNs, "ellipse");
    g.setAttribute("class", kind);
    g.dataset.for = id;
    g.setAttribute("cx", cx);
    g.setAttribute("cy", cy);
    g.setAttribute("rx", headWidth * 0.5);
    g.setAttribute("ry", space * 0.42);
    g.setAttribute("transform", `rotate(-20 ${cx} ${cy})`);
    return g;
}

// Where a moment of the music falls along a system, in px from the sheet's left: between the onsets around it.
function xAt(element, state, q, system) {
    const points = (state.points ?? positions(element, state)).filter(p => p.system === system);
    if (points.length === 0) return null;
    if (q <= points[0].q) return points[0].x - (points.length > 1 ? (points[1].x - points[0].x) * (points[0].q - q) / Math.max(0.01, points[1].q - points[0].q) : 0);
    for (let i = 0; i < points.length - 1; i++) {
        if (q <= points[i + 1].q) return points[i].x + (points[i + 1].x - points[i].x) * (q - points[i].q) / Math.max(0.01, points[i + 1].q - points[i].q);
    }
    return points[points.length - 1].x;
}

export function clearMarks(element) {
    const state = sheets.get(element);
    if (!state) return;
    state.marks = null;
    clearDrawnMarks(element);
}

function clearDrawnMarks(element) {
    element.querySelectorAll(".sheet-music [class*='mark-']").forEach(n => n.classList.remove(...markKinds));
    element.querySelectorAll(".sheet-ghost, .sheet-label, .sheet-arrow").forEach(n => n.remove());
}

// Words centred at x with their baseline at y, moved up clear of any already there (neighbouring notes' marks): a string, or
// parts side by side in their own colours ([["F4", "sheet-meant"], ["G4", "sheet-heard"]]), which move together.
function label(overlay, text, x, y, kind = "sheet-label", forId = null) {
    const t = document.createElementNS(svgNs, "text");
    t.setAttribute("class", kind);
    if (forId) t.dataset.for = forId;
    t.setAttribute("x", x);
    t.setAttribute("y", y);
    t.setAttribute("text-anchor", "middle");
    if (typeof text === "string") t.textContent = text;
    else text.forEach(([words, part], i) => {
        const s = document.createElementNS(svgNs, "tspan");
        s.setAttribute("class", part);
        if (i > 0) s.setAttribute("dx", "6");
        s.textContent = words;
        t.appendChild(s);
    });
    overlay.appendChild(t);
    // In the way: the marks' words already there, and the music's own words (a figure's named first note, a dynamic).
    const sheet = overlay.closest(".sheet").getBoundingClientRect();
    const printed = [...overlay.closest(".sheet").querySelectorAll(".sheet-music .dir, .sheet-music .tempo, .sheet-music .dynam")].map(e => e.getBoundingClientRect()).map(r => ({ x: r.left - sheet.left, y: r.top - sheet.top, width: r.width, height: r.height }));
    const others = [...[...overlay.querySelectorAll(".sheet-label, .sheet-arrow")].filter(o => o !== t).map(o => o.getBBox()), ...printed];
    // Side by side counts as in the way unless there's a little space between, so neighbours' names don't run together.
    const inWay = () => {
        const b = t.getBBox();
        return others.find(r => r.x < b.x + b.width + 4 && b.x < r.x + r.width + 4 && r.y < b.y + b.height && b.y < r.y + r.height);
    };
    for (let tries = 0; tries < others.length; tries++) {
        const hit = inWay();
        if (!hit) break;
        const b = t.getBBox();
        t.setAttribute("y", Number(t.getAttribute("y")) - (b.y + b.height - hit.y) - 2);
    }
    // Never above the paper; with no room above, beside the name in the way instead.
    const top = t.getBBox().y;
    if (top < 1) t.setAttribute("y", Number(t.getAttribute("y")) - top + 1);
    for (let tries = 0; tries < others.length; tries++) {
        const hit = inWay();
        if (!hit) break;
        t.setAttribute("x", Number(t.getAttribute("x")) + hit.x + hit.width + 4 - t.getBBox().x);
    }
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
