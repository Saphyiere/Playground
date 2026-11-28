// The sheet: Verovio engraves the MEI as real printed music, black on paper, and an overlay on top draws the marks, found by
// each note's id. The cursor is a line of its own over both, moved by a transform: on a long piece, anything redrawn on the
// music's own layer has the browser go over the whole piece again, which a cursor moving at every frame can't afford. The
// engraving itself is never changed. Sizes: a printed piano staff is about
// 7 mm tall, and the 13-inch tablet's long side is about 280 mm, so a millimetre is the screen's long side in CSS pixels / 280.
//
// A passage ("line") is engraved here, on the page's own thread, as it's asked for: it's a few bars, and drills time answers
// from when it's painted. A whole piece (a "scroll", or its "page"s) is engraved by a Verovio in a worker of its own
// (engraver.js), and a scroll a part at a time: a long piece takes seconds to lay out on the tablet, and the page must go on
// answering meanwhile. "Whole pieces", below, has how.

import { barBox } from "../bar-box.js";

const svgNs = "http://www.w3.org/2000/svg";
const sheets = new WeakMap();
const renderRequests = new WeakMap();
const renderOwners = new Map();
let sheetCount = 0;
let toolkitPromise;

// Verovio's toolkit, loaded once: a 7 MB script with WebAssembly inside. Called at start so the first sheet doesn't wait, nor
// the first piece for its engraver.
export function warm() {
    toolkit();
    pieceEngraver();
}

function toolkit() {
    toolkitPromise ??= new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "_content/Prima.UI/verovio/verovio-toolkit-wasm.js";
        script.onload = () => {
            const ready = () => resolve(withTextFont(new verovio.toolkit()));
            if (verovio.module.calledRun) ready();
            else verovio.module.onRuntimeInitialized = ready;
        };
        script.onerror = reject;
        document.head.appendChild(script);
    });
    return toolkitPromise;
}

// The words in the music (a song's lyrics, directions, the tempo, fingering) are set in Liberation Serif, the face Verovio measures
// them by and carries with it, so they're drawn as wide as it spaced them for. Left to the device's own serif they weren't: Android
// has no Times, and its serif is wider and taller, so a song's syllables ran into each other. Verovio puts the face's four styles
// in every drawing (380 KB): they're taken from a first small one and given to the page once, and left out of each drawing after.
let textFont = false;
async function withTextFont(tk) {
    try {
        tk.setOptions({ fontTextLiberation: true });
        tk.loadData('<mei xmlns="http://www.music-encoding.org/ns/mei" meiversion="5.0"><music><body><mdiv><score><scoreDef meter.count="4" meter.unit="4"><staffGrp><staffDef n="1" lines="5" clef.shape="G" clef.line="2"/></staffGrp></scoreDef><section><measure n="1"><staff n="1"><layer n="1"><note pname="c" oct="4" dur="1"><verse n="1"><syl>a</syl></verse></note></layer></staff><dir staff="1" tstamp="1">a <rend fontweight="bold">b</rend></dir><tempo staff="1" tstamp="1">a</tempo></measure></section></score></mdiv></body></music></mei>');
        const faces = [...tk.renderToSVG(1).matchAll(/@font-face\s*\{([^}]*)\}/g)].map(m => m[1]).filter(face => /font-family:\s*"Liberation"/.test(face));
        const added = faces.map(face => {
            const data = atob(face.match(/base64,([A-Za-z0-9+/=]+)/)[1]);
            const bytes = Uint8Array.from(data, c => c.charCodeAt(0));
            const font = new FontFace("Liberation", bytes, { weight: face.match(/font-weight:\s*(\w+)/)?.[1] ?? "normal", style: face.match(/font-style:\s*(\w+)/)?.[1] ?? "normal" });
            document.fonts.add(font);
            return font.load();
        });
        await Promise.all(added);
        textFont = added.length === 4;
    } catch {
        // Then each drawing keeps the faces Verovio puts in it.
    }
    tk.resetOptions();
    return tk;
}

// A drawing without the text faces Verovio put in it, once the page has them.
function withoutTextFaces(svg) {
    return textFont ? svg.replace(/@font-face\s*\{[^}]*font-family:\s*"Liberation"[^}]*\}/g, "") : svg;
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
    if (!engraver.loadData(mei)) throw new Error("Verovio couldn't read the reading.");
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
        const r = barBox(m) ?? m.getBoundingClientRect();
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
// piece down one sheet; "page": one page the sheet's size), staffMm, font, page, header, and words (the music has a song's words
// to print: .NET has given each a line of its own, SongWords). Redraws by itself when the sheet's width changes, and tells .NET
// when it has painted. A passage is drawn by the time this returns; a whole piece is being drawn, and .NET hears how far it
// has got (Progress) and when it's all there (Painted).
export async function render(element, mei, options, dotnet, ownerId) {
    if (!element?.isConnected) return;
    if (ownerId) renderOwners.set(ownerId, element);
    const request = {};
    renderRequests.set(element, request);
    const tk = await toolkit();
    if (!element.isConnected || renderRequests.get(element) !== request) return;
    const state = sheets.get(element) ?? { number: ++sheetCount, moves: [], kept: new Map() };
    // Marks belong to the music they were made on: new music starts clean (a passage's note ids can be the last one's), and so
    // do the drawings kept of it, the size its words were fitted at, and the notes' names.
    if (state.mei !== mei) Object.assign(state, { marks: null, music: mei, kept: new Map(), fitted: null, timemap: null, timed: null, names: null });
    Object.assign(state, { mei, options, dotnet, tk, overlay: element.querySelector(".sheet-overlay"), line: [...element.children].find(c => c.classList.contains("sheet-cursor")) });
    sheets.set(element, state);
    element.classList.toggle("sheet-explain", !!options.explain);
    draw(element, state);
    if (!state.observer) {
        state.observer = new ResizeObserver(() => {
            // Hidden (a screen over the music, as the look back is): nothing to draw for nobody to see. When it shows again at the
            // size it had, there's nothing to draw then either.
            if (element.clientWidth === 0) return;
            if (Math.abs(element.clientWidth - state.width) > 1 || (state.options.layout === "page" && Math.abs(element.clientHeight - state.height) > 1)) draw(element, state);
        });
        state.observer.observe(element);
        element.addEventListener("click", event => {
            if (state.options.explain) {
                explainAt(element, state, event.clientX, event.clientY);
                return;
            }
            const note = event.target.closest?.(".sheet-music .note[data-id]");
            if (note) state.dotnet?.invokeMethodAsync("NoteTapped", note.dataset.id);
            // The bar tapped. A scroll is never a click, so it's never a tap.
            const measure = barTapped(element, event.target, event.clientX, event.clientY);
            if (measure) state.dotnet?.invokeMethodAsync("BarTapped", measure.dataset.id);
        });
    }
}

// A song's words: their size and the room between them (Verovio's lyricSize and lyricWordSpace, in half staff-spaces), tried in
// turn until no line's words run together. First a little smaller than Verovio prints lyrics by itself (4.5 is a printed song's
// 11 point on the 7 mm staff, and with a busy tune the words outweigh the notes and spread them): about the 9.5 point a book is set
// in, with Verovio's own room, so a song whose words fit is drawn as it always was. Then two and three times the room between
// words: Verovio fills each line to the brim with bars, and justifying a line a little too full squeezes its words together, at
// any size (a smaller size only lets more bars onto the line), where the room keeps them apart and makes the bars wider, fewer a
// line. Only then smaller words, for a bar wider than the line on its own (a busy song, the tablet upright): small words are read
// more easily than words run together. Verovio sets all of a piece's words at the lyrics' size (its tempo, directions, the
// part's name), so a song's go with its lyrics. Prima's own words under notes (a note's name), and everything else's, keep
// Verovio's.
const songFits = [[3.8, 1.2], [3.8, 2.4], [3.8, 3.6], [3.3, 2.4], [2.9, 2.4], [2.5, 2.4]];
const ownFit = [4.5, 1.2];

// A scroll is drawn in parts, each a page of this many of Verovio's units (some twenty staves' height: two or three lines of
// piano music), one under the other. Verovio breaks the lines the same whatever the page's height, and spaces the lines on a
// page the same too, so the parts make the drawing one tall page would, without the wait for all of it. Only where two parts
// meet is the space this sheet's to leave: between two lines on one page Verovio leaves its system spacing (4 of its staff
// units, 36 here) and the room each line's own notes and marks need above and below, and a part's page is exactly as high as
// its lines and that room (its margins are left off; the paper's are put round the whole). So the seam is the 36, and the
// little a page's height is rounded down by (under one unit, half of that on average).
const partHeight = 1500;
const seam = 36.4;

