// The sheet: Verovio engraves the MEI as real printed music, black on paper, and an overlay on top draws the cursor while playing
// and the marks afterwards, found by each note's id. The engraving itself is never changed. Sizes: a printed piano staff is about
// 7 mm tall, and the 13-inch tablet's long side is about 280 mm, so a millimetre is the screen's long side in CSS pixels / 280.

const svgNs = "http://www.w3.org/2000/svg";
const sheets = new WeakMap();
let sheetCount = 0;
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

// A reading of a PDF's system (Transcription's MEI), engraved as one system across `width` px: for Claude to set beside the
// print, and for the review to show under it. A toolkit of its own, so a sheet on screen isn't disturbed. Returns the PNG, a URL to
// show it by, and each bar's left and right edge as a share of the width, to mark a bar on it.
let engraver;
export async function engrave(mei, width) {
    await toolkit();
    engraver ??= new verovio.toolkit();
    engraver.setOptions({
        font: "Leipzig",
        scale: 100,
        pageWidth: 2000,
        pageHeight: 60000,
        adjustPageHeight: true,
        breaks: "encoded",
        minLastJustification: 0,
        header: "none",
        footer: "none",
        pageMarginLeft: 24,
        pageMarginRight: 24,
        pageMarginTop: 24,
        pageMarginBottom: 24,
        svgViewBox: true,
        svgHtml5: true,
    });
    if (!engraver.loadData(withoutVerses(mei))) throw new Error("Verovio couldn't read the reading.");
    const svg = engraver.renderToSVG(1);

    // Laid out off screen at the width asked, to find the bars, then drawn on white into a PNG of that width.
    const host = document.createElement("div");
    host.style.cssText = `position: absolute; left: -100000px; top: 0; width: ${width}px; visibility: hidden`;
    host.innerHTML = svg;
    document.body.appendChild(host);
    const drawn = host.querySelector("svg");
    drawn.style.width = `${width}px`;
    drawn.style.height = "auto";
    const box = drawn.getBoundingClientRect();
    const bars = [...host.querySelectorAll(".measure")].map(m => {
        const r = m.getBoundingClientRect();
        return [(r.left - box.left) / box.width, (r.right - box.left) / box.width];
    });
    const height = Math.max(1, Math.round(box.height));
    host.remove();

    const image = new Image();
    const source = URL.createObjectURL(new Blob([svg], { type: "image/svg+xml" }));
    try {
        image.src = source;
        await image.decode();
    } finally {
        URL.revokeObjectURL(source);
    }
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    context.fillStyle = "#fff";
    context.fillRect(0, 0, width, height);
    context.drawImage(image, 0, 0, width, height);
    const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
    return { png: new Uint8Array(await blob.arrayBuffer()), url: URL.createObjectURL(blob), bars };
}

export function pixelsPerMm() {
    return Math.max(screen.width, screen.height) / 280;
}

