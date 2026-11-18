// The engraver for whole pieces: Verovio in a worker of its own, started by Sheet.razor.js, which says why. Laying a long piece
// out takes seconds on the tablet, and here it holds nothing up: the page goes on answering taps and the way back. The sheet
// asks for a step at a time ({ id, op, args }), each answered in turn ({ id, result } or { id, error }): the music laid out,
// one of its pages drawn, its time map, the page a note is on. Ended in the middle of a step, the worker simply goes, and the
// sheet starts another.
importScripts("verovio/verovio-toolkit-wasm.js");

let tk = null;
const asked = [];

// What Verovio puts in every page it draws besides the music: the text faces (380 KB), which the page has already, or is given
// once; and a few rules for the drawing, by its id, which every page of a piece shares, so the first page's hold for them all.
// A style sheet put on the page has the browser go over everything drawn there again: a scroll's parts after the first leave
// theirs off, or each would cost more than the last.
const textFaces = /@font-face\s*\{[^}]*font-family:\s*"Liberation"[^}]*\}/g;
const styleSheets = /<style[^>]*>[\s\S]*?<\/style>/g;

const steps = {
    // Lays the music out with these options, only some of its bars when `measures` says which ("6-9"). Returns its pages (a
    // scroll's parts) and how many lines of music they hold in all.
    load(mei, options, measures) {
        tk.setOptions(options);
        if (!tk.loadData(mei)) throw new Error("Verovio couldn't read the music.");
        if (measures) {
            tk.select({ measureRange: measures });
            tk.redoLayout();
        }
        // Only some of its bars shown, Verovio gives no account of its pages to count them in.
        return { pages: tk.getPageCount(), lines: measures ? 0 : lines() };
    },
    // A page as it's drawn, keeping of its style sheets "all", only the drawing's own "rules" (not the text faces), or "none".
    page(n, keep) {
        const svg = tk.renderToSVG(n);
        return keep === "all" ? svg : keep === "rules" ? svg.replace(textFaces, "") : svg.replace(styleSheets, "");
    },
    timemap() {
        return tk.renderToTimemap({ includeMeasures: true });
    },
    pageOf(id) {
        return tk.getPageWithElement(id);
    },
};

// The lines of music in the piece as it's laid out: its systems, counted in Verovio's own account of its pages (a few ms, where
// drawing them all to count them is the whole wait).
function lines() {
    try {
        return (tk.getMEI({ scoreBased: false }).match(/<system[\s>]/g) ?? []).length;
    } catch {
        return 0;
    }
}

function answer({ id, op, args }) {
    try {
        postMessage({ id, result: steps[op](...args) });
    } catch (error) {
        postMessage({ id, error: String(error?.message ?? error) });
    }
}

// Each step is done as its message comes, one after another; what's asked before Verovio is ready waits for it.
onmessage = event => {
    if (tk) answer(event.data);
    else asked.push(event.data);
};

function start() {
    tk = new verovio.toolkit();
    postMessage({ ready: true });
    for (const step of asked.splice(0)) answer(step);
}

if (verovio.module.calledRun) start();
else verovio.module.onRuntimeInitialized = start;