// The paper the sheet's music is laid out on, at the sheet's size as it is now, in Verovio's units: its options, and for a
// scroll the margins left over its first part and under its last (Verovio's own top and bottom ones, on any other layout).
function paperOf(element, state) {
    const { options } = state;
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
    const scroll = options.layout === "scroll";
    const [top, bottom] = [options.layout === "page" || fingered ? 40 : 24, 24];
    const paper = {
        font: options.font,
        scale: 100,
        pageWidth: Math.max(600, Math.round(state.width * unitsPerPx)),
        pageHeight: options.layout === "page" ? Math.max(600, Math.round(state.height * unitsPerPx)) : scroll ? partHeight : 60000,
        adjustPageHeight: options.layout !== "page",
        breaks: "auto",
        minLastJustification: options.layout === "line" ? 0 : 0.8,
        header: options.header ? "auto" : "none",
        footer: "none",
        pageMarginLeft: joined ? 50 : 24,
        pageMarginRight: 24,
        pageMarginTop: scroll ? 0 : top,
        pageMarginBottom: scroll ? 0 : bottom,
        svgViewBox: true,
        svgHtml5: true,
        fontTextLiberation: true,
    };
    return { paper, top, bottom };
}

// Draws the sheet's music at the sheet's size as it is now: a passage at once, a whole piece as it comes from its engraver.
function draw(element, state) {
    if (state.options.layout === "line") drawPassage(element, state);
    else drawPiece(element, state);
}

function drawPassage(element, state) {
    const { tk, options } = state;
    const music = element.querySelector(".sheet-music");
    // A whole piece's drawing, if this sheet showed one before, is let go of.
    endRun(state);
    shown(element, null);
    const { paper } = paperOf(element, state);

    // A song's words at the first fit whose lines hold them apart, starting from the one that did on this paper before: with
    // the passage's bars shared evenly first, then its lines broken where their bars fit, then the next fit.
    const kind = JSON.stringify([paper, options.measures ?? null]);
    let fit = state.fitted?.kind === kind ? state.fitted.fit : 0;
    let even = true;
    let turnedOnly = true;
    for (;;) {
        const [lyricSize, lyricWordSpace] = options.words ? songFits[fit] : ownFit;
        const { shared, turned } = lay(element, state, { ...paper, lyricSize, lyricWordSpace }, even);
        turnedOnly &&= turned;
        if (!options.words || !crowded(music)) break;
        if (shared) even = false;
        else if (fit < songFits.length - 1) [fit, even] = [fit + 1, true];
        else break;
    }
    state.fitted = { kind, fit };
    // When each moment of the music is, as laid out last (its time doesn't change with the layout): a page turned keeps its own.
    if (!turnedOnly) state.timemap = tk.renderToTimemap({ includeMeasures: true });
    finish(element, state);
}

// Lays the music out with these options and draws the page showing. Returns whether a passage's bars were shared evenly between
// its lines (only when `even` allows it), and whether the layout was the one the toolkit had, only another page of it drawn.
function lay(element, state, loaded, even) {
    const { tk, options } = state;
    const music = element.querySelector(".sheet-music");
    const previous = state.loaded;
    state.loaded = loaded;
    // Another page of music laid out already (a page turn): only that page is drawn, the toolkit keeping the layout it has.
    // Anything else (other music or bars, another size or option, or the toolkit given another sheet's) is laid out afresh.
    const laid = tk.current === state.number && state.laidOut === state.music && state.laidBars === options.measures;
    const turned = laid && JSON.stringify(previous) === JSON.stringify(loaded);
    if (!turned) {
        tk.setOptions(loaded);
        load(tk, state, state.music);
        state.pages = tk.getPageCount();
    }
    [state.laidOut, state.laidBars] = [state.music, options.measures];
    music.innerHTML = withoutTextFaces(tk.renderToSVG(Math.min(options.page ?? 1, state.pages)));
    // A passage's bars are shared evenly between its systems, so the last one isn't left with a bar on its own. Not a whole
    // piece's ("scroll"): its bars differ too much for a line of each to hold as many, and the full ones would be squeezed. Its
    // lines break where the bars fit, and a last line that's short keeps its bars' own widths.
    const systems = music.querySelectorAll(".system");
    const bars = music.querySelectorAll(".measure").length;
    let shared = false;
    if (even && options.layout === "line" && systems.length > 1 && systems[systems.length - 1].querySelectorAll(".measure").length < Math.ceil(bars / systems.length) - 1) {
        tk.setOptions({ breaks: "encoded" });
        load(tk, state, withSystemBreaks(state.music, Math.ceil(bars / systems.length), firstMeasure(options)));
        state.pages = tk.getPageCount();
        music.innerHTML = withoutTextFaces(tk.renderToSVG(1));
        state.laidOut = null;
        shared = true;
    }
    return { shared, turned };
}

// Whether a song's words run into each other anywhere on the page: on a line of them (the syllables sharing a baseline), one
// starting before the one before it has ended and left a pixel's room. Verovio leaves a word's space between words and a
// hyphen's between syllables, so touching means the line was squeezed. Measured as drawn, in the face they're set in; each line
// is one verse on one system.
function crowded(music) {
    const lines = new Map();
    for (const syllable of music.querySelectorAll(".syl text")) {
        const r = syllable.getBoundingClientRect();
        if (r.width === 0) continue;
        const line = Math.round(r.bottom);
        if (!lines.has(line)) lines.set(line, []);
        lines.get(line).push(r);
    }
    for (const line of lines.values()) {
        line.sort((a, b) => a.left - b.left);
        for (let i = 1; i < line.length; i++) if (line[i].left < line[i - 1].right + 1) return true;
    }
    return false;
}

// New paper, drawn or put back: what was drawn over the old one goes, and so does what was found on it (each id's place, the
// music's words); the marks go on again, the notes' names and What's this's marks as each line comes into view, and .NET
// hears once it's painted.
function finish(element, state) {
    fresh(state);
    if (state.marks) showMarks(element, state.marks);
    watchLines(element, state);
    painted(element, state);
}

// New paper: nothing drawn over the old one is left, nor anything found on it.
function fresh(state) {
    state.overlay.replaceChildren();
    state.line.hidden = true;
    state.lineWatch?.disconnect();
    Object.assign(state, { points: null, byId: null, words: null, drawn: new Map(), lineWatch: null, named: new WeakSet(), outlined: new WeakSet() });
}

