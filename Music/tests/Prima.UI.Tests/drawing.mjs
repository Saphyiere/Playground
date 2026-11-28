// Verovio's drawing of a piece for two hands, as the boxes a browser gives its parts, for the tests of what measures it
// (bar-box.js, bars.js, Sheet.razor.js), and the few selectors they ask it. Each line of music is a g.system holding its bars;
// each bar a g.measure with its id, holding its two staves (g.staff: five lines, a path each, and the notes, which reach over
// and under them) and its bar line. A mark that runs on over bar lines (an 8va, a slur, a pedal line…) is drawn inside the bar
// it starts in, as Verovio draws it, so that bar's box runs on with it; carried on to the next line, it's drawn by that line.
// Not a test file: the tests import it.

export const box = (left, top, right, bottom) => ({ left, top, right, bottom, width: right - left, height: bottom - top, x: left, y: top });

export class Shape {
    constructor(classes, { rect = null, tag = "g", id = null, children = [] } = {}) {
        Object.assign(this, { classes: classes.split(" ").filter(Boolean), rect, tagName: tag, children: [], parentElement: null, dataset: id ? { id } : {} });
        for (const child of children) this.append(child);
    }
    append(child) {
        child.parentElement = this;
        this.children.push(child);
        return child;
    }
    getAttribute(name) { return name === "class" ? this.classes.join(" ") : null; }
    get classList() { return { contains: c => this.classes.includes(c) }; }
    // A group's box is its parts' together, as the browser's is.
    getBoundingClientRect() {
        const parts = this.rect ? [this.rect] : this.children.map(c => c.getBoundingClientRect()).filter(r => r.width > 0 || r.height > 0);
        if (parts.length === 0) return box(0, 0, 0, 0);
        return box(Math.min(...parts.map(r => r.left)), Math.min(...parts.map(r => r.top)), Math.max(...parts.map(r => r.right)), Math.max(...parts.map(r => r.bottom)));
    }
    *all() {
        for (const c of this.children) {
            yield c;
            yield* c.all();
        }
    }
    querySelectorAll(selector) { return [...this.all()].filter(e => matches(e, selector, this)); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
    getElementsByClassName(name) { return this.querySelectorAll(`.${name}`); }
    closest(selector) {
        for (let e = this; e; e = e.parentElement) if (matches(e, selector, null)) return e;
        return null;
    }
}

// A selector of the kinds the sheet's code asks: tags, classes and [attributes] joined by " " and ">", from :scope, a list by ",".
function matches(e, selector, scope) {
    return selector.split(",").some(one => {
        const parts = one.trim().replace(/\s*>\s*/g, " > ").split(/\s+/);
        return matchFrom(e, parts, parts.length - 1, scope);
    });
}

function matchFrom(e, parts, i, scope) {
    if (!e || !simple(e, parts[i], scope)) return false;
    if (i === 0) return true;
    if (parts[i - 1] === ">") return matchFrom(e.parentElement, parts, i - 2, scope);
    for (let p = e.parentElement; p; p = p.parentElement) if (matchFrom(p, parts, i - 1, scope)) return true;
    return false;
}

function simple(e, part, scope) {
    if (part === ":scope") return e === scope;
    const [, tag, classes, attributes] = part.match(/^([a-z]*)((?:\.[\w-]+)*)((?:\[[^\]]+\])*)$/i) ?? [];
    if (tag === undefined) throw new Error(`the stand-in drawing doesn't know the selector ${part}`);
    if (tag && e.tagName !== tag) return false;
    if (!classes.split(".").filter(Boolean).every(c => e.classes.includes(c))) return false;
    return [...attributes.matchAll(/\[([\w-]+)(?:="([^"]*)")?\]/g)].every(([, name, value]) => {
        const has = name === "data-id" ? e.dataset.id : undefined;
        return value === undefined ? has != null : has === value;
    });
}

// Bars 120 px wide side by side from x 40, `perLine` bars a line, numbered from `first`. Each bar's staves: the top one's lines
// from y 100 to 141 and its notes up to 85, the lower one's lines from 200 to 241 and its notes down to 252; each line `lineGap`
// px under the one before. Marks: [kind, first bar, last bar, top, bottom], from their first bar's first note to their last bar's
// last. The sheet's corner is the page's, so its px are the page's.
export const barWidth = 120, lineStart = 40;

export function drawing({ first = 19, count = 8, perLine = 8, lineGap = 300, marks = [] } = {}) {
    const svg = new Shape("definition-scale", { tag: "svg" });
    const sheet = new Shape("sheet", { tag: "div", rect: box(0, 0, 1200, Math.ceil(count / perLine) * lineGap), children: [new Shape("sheet-music", { tag: "div", children: [svg] })] });
    const frame = new Shape("piece-frame", { tag: "div", rect: box(0, 0, 1200, 800), children: [sheet] });
    const systems = [], measures = [];
    const place = n => {
        const i = n - first, line = Math.floor(i / perLine), along = i % perLine;
        return { line, left: lineStart + along * barWidth, right: lineStart + (along + 1) * barWidth, y: line * lineGap };
    };
    for (let n = first; n < first + count; n++) {
        const { line, left, right, y } = place(n);
        systems[line] ??= svg.append(new Shape("system"));
        const staff = (top, notesTop, notesBottom) => new Shape("staff", {
            children: [
                ...[0, 1, 2, 3, 4].map(k => new Shape("", { tag: "path", rect: box(left, y + top + k * 10, right, y + top + k * 10 + 1) })),
                new Shape("layer", { children: [new Shape("note", { id: `note-${n}-${top}`, rect: box(left + 20, y + notesTop, left + 34, y + notesBottom) })] }),
            ],
        });
        // A line's first bar has its number printed over it, standing a little out to the left of the staff.
        const number = left === lineStart ? [new Shape("mNum autogenerated", { children: [new Shape("", { tag: "text", rect: box(left - 6, y + 70, left + 4, y + 82) })] })] : [];
        measures.push(systems[line].append(new Shape("measure", {
            id: `m${n}`,
            children: [...number, staff(100, 85, 141), staff(200, 199, 252), new Shape("barLine", { children: [new Shape("", { tag: "path", rect: box(right - 2, y + 100, right, y + 241) })] })],
        })));
    }
    const lineEnd = lineStart + perLine * barWidth;
    for (const [kind, from, to, top, bottom] of marks) {
        const a = place(from), b = place(to);
        const mark = (left, right, y, ...more) => new Shape([kind, ...more].join(" "), { children: [new Shape("", { tag: "path", rect: box(left, y + top, right, y + bottom) })] });
        measures[from - first].append(mark(a.left + 20, a.line === b.line ? b.right - 12 : lineEnd, a.y));
        for (let l = a.line + 1; l <= b.line; l++) systems[l].append(mark(lineStart, l === b.line ? b.right - 12 : lineEnd, l * lineGap, "spanning"));
    }
    // Where a finger goes on bar n: its own lines' left, right and middle, the gap between its staves, its top staff's middle;
    // and where the bar starts, its number taken in.
    const at = n => {
        const p = place(n);
        return { ...p, start: p.left === lineStart ? p.left - 6 : p.left, middle: (p.left + p.right) / 2, gap: p.y + 170, staff: p.y + 120 };
    };
    return { frame, sheet, svg, systems, measures, ids: measures.map(m => m.dataset.id), at };
}