// Engraves `mei` into the sheet: options are layout ("line": a passage, its systems as wide as the sheet; "scroll": a whole
// piece down one sheet; "page": one page the sheet's size), staffMm, font, page and header. Redraws by itself when the sheet's
// width changes, and tells .NET when it has painted.
export async function render(element, mei, options, dotnet) {
    const tk = await toolkit();
    const state = sheets.get(element) ?? { number: ++sheetCount };
    // Marks belong to the music they were made on: new music starts clean (a passage's note ids can be the last one's). What
    // Verovio is given is the music without its verses.
    if (state.mei !== mei) Object.assign(state, { marks: null, music: withoutVerses(mei) });
    Object.assign(state, { mei, options, dotnet, tk, overlay: element.querySelector(".sheet-overlay") });
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
    // A brace (or a bracket) joining the staves hangs to their left: the margin there makes room for it, so it stands as far
    // in from the paper's edge as the last bar line does on the right, not on the edge itself.
    const joined = /\ssymbol="brac(e|ket)"/.test(state.music);
    // Fingering is printed over the top staff, and a mark's words (the name of a wrong key) go over the fingering: a deeper
    // top margin leaves them room there, over their own note.
    const fingered = /<fing[\s>]/.test(state.music);
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
        pageMarginLeft: joined ? 50 : 24,
        pageMarginRight: 24,
        pageMarginTop: options.layout === "page" || fingered ? 40 : 24,
        pageMarginBottom: 24,
        svgViewBox: true,
        svgHtml5: true,
    };
    tk.setOptions(state.loaded);
    load(tk, state, state.music);
    state.pages = tk.getPageCount();
    music.innerHTML = tk.renderToSVG(Math.min(options.page ?? 1, state.pages));
    // A passage's bars are shared evenly between its systems, so the last one isn't left with a bar on its own. Not a whole
    // piece's ("scroll"): its bars differ too much for a line of each to hold as many, and the full ones would be squeezed. Its
    // lines break where the bars fit, and a last line that's short keeps its bars' own widths.
    const systems = music.querySelectorAll(".system");
    const bars = music.querySelectorAll(".measure").length;
    if (options.layout === "line" && systems.length > 1 && systems[systems.length - 1].querySelectorAll(".measure").length < Math.ceil(bars / systems.length) - 1) {
        tk.setOptions({ breaks: "encoded" });
        load(tk, state, withSystemBreaks(state.music, Math.ceil(bars / systems.length), firstMeasure(options)));
        state.pages = tk.getPageCount();
        music.innerHTML = tk.renderToSVG(1);
    }
    state.timemap = tk.renderToTimemap({ includeMeasures: true });
    // New paper: what was drawn over the old one goes, and so does what was found on it (each id's place, the music's words).
    state.overlay.replaceChildren();
    Object.assign(state, { cursor: null, points: null, byId: null, words: null, drawn: new Map() });
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
    // Whose music it has, by the sheet's number: the toolkit outlives every sheet, and mustn't keep one that has gone.
    tk.current = state.number;
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
    if (state.tk.current === state.number) return;
    state.tk.setOptions(state.loaded);
    load(state.tk, state, state.music);
}