// .NET hears that the music is drawn once the browser has painted it.
function painted(element, state) {
    requestAnimationFrame(() => requestAnimationFrame(() => {
        if (element.isConnected && sheets.get(element) === state) state.dotnet?.invokeMethodAsync("Painted", state.pages);
    }));
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

// ——— Whole pieces ———
//
// A whole piece is laid out and drawn by a Verovio of its own, in a worker (engraver.js). Laying a long piece out is one step
// that can't be broken up, seconds long on the tablet: on the page's own thread nothing could answer meanwhile, not a tap, not
// the way back. From the worker, nothing is asked of the page until there's music to put on it. A scroll comes a part at a
// time, so its first lines show as soon as they're drawn; the rest follows, and can be stopped. Whatever ends a drawing (the
// sheet gone, another size, other music) lets go of the engraver at once: in the middle of laying a piece out, the worker is
// ended and another started.
//
// Should the worker not start, the same steps are taken on the page's thread, a breath between them, and the problem log says
// so (Settings › Problems).

// The engraver: its worker (none: the page's own thread does its steps), the steps asked and not yet answered, the one in hand,
// whose drawing has it, and the layout it holds.
const engine = { worker: null, calls: new Map(), count: 0, doing: null, holder: null, laid: null, tail: Promise.resolve(), own: null, up: null };
let engraverReady;

// What a drawing stops on when its step was given up.
const letGo = new Error("The drawing was let go of.");

// The engraver, started once and ready: in its worker, or on the page's thread if that couldn't be had.
function pieceEngraver() {
    engraverReady ??= new Promise(resolve => {
        engine.up = resolve;
        startWorker();
    }).then(() => engine);
    return engraverReady;
}

function startWorker() {
    let worker = null;
    // The worker can't be had (or has gone wrong): the steps it was asked fail, and from here on the page's thread takes them.
    const broken = why => {
        if (engine.worker !== worker) return;
        clearTimeout(late);
        worker?.terminate();
        engine.worker = engine.doing = engine.laid = null;
        const stopped = new Error(`The sheet's engraver stopped (${why}).`);
        for (const call of [...engine.calls.values()]) call.reject(stopped);
        engine.calls.clear();
        console.error(`The sheet's engraver couldn't run in a worker (${why}). Whole pieces are engraved on the page's own thread from here on, which can't answer taps while it lays one out.`);
        engine.up?.();
    };
    // Not ready in half a minute, it isn't going to be.
    const late = setTimeout(() => broken("it didn't start within 30 seconds"), 30000);
    try {
        worker = engine.worker = new Worker("_content/Prima.UI/engraver.js");
    } catch (error) {
        broken(error?.message ?? String(error));
        return;
    }
    worker.onerror = event => {
        event.preventDefault?.();
        broken(event.message || "its script couldn't be loaded");
    };
    worker.onmessage = ({ data }) => {
        if (data.ready) {
            clearTimeout(late);
            engine.up?.();
            return;
        }
        const call = engine.calls.get(data.id);
        if (!call) return;
        engine.calls.delete(data.id);
        if (engine.calls.size === 0) engine.doing = null;
        if ("error" in data) call.reject(new Error(data.error));
        else call.resolve(data.result);
    };
}

// A step asked of the engraver: "load" (the music, the layout's options, the bars shown: its pages and lines), "page" (a
// page's drawing, keeping of its style sheets "all", only the drawing's own "rules", or "none"), "timemap", "pageOf" (a
// note's page). Answered in the order asked.
function ask(op, ...args) {
    if (!engine.worker) return onOwnThread(op, args);
    return new Promise((resolve, reject) => {
        const id = ++engine.count;
        engine.calls.set(id, { resolve, reject });
        engine.doing = op;
        engine.worker.postMessage({ id, op, args });
    });
}

// The same steps on the page's own thread (engraver.js has them for the worker), where the worker can't be had: with a
// toolkit of the sheet's own, and a breath before each for a tap or the way back to be taken.
const pageSteps = {
    load(tk, mei, options, measures) {
        tk.setOptions(options);
        if (!tk.loadData(mei)) throw new Error("Verovio couldn't read the music.");
        if (measures) {
            tk.select({ measureRange: measures });
            tk.redoLayout();
        }
        let lines = 0;
        try {
            if (!measures) lines = (tk.getMEI({ scoreBased: false }).match(/<system[\s>]/g) ?? []).length;
        } catch {
            // Then the lines aren't counted.
        }
        return { pages: tk.getPageCount(), lines };
    },
    page(tk, n, keep) {
        const svg = tk.renderToSVG(n);
        return keep === "all" ? svg : keep === "rules" ? withoutTextFaces(svg) : svg.replace(/<style[^>]*>[\s\S]*?<\/style>/g, "");
    },
    timemap: tk => tk.renderToTimemap({ includeMeasures: true }),
    pageOf: (tk, id) => tk.getPageWithElement(id),
};

async function onOwnThread(op, args) {
    await new Promise(resolve => setTimeout(resolve));
    await toolkit();
    engine.own ??= new verovio.toolkit();
    return pageSteps[op](engine.own, ...args);
}

// One drawing at a time has the engraver, for all its steps: it holds one piece's layout. Returns what gives it up.
async function takeEngraver(holder) {
    const before = engine.tail;
    let free;
    engine.tail = new Promise(resolve => free = resolve);
    await before;
    engine.holder = holder;
    return () => {
        if (engine.holder === holder) engine.holder = null;
        free();
    };
}

// A sheet's drawing ended from outside (the sheet gone, drawn afresh, or showing a passage now): it stops at its next step,
// and a long step the engraver has in hand for it (laying the piece out, or its time map) is given up at once, the worker
// ended and another started, ready for the next piece.
function endRun(state) {
    const run = state.run;
    state.run = null;
    if (!run || engine.holder !== run || !engine.worker || (engine.doing !== "load" && engine.doing !== "timemap")) return;
    const [worker, calls] = [engine.worker, [...engine.calls.values()]];
    worker.onerror = worker.onmessage = null;
    worker.terminate();
    engine.calls.clear();
    engine.doing = engine.laid = null;
    startWorker();
    for (const call of calls) call.reject(letGo);
}

// The music laid out by the engraver with these options, unless that's the layout it holds: its pages and its lines.
async function laidOut(state, loaded) {
    const key = JSON.stringify([loaded, state.options.measures ?? null]);
    const has = engine.laid;
    if (has?.number === state.number && has.music === state.music && has.key === key) return has;
    engine.laid = null;
    const { pages, lines } = await ask("load", state.music, loaded, state.options.measures ?? null);
    return engine.laid = { number: state.number, music: state.music, key, pages, lines };
}

// What the paper shows of a whole piece, for the stylesheet (data-drawn): nothing yet when it's not there (the paper waits,
// with a mark that moves), "part" with more coming under it, "stopped" part-way, or "all".
function shown(element, how) {
    if (how) element.setAttribute("data-drawn", how);
    else element.removeAttribute("data-drawn");
}

// .NET hears how far a whole piece's drawing has got: the lines drawn of the lines in all, and how it stands ("coming",
// "done", "stopped" or "failed"). As lines come, a few times a second at most.
function tell(state, how) {
    const now = performance.now();
    if (!how && now - (state.told ?? 0) < 250) return;
    state.told = now;
    Promise.resolve(state.dotnet?.invokeMethodAsync("Progress", state.drawnLines ?? 0, state.lines ?? 0, how ?? "coming")).catch(() => { });
}

// Whether the music is a song: words under its notes all through, a syllable or more to a bar on the whole. (A sonata whose
// file has a few "pp" entered as lyrics isn't.)
function wordy(mei) {
    const count = what => {
        let n = 0;
        for (let at = mei.indexOf(what); at >= 0; at = mei.indexOf(what, at + what.length)) n++;
        return n;
    };
    return count("<syl") >= count("<measure");
}

// Draws a whole piece at the sheet's size as it is now, ending any drawing of it still going.
function drawPiece(element, state) {
    endRun(state);
    const run = state.run = { stopped: false };
    drawWhole(element, state, run).catch(error => failed(element, state, run, error));
}

// A drawing that couldn't be done: said (and so kept in the problem log), the paper left with what it has, and .NET told.
function failed(element, state, run, error) {
    if (state.run !== run || error === letGo) return;
    console.error(`The sheet couldn't draw its music: ${error?.stack ?? error}`);
    element.querySelector(".sheet-music").style.visibility = "";
    shown(element, "stopped");
    tell(state, "failed");
}

async function drawWhole(element, state, run) {
    const live = () => state.run === run && element.isConnected;
    const { options } = state;
    const music = element.querySelector(".sheet-music");
    const scroll = options.layout === "scroll";
    // Where a scroll drawn already is in its frame: it's put back there, by its bar, in the new drawing. A drawing that
    // was still to be put back somewhere when this one was asked for leaves this one the same place.
    state.place = !scroll ? null : state.place ?? (music.childElementCount > 0 ? placeIn(element, state) : null);
    const { paper, top, bottom } = paperOf(element, state);

    // The whole piece at a size it was drawn at before (out of full screen, or the tablet turned back): the drawing kept then
    // goes back on the paper, rather than the piece being laid out and drawn again, which on a long piece takes seconds. The
    // last three sizes' drawings are kept.
    state.key = scroll ? JSON.stringify([paper, options.measures ?? null, options.page ?? 1, !!options.words]) : null;
    const kept = state.key ? state.kept.get(state.key) : null;
    if (kept) {
        state.kept.delete(state.key);
        state.kept.set(state.key, kept);
        music.replaceChildren(...kept.drawing);
        music.style.visibility = "";
        music.querySelectorAll("[class*='mark-']").forEach(n => n.classList.remove(...markKinds));
        Object.assign(state, { pages: 1, loaded: kept.loaded, parts: kept.parts, drawnParts: kept.parts, lines: kept.lines, drawnLines: kept.lines, margins: kept.margins, shownWidth: state.width });
        shown(element, "all");
        backInPlace(state, music, true);
        finish(element, state);
        tell(state, "done");
        return;
    }

    // The paper waiting: nothing on it, nor over it. What was drawn before stays until the new drawing's first lines are
    // there, or a moment has passed without them: a short piece is simply drawn afresh, a long one's paper waits.
    const wait = () => {
        music.replaceChildren();
        music.style.visibility = "";
        state.glyphs = new Set();
        fresh(state);
        shown(element, null);
    };
    let old = 0;
    if (music.childElementCount > 0) old = setTimeout(() => live() && wait(), 300);
    else wait();
    const begun = () => {
        clearTimeout(old);
        wait();
        state.shownWidth = state.width;
    };

    await pieceEngraver();
    if (!live()) return clearTimeout(old);
    const release = await takeEngraver(run);
    try {
        if (!live()) return;
        // A song's words at the first fit whose lines hold them apart, starting from the one that did on this paper before (a
        // page turned, say). A song is shown once all of it is drawn with its words apart, as it always was: a fit that crowds
        // a line is never seen. Anything else is shown as its lines come.
        const kind = JSON.stringify([paper, options.measures ?? null]);
        const song = options.words && wordy(state.music);
        for (let fit = state.fitted?.kind === kind ? state.fitted.fit : 0; ; fit++) {
            const [lyricSize, lyricWordSpace] = options.words ? songFits[fit] : ownFit;
            const loaded = { ...paper, lyricSize, lyricWordSpace };
            const { pages, lines } = await laidOut(state, loaded);
            if (!live()) return;
            state.loaded = loaded;
            state.fitted = { kind, fit };
            // Whether the words still running together would have another fit tried.
            const check = options.words && fit < songFits.length - 1;
            if (scroll) {
                Object.assign(state, { pages: 1, parts: pages, lines, drawnParts: 0, drawnLines: 0, margins: { top, bottom, width: paper.pageWidth } });
                const went = await drawParts(element, state, run, song, check, begun);
                if (went === "crowded") {
                    wait();
                    continue;
                }
                if (went !== "gone") await ended(element, state, run, went);
                return;
            }

            // One page of it: the one showing.
            const svg = await ask("page", Math.min(options.page ?? 1, pages), textFont ? "rules" : "all");
            if (!live()) return;
            clearTimeout(old);
            // A song's page isn't seen until its words are apart on it: the paper waits meanwhile.
            if (check) {
                music.style.visibility = "hidden";
                shown(element, null);
            }
            music.innerHTML = svg;
            if (check && crowded(music)) continue;
            music.style.visibility = "";
            Object.assign(state, { pages, parts: pages, drawnParts: pages, lines, drawnLines: lines });
            shown(element, "all");
            finish(element, state);
            tell(state, "done");
            await timed(state, live);
            return;
        }
    } finally {
        clearTimeout(old);
        release();
    }
}

// Draws the scroll's parts that aren't drawn yet, each put under the last as it comes from the engraver, which is drawing the
// next meanwhile. `hidden` keeps them out of sight (a song, until it's all there); with `check`, a part where a song's words
// run together ends it, for another fit to be tried. Returns how it went: "done", "stopped" (Stop: what's drawn stays),
// "crowded", or "gone" (the drawing was let go of).
async function drawParts(element, state, run, hidden, check, begun) {
    const music = element.querySelector(".sheet-music");
    const part = n => {
        if (n > state.parts) return null;
        // The first part brings the drawing's rules for them all (and the text faces, if the page hasn't them): a style sheet
        // with every part would have the browser go over all the parts before it again.
        const drawn = ask("page", n, n > 1 ? "none" : textFont ? "rules" : "all");
        // Let go of before it's looked at, it has nobody to tell.
        drawn.catch(() => { });
        return drawn;
    };
    let coming = part(state.drawnParts + 1);
    for (let n = state.drawnParts + 1; n <= state.parts; n++) {
        let svg;
        try {
            svg = await coming;
        } catch (error) {
            if (state.run !== run) return "gone";
            throw error;
        }
        if (state.run !== run || !element.isConnected) return "gone";
        if (run.stopped) return "stopped";
        coming = part(n + 1);
        if (n === 1) {
            begun();
            if (hidden) music.style.visibility = "hidden";
        }
        const added = addPart(state, music, svg, n);
        if (check && crowded(added)) return "crowded";
        backInPlace(state, added, n === state.parts);
        state.drawnParts = n;
        state.drawnLines += added.querySelectorAll(".system").length;
        if (hidden) continue;
        shown(element, n < state.parts ? "part" : "all");
        tell(state, n === 1 ? "coming" : null);
    }
    return "done";
}

// A part put on the paper under those before it, with the space Verovio would have left between their lines (`seam`), or
// the paper's margin over the first and under the last: in the paper's units as a share of its width, which is what a
// margin's % is of, so it keeps to the music's size.
function addPart(state, music, svg, n) {
    music.insertAdjacentHTML("beforeend", withNewGlyphs(state, svg));
    const part = music.lastElementChild;
    const { top, bottom, width } = state.margins;
    part.style.marginTop = `${(n === 1 ? top : seam) / width * 100}%`;
    if (n === state.parts) part.style.marginBottom = `${bottom / width * 100}%`;
    // What was found on the paper holds for the parts before, which don't move: the new part's notes join them, and the
    // words in a mark's way and the cursor's places are found again when next needed.
    if (state.byId) {
        for (const printed of part.querySelectorAll("[data-id]")) if (!state.byId.has(printed.dataset.id)) state.byId.set(printed.dataset.id, printed);
    }
    state.words = null;
    state.points = null;
    // Its lines named and marked as they come into view, where the sheet has names or What's this on.
    if (state.names || state.options.explain) watchLines(state.overlay.closest(".sheet"), state, part);
    return part;
}

// Where the music is in the frame it scrolls in (a piece's), for a drawing at another size to be put back there: the first
// line in view, by its first bar, and how far its top is from the frame's. Nothing at the piece's top, which stays the top.
// The sheet has its new width by now, and the drawing on it is stretched or shrunk to it under a frame that hasn't moved
// (the stylesheet has the browser leave the frame's place alone), so where each line was is worked back from the width it
// was drawn at; at the piece's very end, where the frame has had to move, the lines are taken as they stand. Not moved since
// it was last put back, it's the same bar again, so going in and out of full screen doesn't creep. Moved by hand before it's
// back there, the music stays where it's been put.
function placeIn(element, state) {
    const frame = element.closest("[data-follow]");
    if (!frame || frame.scrollTop <= 0) return null;
    const put = state.put;
    if (put && Math.abs(frame.scrollTop - put.top) < 2) return watched(state, { frame, id: put.id, offset: put.offset, system: null });
    const [sheet, top] = [element.getBoundingClientRect().top, frame.getBoundingClientRect().top];
    const atEnd = frame.scrollTop + frame.clientHeight >= frame.scrollHeight - 2;
    const scaled = !atEnd && state.shownWidth > 0 ? element.clientWidth / state.shownWidth : 1;
    const was = y => sheet - top + (y - sheet) / scaled;
    for (const system of element.querySelectorAll(".sheet-music .system")) {
        const box = system.getBoundingClientRect();
        if (was(box.bottom) <= 1) continue;
        const id = system.querySelector(".measure[data-id]")?.dataset.id;
        return id ? watched(state, { frame, id, offset: was(box.top), system: null }) : null;
    }
    return null;
}

function watched(state, place) {
    const moved = () => { if (state.place === place) state.place = null; };
    place.frame.addEventListener("pointerdown", moved, { once: true, passive: true });
    place.frame.addEventListener("wheel", moved, { once: true, passive: true });
    return place;
}

// The music back where it was (placeIn), once the line that was first in view is drawn again (looked for in what's just
// been `drawn`) and there's enough under it for the frame to scroll that far; with `all` of it drawn, as near as it goes.
function backInPlace(state, drawn, all) {
    const place = state.place;
    if (!place) return;
    place.system ??= drawn.querySelector(`.measure[data-id="${CSS.escape(place.id)}"]`)?.closest(".system") ?? null;
    if (place.system) {
        const { frame } = place;
        const to = frame.scrollTop + place.system.getBoundingClientRect().top - frame.getBoundingClientRect().top - place.offset;
        frame.scrollTop = to;
        if (Math.abs(frame.scrollTop - to) < 2 || all) {
            state.put = { id: place.id, offset: place.offset, top: frame.scrollTop };
            state.place = null;
        }
    }
    if (all) state.place = null;
}

// A part with only the glyphs the paper hasn't yet. Every page Verovio draws carries the outlines of the glyphs on it, under
// ids the pieces' pages share, and its notes are uses of them. An outline put on the page again has the browser make every use
// of it on the parts before all over again, so each part would cost more than the last: one outline of each is enough, where
// it first comes.
function withNewGlyphs(state, svg) {
    const [start, end] = [svg.indexOf("<defs>"), svg.indexOf("</defs>")];
    if (start < 0 || end < start) return svg;
    const outlines = svg.slice(start, end).replace(/<g id="([^"]+)">[\s\S]*?<\/g>\s*/g, (outline, id) => state.glyphs.has(id) ? "" : (state.glyphs.add(id), outline));
    return svg.slice(0, start) + outlines + svg.slice(end);
}

// A scroll's drawing over: stopped part-way, or all there. All there, its marks go on, .NET hears it's painted, a cursor
// already moving is given the whole of its way, and the drawing is kept for this size once its time map has come.
async function ended(element, state, run, went) {
    const music = element.querySelector(".sheet-music");
    music.style.visibility = "";
    state.place = null;
    if (went === "stopped") {
        shown(element, "stopped");
        tell(state, "stopped");
        painted(element, state);
        return;
    }
    shown(element, "all");
    if (state.marks) showMarks(element, state.marks);
    tell(state, "done");
    painted(element, state);
    cursorAgain(element, state);
    if (!await timed(state, () => state.run === run && element.isConnected)) return;
    state.kept.set(state.key, { drawing: [...music.childNodes], loaded: state.loaded, parts: state.parts, lines: state.lines, margins: state.margins });
    if (state.kept.size > 3) state.kept.delete(state.kept.keys().next().value);
}

// When each moment of the music is, from the engraver, unless the sheet has it for this music already: its time doesn't change
// with the layout. Whether the drawing is still the sheet's once it has come.
async function timed(state, live) {
    const of = state.options.measures ?? "";
    if (state.timemap && state.timed === of) return true;
    const map = await ask("timemap");
    if (!live()) return false;
    Object.assign(state, { timemap: map, timed: of, points: null });
    return true;
}