// Everything printed that has an id (a note, a rest, a bar), by that id: listed once for the music as it's drawn. Looking one up
// in the page itself walks the whole piece, which a long piece pays for at every mark and every step of the cursor.
function printedById(element, state) {
    if (!state.byId) {
        state.byId = new Map();
        for (const printed of element.querySelectorAll(".sheet-music [data-id]")) if (!state.byId.has(printed.dataset.id)) state.byId.set(printed.dataset.id, printed);
    }
    return state.byId;
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
    const state = sheets.get(element);
    if (!state) return [];
    const byId = printedById(element, state);
    return ids.filter(id => byId.has(id));
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
function positions(element, state) {
    const box = element.getBoundingClientRect();
    const byId = printedById(element, state);
    const onsets = state.options.onsets;
    const points = [];
    // Onsets given by .NET take its place: a stretch of a real piece, where Verovio's time map is the whole piece's. (A run's
    // cursor has the run's own path instead: pathPoints.)
    if (onsets) {
        for (const [id, q] of onsets) {
            const printed = byId.get(id);
            const head = printed?.querySelector(".notehead") ?? printed;
            if (!head) continue;
            const system = head.closest(".system");
            const h = head.getBoundingClientRect();
            const s = system.getBoundingClientRect();
            points.push({ q, x: h.left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6, system });
        }
    } else {
        for (const entry of state.timemap) {
            // The first of the moment's notes that's on this page with a head to stand at, or a rest.
            const printed = (entry.on ?? []).map(i => byId.get(i)).find(e => e && (e.classList.contains("rest") || e.querySelector(".notehead")));
            if (!printed) continue;
            const head = printed.querySelector(".notehead") ?? printed;
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

// A run's own way through the music, from .NET: `bars`, every bar as it's played ([its place in the score, the quarter it starts
// at, how many it lasts]: a repeated or looped bar once each time round), and `events`, what's printed in those bars ([the bar's
// place, an id, quarters from the bar's start] for each note and rest, both hands'). A point for each moment something is
// printed, where it is on the paper, so the cursor passes each as it's due and moves evenly between: through rests, and through
// bars the hand played has nothing in. Where the bar played next isn't the next along the line (a new line, a repeat, a loop
// going round), the bar ends at its closing bar line as its time does, and the next starts at once where its music does.
function pathPoints(element, state, path) {
    const box = element.getBoundingClientRect();
    const byId = printedById(element, state);
    const drawn = [...element.querySelectorAll(".sheet-music .measure")];
    // Each bar on the paper: its measure, and the place of each moment in it (the leftmost of what's printed then).
    const places = new Map();
    for (const [index, id, at] of path.events) {
        const printed = byId.get(id);
        if (!printed) continue;
        if (!places.has(index)) places.set(index, { measure: printed.closest(".measure"), moments: new Map() });
        // A whole bar's rest is printed in the middle of its bar, not where its time starts.
        if (printed.classList.contains("mRest")) continue;
        const x = (printed.querySelector(".notehead") ?? printed).getBoundingClientRect().left - box.left - 6;
        const moments = places.get(index).moments;
        moments.set(at, Math.min(x, moments.get(at) ?? Infinity));
    }
    // A bar with nothing printed that was found is the one that many along from one that was.
    const known = [...places].find(([, place]) => place.measure);
    const along = known ? drawn.indexOf(known[1].measure) - known[0] : 0;
    const lines = new Map();
    const placeOf = index => {
        const place = places.get(index) ?? { measure: drawn[index + along], moments: new Map() };
        places.set(index, place);
        if (!place.measure || place.system) return place;
        place.system = place.measure.closest(".system");
        if (!lines.has(place.system)) {
            const s = place.system.getBoundingClientRect();
            lines.set(place.system, [s.top - box.top - 6, s.bottom - box.top + 6]);
        }
        [place.top, place.bottom] = lines.get(place.system);
        place.moments = [...place.moments].sort((a, b) => a[0] - b[0]);
        // The bar is as wide as its staff's lines (a slur or a pedal mark drawn with it can run on past them). A bar line in
        // its right half closes it; one before its music (a repeat's start) is passed before the music starts, as the clef and
        // signatures a line opens with are.
        const m = (place.measure.querySelector(":scope > .staff > path") ?? place.measure).getBoundingClientRect();
        const middle = (m.left + m.right) / 2;
        const barLines = [...place.measure.querySelectorAll(":scope > .barLine")].map(b => b.getBoundingClientRect());
        const opening = [...place.measure.querySelectorAll(":scope > .staff > .clef, :scope > .staff > .keySig, :scope > .staff > .meterSig")].map(e => e.getBoundingClientRect().right);
        const closing = barLines.filter(b => b.left > middle).map(b => b.left);
        place.close = (closing.length > 0 ? Math.max(...closing) : m.right) - box.left - 3;
        place.open = Math.min(Math.max(m.left, ...opening, ...barLines.filter(b => b.right < middle).map(b => b.right)) - box.left + 2, place.moments[0]?.[1] ?? place.close);
        return place;
    };
    const points = [];
    let before = null;
    path.bars.forEach(([index, start, length], k) => {
        const place = placeOf(index);
        if (!place.measure) return;
        const { system, top, bottom } = place;
        // Carried on along its line from the bar before, a bar needs no start of its own: the cursor crosses the bar line on its
        // way from that bar's last moment to this one's first.
        const carried = before !== null && before.index + 1 === index && before.system === system;
        if (!carried && place.moments[0]?.[0] !== 0) points.push({ q: start, x: place.open, top, bottom, system });
        for (const [at, x] of place.moments) points.push({ q: start + at, x, top, bottom, system });
        const next = path.bars[k + 1];
        if (!next || next[0] !== index + 1 || placeOf(next[0]).system !== system) points.push({ q: start + length, x: place.close, top, bottom, system, end: true });
        before = { index, system };
    });
    return points;
}

// Moves a cursor in the look's accent with the music: bar 1 starts `msUntilStart` from now, at `msPerQuarter`, from quarter
// `startQuarter` of the music (later than 0 after a pickup). Until the music's first moment (the count-in) it waits there and
// pulses with the click: a beat every `beatMs`, one of them `msUntilBeat` from now. Given the run's own path (pathPoints), it
// keeps to that, through repeats or from wherever the run starts; otherwise it goes from note to note of the sheet's music.
// Not `shown`, it isn't drawn, but the line being played is still kept in view. A guide only: nothing is scored from it.
export function cursorStart(element, msUntilStart, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat, path, shown = true) {
    const state = sheets.get(element);
    if (!state) return;
    cursorStop(element);
    const points = path ? pathPoints(element, state, path) : positions(element, state);
    state.points = points;
    if (points.length === 0) return;
    const bars = windowQuarters ? measures(element, state) : null;
    const line = document.createElementNS(svgNs, "rect");
    line.setAttribute("class", "sheet-cursor");
    line.setAttribute("width", "3");
    if (!shown) line.style.display = "none";
    state.overlay.appendChild(line);
    state.cursor = line;
    const start = performance.now() + msUntilStart;
    const beat = performance.now() + (msUntilBeat ?? msUntilStart);
    const opens = Math.min(points[0].q, startQuarter);
    let at = 0;
    let inView = null;
    const frame = now => {
        const q = startQuarter + (now - start) / msPerQuarter;
        // Time only goes on, so the place does too.
        while (at < points.length - 1 && points[at + 1].q <= q) at++;
        const here = points[at];
        const next = points[at + 1];
        const x = next && !here.end && next.system === here.system && q > here.q ? here.x + (next.x - here.x) * (q - here.q) / (next.q - here.q) : here.x;
        line.setAttribute("x", x);
        line.setAttribute("y", here.top);
        line.setAttribute("height", here.bottom - here.top);
        if (here.system !== inView) follow(element, inView = here.system);
        // The count-in: bright on each click, fading towards the next.
        if (q < opens && beatMs > 0) {
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
    const printed = state && printedById(element, state).get(id);
    const head = printed?.querySelector(".notehead") ?? printed;
    if (!head) return;
    cancelAnimationFrame(state.raf);
    if (!state.cursor) {
        state.cursor = document.createElementNS(svgNs, "rect");
        state.cursor.setAttribute("class", "sheet-cursor");
        state.cursor.setAttribute("width", "3");
        state.overlay.appendChild(state.cursor);
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
    const byId = printedById(element, state);
    const starts = state.timemap.filter(e => e.measureOn).map(e => ({ id: e.measureOn, start: e.qstamp }));
    const last = Math.max(...state.timemap.map(e => e.qstamp));
    return starts.map((m, i) => {
        const g = byId.get(m.id);
        if (!g) return null;
        const r = g.getBoundingClientRect();
        const cover = document.createElementNS(svgNs, "rect");
        cover.setAttribute("class", "sheet-cover");
        cover.setAttribute("x", r.left - box.left);
        cover.setAttribute("y", r.top - box.top - 8);
        cover.setAttribute("width", r.width + 1);
        cover.setAttribute("height", r.height + 16);
        cover.style.display = "none";
        state.overlay.appendChild(cover);
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
    clearDrawnMarks(element, state);
    for (const mark of marks) drawMark(element, state, mark);
}

// Marks notes during a run, as each is judged: a note marked again (a slip put right) loses its earlier mark.
export function markLive(element, marks) {
    const state = sheets.get(element);
    if (!state) return;
    for (const mark of marks) drawMark(element, state, mark);
}

const markKinds = ["mark-right", "mark-wrong", "mark-missed", "mark-early", "mark-late", "mark-apart", "mark-heat1", "mark-heat2", "mark-heat3", "mark-picked"];

function drawMark(element, state, mark) {
    const note = printedById(element, state).get(mark.id);
    if (!note) return;
    // What's drawn over the paper for this note (the key played, its name, an arrow, a word), kept so it can be taken off again.
    const mine = state.drawn.get(mark.id) ?? [];
    // An extra key is drawn beside the note before it, which keeps its own mark, as a note with a word over it does.
    if (mark.kind !== "extra" && mark.kind !== "hint") {
        note.classList.remove(...markKinds);
        mine.splice(0).forEach(n => n.remove());
        note.classList.add(`mark-${mark.kind}`);
    }
    // A note with nothing to draw beside it is its colour and no more: nothing on the paper is measured for it.
    const beside = mark.kind === "hint" || mark.kind === "early" || mark.kind === "late" || mark.q != null || ((mark.kind === "wrong" || mark.kind === "extra") && mark.played);
    if (!beside) return;
    state.drawn.set(mark.id, mine);
    const box = element.getBoundingClientRect();
    const overlay = state.overlay;
    const head = note.querySelector(".notehead") ?? note;
    const h = head.getBoundingClientRect();
    const cx = h.left - box.left + h.width / 2;
    const cy = h.top - box.top + h.height / 2;
    const space = staffSpace(note) ?? h.height;
    // Words go above the note, or above its staff when it's lower.
    const above = Math.min((note.closest(".staff")?.getBoundingClientRect().top ?? h.top) - box.top, h.top - box.top) - 6;
    // A word over the note ("together", over a chord played a key at a time); with no word, the one there goes.
    if (mark.kind === "hint") {
        for (const word of mine.filter(n => n.classList.contains("sheet-hint"))) mine.splice(mine.indexOf(word), 1)[0].remove();
        if (mark.played) mine.push(label(element, state, mark.played, cx, above, "sheet-hint", mark.id));
        return;
    }
    // The key played: a red head beside the note, that many positions away, named in red above it. A wrong note's written name
    // goes beside that in grey, over the note (grey too), so the pair reads left to right as the heads do: meant, then played.
    if ((mark.kind === "wrong" || mark.kind === "extra") && mark.played) {
        const gy = cy - mark.steps * space / 2;
        const gx = cx + h.width * 1.3;
        mine.push(overlay.appendChild(ghost(mark.id, gx, gy, h.width, space, "sheet-ghost sheet-played")));
        const y = Math.min(above, gy - space - 6);
        mine.push(mark.kind === "wrong" && mark.written
            ? label(element, state, [[mark.written, "sheet-meant"], [mark.played, "sheet-heard"]], (cx + gx) / 2, y, "sheet-label", mark.id)
            : label(element, state, mark.played, gx, y, "sheet-label", mark.id));
    }
    // Where it was played, when that's given: a ring on the note's line at that moment's place along the system (on a green note
    // too, when it was a little off).
    const at = mark.q == null ? null : xAt(element, state, mark.q, note.closest(".system"));
    if (at != null && Math.abs(at - cx) > h.width * 0.6) mine.push(overlay.appendChild(ghost(mark.id, at, cy, h.width, space, "sheet-ghost sheet-when")));
    if (mark.kind === "early" || mark.kind === "late") mine.push(label(element, state, mark.kind === "early" ? "←" : "→", cx, above, "sheet-arrow", mark.id));
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
    state.points ??= positions(element, state);
    const points = state.points.filter(p => p.system === system);
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
    clearDrawnMarks(element, state);
}

function clearDrawnMarks(element, state) {
    element.querySelectorAll(".sheet-music [class*='mark-']").forEach(n => n.classList.remove(...markKinds));
    state.overlay.querySelectorAll(".sheet-ghost, .sheet-label, .sheet-arrow, .sheet-hint").forEach(n => n.remove());
    state.drawn = new Map();
}

// Words centred at x with their baseline at y, moved up clear of any already there (neighbouring notes' marks): a string, or
// parts side by side in their own colours ([["F4", "sheet-meant"], ["G4", "sheet-heard"]]), which move together. Returns them.
function label(element, state, text, x, y, kind = "sheet-label", forId = null) {
    const overlay = state.overlay;
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
    // In the way: the marks' words already there, and the music's own (a figure's named first note, a dynamic, the fingering),
    // which are found once for the music as it's drawn.
    if (!state.words) {
        const sheet = element.getBoundingClientRect();
        state.words = [...element.querySelectorAll(".sheet-music .dir, .sheet-music .tempo, .sheet-music .dynam, .sheet-music .fing")].map(e => e.getBoundingClientRect()).map(r => ({ x: r.left - sheet.left, y: r.top - sheet.top, width: r.width, height: r.height }));
    }
    const others = [...[...overlay.querySelectorAll(".sheet-label, .sheet-arrow, .sheet-hint")].filter(o => o !== t).map(o => o.box), ...state.words];
    // The words are measured once and their box moved with them: measuring again after each move, and against every other
    // word on the piece, is what a long, rough run would pay most for.
    const measured = t.getBBox();
    const b = { x: measured.x, y: measured.y, width: measured.width, height: measured.height };
    // Side by side counts as in the way unless there's a little space between, so neighbours' names don't run together.
    const beside = r => r.x < b.x + b.width + 4 && b.x < r.x + r.width + 4;
    const inWay = among => among.find(r => beside(r) && r.y < b.y + b.height && b.y < r.y + r.height);
    // Moving up, only what's over or under them can be in the way.
    const column = others.filter(beside);
    for (let tries = 0; tries < others.length; tries++) {
        const hit = inWay(column);
        if (!hit) break;
        const up = b.y + b.height - hit.y + 2;
        t.setAttribute("y", Number(t.getAttribute("y")) - up);
        b.y -= up;
    }
    // Never above the paper; with no room above, beside the name in the way instead.
    if (b.y < 1) {
        t.setAttribute("y", Number(t.getAttribute("y")) - b.y + 1);
        b.y = 1;
    }
    for (let tries = 0; tries < others.length; tries++) {
        const hit = inWay(tries === 0 ? column : others);
        if (!hit) break;
        const along = hit.x + hit.width + 4 - b.x;
        t.setAttribute("x", Number(t.getAttribute("x")) + along);
        b.x += along;
    }
    t.box = b;
    return t;
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