// Stop: no more of a scroll is drawn than is there already.
export function stop(element) {
    const run = sheets.get(element)?.run;
    if (run) run.stopped = true;
}

// The rest of a scroll that was stopped part-way.
export function rest(element) {
    const state = sheets.get(element);
    if (!state || element.getAttribute("data-drawn") !== "stopped" || !(state.drawnParts < state.parts)) return;
    endRun(state);
    const run = state.run = { stopped: false };
    const live = () => state.run === run && element.isConnected;
    (async () => {
        await pieceEngraver();
        if (!live()) return;
        const release = await takeEngraver(run);
        try {
            if (!live()) return;
            await laidOut(state, state.loaded);
            if (!live()) return;
            shown(element, "part");
            tell(state, "coming");
            const went = await drawParts(element, state, run, false, state.options.words && state.fitted.fit < songFits.length - 1, () => { });
            // Words run together further on: the whole of it, at the next fit.
            if (went === "crowded") {
                state.fitted = { ...state.fitted, fit: state.fitted.fit + 1 };
                drawPiece(element, state);
            }
            else if (went !== "gone") await ended(element, state, run, went);
        } finally {
            release();
        }
    })().catch(error => failed(element, state, run, error));
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

// The page a note is on (1 for the first), or 0 if it isn't in the music. A whole piece's engraver is asked, once it has
// the piece laid out as the sheet shows it.
export async function pageOf(element, id) {
    const state = sheets.get(element);
    if (!state) return 0;
    if (state.options.layout === "line") {
        ensureLoaded(state);
        return state.tk.getPageWithElement(id);
    }
    if (!state.loaded) return 0;
    await pieceEngraver();
    const asking = {};
    const release = await takeEngraver(asking);
    try {
        if (sheets.get(element) !== state) return 0;
        await laidOut(state, state.loaded);
        return await ask("pageOf", id);
    } catch {
        return 0;
    } finally {
        release();
    }
}

// Which of these notes are on the page showing.
export function onPage(element, ids) {
    const state = sheets.get(element);
    if (!state) return [];
    const byId = printedById(element, state);
    return ids.filter(id => byId.has(id));
}

// The bar whose space a point is in: between its bar lines, and from a little over its top staff to a little under its lowest
// (bar-box.js), the nearest of two lines' if both reach it. Given a line's `system`, only across: its bar under the point.
function measureAt(element, x, y, system = null) {
    let at, nearest = Infinity;
    for (const m of system ? system.querySelectorAll(".measure[data-id]") : element.querySelectorAll(".sheet-music .measure[data-id]")) {
        const r = barBox(m);
        if (!r || x < r.left || x > r.right) continue;
        const away = system ? 0 : Math.max(r.top - y, y - r.bottom, 0);
        if (away <= 20 && away < nearest) [at, nearest] = [m, away];
    }
    return at;
}

// The bar a tap at x, y on `target` chooses. On a note, or anything else printed in a staff, that staff's bar. On a mark printed
// with a line of music (an octave line, a slur, a hairpin, a pedal line, words), the bar on that line under it: a mark that runs
// on over bar lines is drawn with the bar it starts in, and would choose that bar wherever along it it was tapped. On the paper,
// the bar whose space it's in.
function barTapped(element, target, x, y) {
    const staff = target.closest?.(".sheet-music .measure[data-id] > .staff");
    if (staff) return staff.parentElement;
    const system = target.closest?.(".sheet-music .system");
    return (system ? measureAt(element, x, y, system) : null) ?? measureAt(element, x, y) ?? target.closest?.(".sheet-music .measure[data-id]") ?? null;
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
        for (const entry of state.timemap ?? []) {
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
    // A whole piece's time map comes after its drawing: until it has, there are no bar ends to find.
    if (!state.timemap) return points;
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
        // The bar is as wide as its staff's lines (bar-box.js: a slur or a pedal mark drawn with it can run on past them). A bar
        // line in its right half closes it; one before its music (a repeat's start) is passed before the music starts, as the
        // clef and signatures a line opens with are.
        const m = barBox(place.measure) ?? place.measure.getBoundingClientRect();
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
// Not `shown`, it isn't drawn, but the line being played is still kept in view. A guide only: nothing is scored from it. Given
// `beats`, for music whose tempo changes ([a beat's quarter, ms after that click `msUntilBeat` from now] for each of the run's
// beats, and the last one's end), a moment is due by those, in place of every quarter taking `msPerQuarter`.
//
// Its whole way is given to the browser once, as an animation the browser runs by itself: along a system from each moment's
// place to the next, and at a system's end (or where a repeat or a loop goes back) waiting there until the next moment and then
// at its place at once. Placed from here at every frame instead, each move has the browser go over the whole piece again, and
// on a long piece that leaves it no time for the keys being played.
export function cursorStart(element, msUntilStart, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat, path, shown = true, beats = null) {
    const state = sheets.get(element);
    if (!state) return;
    cursorStop(element);
    // Kept with when it was asked for: a scroll still being drawn gives the cursor the whole of its way once it's all there.
    state.cursor = { at: performance.now(), args: [msUntilStart, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat, path, shown, beats] };
    const points = path ? pathPoints(element, state, path) : positions(element, state);
    state.points = points;
    if (points.length === 0) return;
    const bars = windowQuarters ? measures(element, state) : null;
    const line = state.line;
    line.hidden = !shown;
    const now = performance.now();
    const start = now + msUntilStart;
    // When a moment of the music is due, on the page's clock, which is the animations' too; and the moment due at a time.
    const zero = now + msUntilBeat;
    const due = beats ? q => zero + along(beats, q, 0, 1) : q => start + (q - startQuarter) * msPerQuarter;
    const moment = beats ? t => along(beats, t - zero, 1, 0) : t => startQuarter + (t - start) / msPerQuarter;
    const opens = due(Math.min(points[0].q, startQuarter));
    const lastBar = path?.bars.at(-1);
    const first = due(path?.bars[0]?.[1] ?? points[0].q);
    const last = due(lastBar ? lastBar[1] + lastBar[2] : points[points.length - 1].q);
    // Keep one cycle in the browser. A seek can have a short first stretch before the
    // selection wraps back to its beginning; it has its own animation and count-in.
    const split = path?.introBars ? path.bars[path.introBars][1] : null;
    const entry = split == null ? [] : points.filter(p => p.q < split || (p.q === split && p.end));
    const cycle = split == null ? points : points.filter(p => p.q > split || (p.q === split && !p.end));
    const segments = [];
    if (entry.length) segments.push({ points: entry, start: first, end: due(split), iterations: 1 });
    if (cycle.length) segments.push({ points: cycle, start: split == null ? first : due(split), end: last, iterations: path?.iterations ?? 1 });
    line.style.opacity = "";
    line.style.transform = placed(points[0]);
    for (const segment of segments) {
        const duration = segment.end - segment.start;
        if (!shown || duration <= 0) continue;
        const way = [];
        segment.points.forEach((here, i) => {
            const offset = Math.max(0, Math.min(1, (due(here.q) - segment.start) / duration));
            const before = segment.points[i - 1];
            if (before && (before.end || before.system !== here.system)) way.push({ offset, transform: placed(before) });
            way.push({ offset, transform: placed(here) });
        });
        const move = line.animate(way, { duration, iterations: segment.iterations, fill: segment === segments[0] ? "both" : "forwards" });
        move.startTime = segment.start;
        state.moves.push(move);
    }
    if (shown && beatMs > 0 && opens > now) {
        // The count-in: bright on each click, fading towards the next, from the click before now until the music opens.
        const click = now + (msUntilBeat ?? msUntilStart);
        const from = click - Math.ceil((click - now) / beatMs) * beatMs;
        const pulse = line.animate([{ opacity: 1 }, { opacity: 0.3 }], { duration: beatMs, iterations: (opens - from) / beatMs, easing: "cubic-bezier(0.5, 1, 0.89, 1)" });
        pulse.startTime = from;
        state.moves.push(pulse);
    }
    // The line being played is kept in view: at once the first (or the one the music has reached, for a cursor given its way
    // again part-way), and each one after when its first moment comes. Nothing else is done from here while the cursor moves,
    // so the page is free for the keys.
    const lines = [];
    let previous = points[0].system;
    for (const segment of segments) {
        const changes = segment.points.filter((p,i) => i === 0 || p.system !== segment.points[i - 1].system);
        for (let pass = 0; pass < segment.iterations; pass++) {
            for (const change of changes) {
                if (previous !== change.system) lines.push({ at: due(change.q) + pass * (segment.end - segment.start), system: change.system });
                previous = change.system;
            }
        }
    }
    const reached = () => lines.findLastIndex(p => p.at <= performance.now() + 1);
    const begun = reached();
    follow(element, (begun >= 0 ? lines.splice(0, begun + 1)[begun] : points[0]).system);
    const onward = () => {
        const next = reached();
        if (next >= 0) follow(element, lines.splice(0, next + 1)[next].system);
        if (lines.length > 0) state.timer = setTimeout(onward, lines[0].at - performance.now());
    };
    if (lines.length > 0) state.timer = setTimeout(onward, lines[0].at - performance.now());
    // Read ahead's covers move with the music at every frame.
    if (!bars) return;
    const frame = now => {
        const q = moment(now);
        for (const bar of bars) bar.cover.style.display = bar.end <= q - 0.05 || bar.start > q + windowQuarters ? "" : "none";
        state.raf = requestAnimationFrame(frame);
    };
    state.raf = requestAnimationFrame(frame);
}

// Along a line through points ([x, y] pairs, in order of both), the y at an x (`from` and `to` say which of the pair is which),
// carried on as the first or last stretch goes before and after them.
function along(points, x, from, to) {
    let i = 1, high = points.length - 1;
    while (i < high) {
        const mid = Math.floor((i + high) / 2);
        if (points[mid][from] < x) i = mid + 1;
        else high = mid;
    }
    const [a, b] = [points[i - 1], points[i]];
    return b[from] === a[from] ? a[to] : a[to] + (x - a[from]) * (b[to] - a[to]) / (b[from] - a[from]);
}

// A cursor that's moving, given its way again as the paper stands now (all of a scroll there, where only its first lines
// were when it started), its times as they were asked for.
function cursorAgain(element, state) {
    if (!state.cursor) return;
    const passed = performance.now() - state.cursor.at;
    const [msUntilStart, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat, path, shown, beats] = state.cursor.args;
    cursorStart(element, msUntilStart - passed, msPerQuarter, startQuarter, windowQuarters, beatMs, msUntilBeat == null ? msUntilBeat : msUntilBeat - passed, path, shown, beats);
}

// The cursor at a place: a line a pixel high at the sheet's corner, moved there and stretched to its system's height, so its
// place and its height are one transform the browser can animate by itself.
function placed(point) {
    return `translate(${point.x}px, ${point.top}px) scaleY(${point.bottom - point.top})`;
}

// Wait for me's cursor: still, just before a note (the first of the notes waited for), its system kept in view.
export function cursorAt(element, id) {
    const state = sheets.get(element);
    const printed = state && printedById(element, state).get(id);
    const head = printed?.querySelector(".notehead") ?? printed;
    if (!head) return;
    cancelAnimationFrame(state.raf);
    clearTimeout(state.timer);
    for (const move of state.moves.splice(0)) move.cancel();
    state.cursor = null;
    const box = element.getBoundingClientRect();
    const system = head.closest(".system");
    const s = system.getBoundingClientRect();
    state.line.hidden = false;
    state.line.style.opacity = "";
    state.line.style.transform = placed({ x: head.getBoundingClientRect().left - box.left - 6, top: s.top - box.top - 6, bottom: s.bottom - box.top + 6 });
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
    clearTimeout(state.timer);
    for (const move of state.moves.splice(0)) move.cancel();
    state.cursor = null;
    state.line.hidden = true;
    state.overlay.querySelectorAll(".sheet-cover").forEach(c => c.remove());
}

// Read ahead: each bar with its span in quarters and a cover the colour of the paper, drawn over it when it's hidden.
function measures(element, state) {
    if (!state.timemap) return [];
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

// ——— What's this, and the notes' names ———
//
// What's this makes a tap on the music say what's printed there, and marks what there is to tap. A tap counts for anything within
// a finger's reach of it (its box at least 44 px each way, round its middle: a dot is 3 px on the tablet, a slur 2 px thick), and
// the nearest wins; .NET hears the few nearest, to ask which one was meant where they're as near. The marks and the notes' names
// are drawn a line of music at a time as it comes into view, on a layer of their own under the run's marks: a long piece has
// thousands of notes, and only a few lines show at once.

// The kinds of symbol a tap can name, the innermost first (an accidental before its note, a note before its beam).
const symbols = ["accid", "artic", "dots", "stem", "flag", "fing", "dynam", "hairpin", "dir", "tempo", "fermata", "trill", "mordent", "turn",
    "arpeg", "gliss", "bTrem", "fTrem", "breath", "caesura", "slur", "phrase", "tie", "pedal", "octave", "ending", "repeatMark", "tuplet",
    "note", "rest", "mRest", "multiRest", "chord", "beam", "ledgerLines", "clef", "keySig", "meterSig", "barLine", "mNum", "grpSym", "reh",
    "harm"];
// What a finger can tap: a chord is its notes, and a stem its note's.
const tappable = symbols.filter(s => s !== "chord" && s !== "stem").map(s => `.${s}`).join(", ");
// What's marked to tap while What's this is on: the signs, not what there are hundreds of (notes, rests, beams, fingers, a note's
// accidental, dots and articulations), nor the curves, whose boxes cover the notes under them.
const outlinedKinds = ["clef", "keySig", "meterSig", "dynam", "hairpin", "dir", "tempo", "fermata", "trill", "mordent", "turn", "arpeg", "gliss",
    "breath", "caesura", "pedal", "octave", "ending", "repeatMark", "tuplet", "harm", "reh"];
const outlined = outlinedKinds.map(s => `.${s}`).join(", ");
// Half a finger's width: a tap counts for what's printed this far from it.
const reach = 22;
// A long or wide sign's box takes in more than the sign (a beam's or a slur's covers the notes under it, a hairpin's the space
// inside it): it counts as this much farther than its box says, so the note or the dot the finger is on comes first.
const wide = { beam: 8, slur: 10, phrase: 10, tie: 8, hairpin: 6, ledgerLines: 6, barLine: 4, ending: 8, octave: 6, pedal: 4, tuplet: 4, mNum: 4 };
// The glyphs that tell a clef's shape: SMuFL's G, F and C clefs.
const clefGlyphs = { E050: "G", E052: "G", E062: "F", E064: "F", E05C: "C" };

export function explain(element, on) {
    const state = sheets.get(element);
    if (!state) return;
    state.options.explain = on;
    element.classList.toggle("sheet-explain", on);
    // On, the lines in view are marked as they come; off, the marks go.
    state.overlay.querySelector(".sheet-explainable")?.remove();
    state.outlined = new WeakSet();
    if (on) watchLines(element, state);
}

// The notes' names, from .NET: [id, name] for each note to name, or none.
export function names(element, labels) {
    const state = sheets.get(element);
    if (!state) return;
    state.names = labels?.length ? new Map(labels) : null;
    state.named = new WeakSet();
    state.overlay.querySelector(".sheet-names")?.remove();
    watchLines(element, state);
}

// Watches the lines of music (all of them, or those in a part just added) for coming into view, to name their notes and, with
// What's this on, mark what there is to tap: a little before they're in view, so they're there as they scroll in.
function watchLines(element, state, within = element) {
    if (!state.names && !state.options?.explain) return;
    state.lineWatch ??= new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting || sheets.get(element) !== state) continue;
            if (state.names) nameLine(element, state, entry.target);
            if (state.options.explain) outlineLine(element, state, entry.target);
        }
    }, { rootMargin: "600px 0px" });
    for (const system of within.querySelectorAll(".sheet-music .system")) {
        // Observed again, it's told again whether it's in view, which draws what it lacks.
        state.lineWatch.unobserve(system);
        state.lineWatch.observe(system);
    }
}

// The overlay's own group for something drawn a line at a time: under the run's marks, which go on after it.
function layerOf(state, name) {
    let layer = state.overlay.querySelector(`:scope > .${name}`);
    if (!layer) {
        layer = document.createElementNS(svgNs, "g");
        layer.setAttribute("class", name);
        state.overlay.prepend(layer);
    }
    return layer;
}

// A staff's top and bottom lines, in px on the screen, from its five line paths.
function staffEdges(staff) {
    const lines = [...(staff?.querySelectorAll(":scope > path") ?? [])].map(p => p.getBoundingClientRect());
    if (lines.length < 2) return null;
    const tops = lines.map(l => (l.top + l.bottom) / 2);
    return { top: Math.min(...tops), bottom: Math.max(...tops), space: (Math.max(...tops) - Math.min(...tops)) / (lines.length - 1) };
}

// A line's notes named: over a staff that's the top of its line (the right hand's), under any other (the left hand's), as
// fingering is printed, so the busy space between the staves is left alone. Each name goes over its note, or over its staff when
// the note is in it, a chord's names stacked as its notes are (its lowest nearest the staff), moved out past the music's own
// words (fingering, dynamics, directions) and the names beside it.
function nameLine(element, state, system) {
    if (state.named.has(system) || !system.isConnected) return;
    state.named.add(system);
    const sheet = element.getBoundingClientRect();
    const layer = layerOf(state, "sheet-names");
    const own = [...system.querySelectorAll(".dir, .tempo, .dynam, .fing, .syl text")].map(e => e.getBoundingClientRect()).filter(r => r.width > 0)
        .map(r => ({ x: r.left - sheet.left, y: r.top - sheet.top, width: r.width, height: r.height }));
    const placed = [];
    // Each note to name, by what it's struck with: a chord's notes together.
    const groups = new Map();
    for (const note of system.querySelectorAll(".note[data-id]")) {
        const name = state.names.get(note.dataset.id);
        const head = name && note.querySelector(".notehead");
        if (!head) continue;
        const holder = note.closest(".chord") ?? note;
        if (!groups.has(holder)) groups.set(holder, []);
        groups.get(holder).push({ name, r: head.getBoundingClientRect() });
    }
    const edges = new Map();
    for (const [holder, notes] of groups) {
        const staff = holder.closest(".staff");
        if (!edges.has(staff)) edges.set(staff, staffEdges(staff));
        const edge = edges.get(staff);
        if (!edge) continue;
        const under = [...staff.parentElement.querySelectorAll(":scope > .staff")].indexOf(staff) > 0;
        const size = Math.max(10, Math.round(edge.space * 1.5));
        const gap = size * 0.25;
        // Nearest the staff first: a chord's lowest note over it, its highest under it.
        notes.sort((a, b) => under ? a.r.top - b.r.top : b.r.top - a.r.top);
        const x = notes.reduce((sum, n) => sum + n.r.left + n.r.width / 2, 0) / notes.length - sheet.left;
        // Over the staff (or the note, where it's higher), its baseline a gap above; under it, its top a gap below.
        let y = under ? Math.max(edge.bottom, ...notes.map(n => n.r.bottom)) + gap + size * 0.8 : Math.min(edge.top, ...notes.map(n => n.r.top)) - gap;
        y -= sheet.top;
        for (const { name } of notes) {
            const t = document.createElementNS(svgNs, "text");
            t.setAttribute("class", "sheet-name");
            t.setAttribute("x", x);
            t.setAttribute("y", y);
            t.style.fontSize = `${size}px`;
            t.textContent = name;
            layer.appendChild(t);
            const m = t.getBBox();
            const b = { x: m.x, y: m.y, width: m.width, height: m.height };
            // Out past anything in the way, a pixel or two clear of it.
            const clash = () => [...own, ...placed].find(r => r.x < b.x + b.width + 2 && b.x < r.x + r.width + 2 && r.y < b.y + b.height && b.y < r.y + r.height);
            for (let tries = 0, hit = clash(); hit && tries < 12; tries++, hit = clash()) {
                const by = under ? hit.y + hit.height + 1 - b.y : -(b.y + b.height + 1 - hit.y);
                b.y += by;
                t.setAttribute("y", Number(t.getAttribute("y")) + by);
            }
            t.box = b;
            placed.push(b);
            y = under ? b.y + b.height + size * 0.8 + 1 : b.y - 1;
        }
    }
}

// A line's signs marked, for What's this: a faint outline round each, its box at least a finger wide, as it can be tapped.
function outlineLine(element, state, system) {
    if (state.outlined.has(system) || !system.isConnected) return;
    state.outlined.add(system);
    const sheet = element.getBoundingClientRect();
    const layer = layerOf(state, "sheet-explainable");
    for (const symbol of system.querySelectorAll(outlined)) {
        // A sign inside another marked one (an accidental in a key signature) is part of it.
        if (symbol.parentElement?.closest(outlined)) continue;
        const r = symbol.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        const [w, h] = [Math.max(r.width + 8, 28), Math.max(r.height + 8, 28)];
        const box = document.createElementNS(svgNs, "rect");
        box.setAttribute("x", r.left + r.width / 2 - w / 2 - sheet.left);
        box.setAttribute("y", r.top + r.height / 2 - h / 2 - sheet.top);
        box.setAttribute("width", w);
        box.setAttribute("height", h);
        box.setAttribute("rx", 7);
        layer.appendChild(box);
    }
}

// The kind a symbol is, as the glossary knows it: Verovio's class, and what its drawing shows of a clef's shape, a time
// signature's figures or a repeat's dots.
function kindOf(symbol) {
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
    return kind;
}

// A tap on the music with What's this on: what's printed within a finger's reach of it, the nearest first, of the line tapped
// (only its symbols are measured: a long piece has tens of thousands). .NET hears the nearest few, each with how far it is from
// the finger (0 on it) and where it is on the screen, for its card to stand beside it.
function explainAt(element, state, x, y) {
    const lines = [...element.querySelectorAll(".sheet-music .system")].map(system => ({ system, r: system.getBoundingClientRect() }));
    const line = lines.filter(l => y >= l.r.top - reach && y <= l.r.bottom + reach).sort((a, b) => Math.abs(y - (a.r.top + a.r.bottom) / 2) - Math.abs(y - (b.r.top + b.r.bottom) / 2))[0];
    if (!line) return;
    const near = [];
    for (const symbol of line.system.querySelectorAll(tappable)) {
        const kind = symbols.find(s => symbol.classList.contains(s));
        // A note by its head, so a tap on the stem or the beam beside it isn't taken for it.
        const r = (kind === "note" ? symbol.querySelector(".notehead") ?? symbol : symbol).getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        const [cx, cy] = [(r.left + r.right) / 2, (r.top + r.bottom) / 2];
        if (Math.abs(x - cx) > Math.max(r.width / 2, reach) || Math.abs(y - cy) > Math.max(r.height / 2, reach)) continue;
        near.push({ symbol, r, away: Math.hypot(Math.max(r.left - x, 0, x - r.right), Math.max(r.top - y, 0, y - r.bottom)) + (wide[kind] ?? 0) });
    }
    near.sort((a, b) => a.away - b.away);
    const found = near.slice(0, 5).map(({ symbol, r, away }) => ({
        id: symbol.dataset.id ?? "",
        kind: kindOf(symbol),
        within: symbol.parentElement?.closest(".sheet-music .note[data-id], .sheet-music .chord[data-id], .sheet-music .measure[data-id]")?.dataset.id ?? null,
        away,
        left: r.left, top: r.top, right: r.right, bottom: r.bottom,
    }));
    state.dotnet?.invokeMethodAsync("SymbolsTapped", found);
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

// Marks notes during a run, as each is judged: a note marked again (a slip put right) loses its earlier mark. Its colour goes on
// the marks' layer over the note, not on the note: changing the music had the browser paint all of it again for each note
// played, which on a long piece was most of what the page did while you played.
export function markLive(element, marks) {
    const state = sheets.get(element);
    if (!state) return;
    for (const mark of marks) drawMark(element, state, mark, true);
}

const markKinds = ["mark-right", "mark-wrong", "mark-missed", "mark-early", "mark-late", "mark-apart", "mark-heat1", "mark-heat2", "mark-heat3", "mark-picked"];

function drawMark(element, state, mark, live = false) {
    const note = printedById(element, state).get(mark.id);
    if (!note) return;
    // What's drawn over the paper for this note (its colour while playing, the key played, its name, an arrow, a word), kept so
    // it can be taken off again.
    const mine = state.drawn.get(mark.id) ?? [];
    state.drawn.set(mark.id, mine);
    // An extra key is drawn beside the note before it, which keeps its own mark, as a note with a word over it does.
    if (mark.kind !== "extra" && mark.kind !== "hint") {
        if (!live) note.classList.remove(...markKinds);
        mine.splice(0).forEach(n => n.remove());
        if (live) mine.push(...tint(state, note, mark.kind));
        else note.classList.add(`mark-${mark.kind}`);
    }
    // A note with nothing to draw beside it is its colour and no more: nothing on the paper is measured for it.
    const beside = mark.kind === "hint" || mark.kind === "early" || mark.kind === "late" || mark.q != null || ((mark.kind === "wrong" || mark.kind === "extra") && mark.played);
    if (!beside) return;
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

// A note's colour for a mark while playing, on the marks' layer: what the mark colours (the whole note when it's right or wrong,
// its head otherwise) drawn again there in its colour, each shape worked out where it lies on the layer. Shapes moved into place
// by a transform would each have the browser sort out the page's layers again, which costs as much as painting the music did.
function tint(state, note, kind) {
    const toLayer = state.overlay.getScreenCTM().inverse();
    const group = document.createElementNS(svgNs, "g");
    group.setAttribute("class", `sheet-tint tint-${kind}`);
    const parts = kind === "right" || kind === "wrong" ? [note] : [...note.querySelectorAll(".notehead")];
    for (const shape of parts.flatMap(part => [...part.querySelectorAll("use, path, ellipse")])) {
        const at = toLayer.multiply(shape.getScreenCTM());
        const scale = Math.sqrt(Math.abs(at.a * at.d - at.b * at.c));
        if (shape.localName === "ellipse") {
            const e = group.appendChild(document.createElementNS(svgNs, "ellipse"));
            const [cx, cy] = [shape.cx.baseVal.value, shape.cy.baseVal.value];
            e.setAttribute("cx", at.a * cx + at.c * cy + at.e);
            e.setAttribute("cy", at.b * cx + at.d * cy + at.f);
            e.setAttribute("rx", shape.rx.baseVal.value * Math.abs(at.a));
            e.setAttribute("ry", shape.ry.baseVal.value * Math.abs(at.d));
            continue;
        }
        // A glyph is drawn from the music's own outlines (a use of one, by its id); a line (a stem) is stroked.
        const glyph = shape.localName === "use" ? document.getElementById((shape.getAttribute("href") ?? shape.getAttribute("xlink:href") ?? "").slice(1)) : null;
        for (const path of glyph ? [...glyph.querySelectorAll("path")] : shape.localName === "path" ? [shape] : []) {
            const own = glyph ? path.transform.baseVal.consolidate()?.matrix : null;
            const p = group.appendChild(document.createElementNS(svgNs, "path"));
            p.setAttribute("d", moved(path.getAttribute("d") ?? "", own ? at.multiply(own) : at));
            if (glyph) p.style.strokeWidth = `${28 * scale}px`;
            else Object.assign(p.style, { fill: "none", stroke: "currentColor", strokeWidth: `${(parseFloat(path.getAttribute("stroke-width")) || 1) * scale}px` });
        }
    }
    return [state.overlay.appendChild(group)];
}

// An outline's path data moved by a matrix: each point worked out afresh, every command made absolute (M, L, C, Q, Z).
function moved(d, m) {
    const tokens = d.match(/[a-df-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?/gi) ?? [];
    const out = [];
    const to = (x, y) => `${(m.a * x + m.c * y + m.e).toFixed(2)} ${(m.b * x + m.d * y + m.f).toFixed(2)}`;
    let [i, command, x, y, startX, startY, before] = [0, "M", 0, 0, 0, 0, null];
    const next = () => parseFloat(tokens[i++]);
    while (i < tokens.length) {
        if (/[a-z]/i.test(tokens[i])) command = tokens[i++];
        const kind = command.toUpperCase();
        const [ox, oy] = command === kind ? [0, 0] : [x, y];
        // The control point a smooth curve mirrors: the last curve's second, or the point itself.
        const mirrored = before ? [2 * x - before[0], 2 * y - before[1]] : [x, y];
        let control = null;
        if (kind === "M") {
            [x, y] = [ox + next(), oy + next()];
            [startX, startY] = [x, y];
            out.push(`M${to(x, y)}`);
            command = command === kind ? "L" : "l";
        } else if (kind === "L") {
            [x, y] = [ox + next(), oy + next()];
            out.push(`L${to(x, y)}`);
        } else if (kind === "H") {
            x = ox + next();
            out.push(`L${to(x, y)}`);
        } else if (kind === "V") {
            y = oy + next();
            out.push(`L${to(x, y)}`);
        } else if (kind === "C" || kind === "S") {
            const first = kind === "C" ? [ox + next(), oy + next()] : mirrored;
            control = [ox + next(), oy + next()];
            [x, y] = [ox + next(), oy + next()];
            out.push(`C${to(...first)} ${to(...control)} ${to(x, y)}`);
        } else if (kind === "Q" || kind === "T") {
            control = kind === "Q" ? [ox + next(), oy + next()] : mirrored;
            [x, y] = [ox + next(), oy + next()];
            out.push(`Q${to(...control)} ${to(x, y)}`);
        } else if (kind === "Z") {
            [x, y] = [startX, startY];
            out.push("Z");
        } else {
            // An arc isn't in a music font's outlines: what's moved so far is kept.
            break;
        }
        before = control;
    }
    return out.join("");
}

// An oval the size of a note head, leaning as one does, at (cx, cy): two arcs that lean by their own angle, since a shape turned
// by a transform has the browser sort out the page's layers again.
function ghost(id, cx, cy, headWidth, space, kind = "sheet-ghost") {
    const g = document.createElementNS(svgNs, "path");
    g.setAttribute("class", kind);
    g.dataset.for = id;
    const [rx, ry, lean] = [headWidth * 0.5, space * 0.42, -20];
    const [dx, dy] = [rx * Math.cos(lean * Math.PI / 180), rx * Math.sin(lean * Math.PI / 180)];
    g.setAttribute("d", `M${cx - dx} ${cy - dy}A${rx} ${ry} ${lean} 1 0 ${cx + dx} ${cy + dy}A${rx} ${ry} ${lean} 1 0 ${cx - dx} ${cy - dy}Z`);
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

// A bar outlined on the marks' layer, by its id, and its line brought into view; with no id, the outline goes.
export function outline(element, id) {
    const state = sheets.get(element);
    if (!state) return;
    state.overlay.querySelector(".sheet-outline")?.remove();
    const measure = id ? element.querySelector(`.sheet-music .measure[data-id="${id}"]`) : null;
    if (!measure) return;
    const box = element.getBoundingClientRect();
    const r = barBox(measure) ?? measure.getBoundingClientRect();
    const rect = document.createElementNS(svgNs, "rect");
    rect.setAttribute("class", "sheet-outline");
    rect.setAttribute("x", r.left - box.left - 4);
    rect.setAttribute("y", r.top - box.top - 10);
    rect.setAttribute("width", r.right - r.left + 8);
    rect.setAttribute("height", r.bottom - r.top + 20);
    rect.setAttribute("rx", 6);
    state.overlay.appendChild(rect);
    follow(element, measure.closest(".system"));
}

export function clearMarks(element) {
    const state = sheets.get(element);
    if (!state) return;
    state.marks = null;
    clearDrawnMarks(element, state);
}

function clearDrawnMarks(element, state) {
    element.querySelectorAll(".sheet-music [class*='mark-']").forEach(n => n.classList.remove(...markKinds));
    state.overlay.querySelectorAll(".sheet-tint, .sheet-ghost, .sheet-label, .sheet-arrow, .sheet-hint").forEach(n => n.remove());
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
    // In the way: the marks' words already there, and the music's own (a figure's named first note, a dynamic, the fingering, a
    // song's words over a lower staff), which are found once for the music as it's drawn.
    if (!state.words) {
        const sheet = element.getBoundingClientRect();
        state.words = [...element.querySelectorAll(".sheet-music .dir, .sheet-music .tempo, .sheet-music .dynam, .sheet-music .fing, .sheet-music .syl text")].map(e => e.getBoundingClientRect()).map(r => ({ x: r.left - sheet.left, y: r.top - sheet.top, width: r.width, height: r.height }));
    }
    // The notes' names too: a wrong key's name goes over them.
    const others = [...[...overlay.querySelectorAll(".sheet-label, .sheet-arrow, .sheet-hint, .sheet-name")].filter(o => o !== t && o.box).map(o => o.box), ...state.words];
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

export function dispose(element, ownerId) {
    element ??= renderOwners.get(ownerId);
    if (ownerId) renderOwners.delete(ownerId);
    if (!element) return;
    renderRequests.delete(element);
    const state = sheets.get(element);
    if (!state) return;
    state.dotnet = null;
    // A whole piece still being drawn is let go of: its engraver is free for the next one at once.
    endRun(state);
    cancelAnimationFrame(state.raf);
    clearTimeout(state.timer);
    state.observer?.disconnect();
    sheets.delete(element);
}
