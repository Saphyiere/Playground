// Choosing where a piece plays. .NET keeps the choice (the bar a run starts at, and a loop's last bar) and says what it is
// (show); this draws it and does the dragging, so a drag never waits on .NET, which is told once it ends:
// - on the music: the chosen bar or the loop lit, with a tab over it (Loop on a bar, × on a loop), and on a loop a handle at
//   each end to drag, onto other lines too, the bar under the finger named;
// - beside the music, when it scrolls: the thumb, to drag through the piece, its rail marked with each line's first bar, the
//   start and the loop; held, it names the bar at the top of the view and outlines it;
// - under the piece's bar: the strip, a cell a bar, the bars on screen boxed: tap a cell to start there, drag along it to loop.

const pieces = new Map();

const playIcon = '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true"><path d="M2.5 1.5v9l8-4.5z" fill="currentColor"/></svg>';
const loopIcon = '<svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true"><path d="M15.5 7A6 6 0 0 0 4.3 8.6M4.5 13A6 6 0 0 0 15.7 11.4" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"/><path d="M16 3.5V7.5H12M4 16.5V12.5H8" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
const endIcon = '<svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true"><path d="M5 5l10 10M15 5L5 15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';

// The choice, from .NET: { from, to (null for no loop), lit, playing, explain, firstBar, ids (every bar's MEI id, in order) }.
// The strip and the thumb are null when they aren't on screen.
export function show(frame, strip, thumb, dotnet, choice) {
    if (!(frame instanceof Element)) return;
    let state = pieces.get(frame);
    if (!state) {
        state = { frame, drag: null, queued: 0 };
        pieces.set(frame, state);
        state.onScroll = () => queue(state);
        frame.addEventListener("scroll", state.onScroll, { passive: true });
        state.resized = new ResizeObserver(() => queue(state, true));
        state.resized.observe(frame);
    }
    state.dotnet = dotnet;
    const ids = choice.ids ?? [];
    if (ids.length !== state.choice?.ids.length || ids[0] !== state.choice?.ids[0] || ids[ids.length - 1] !== state.choice?.ids.at(-1)) {
        state.number = new Map(ids.map((id, i) => [id, choice.firstBar + i]));
        state.cellsKey = null;
    }
    state.choice = { ...choice, ids };
    state.geo = null;
    if (state.strip !== strip) bindStrip(state, strip);
    if (state.thumb !== thumb) bindThumb(state, thumb);
    watchMusic(state);
    drawAll(state);
}

export function forget(frame) {
    const state = pieces.get(frame);
    if (!state) return;
    frame.removeEventListener("scroll", state.onScroll);
    state.resized.disconnect();
    state.painted?.disconnect();
    cancelAnimationFrame(state.queued);
    cancelAnimationFrame(state.scrolling);
    pieces.delete(frame);
}

// The music drawn again (another width, another page): everything with it.
function watchMusic(state) {
    const music = state.frame.querySelector(".sheet .sheet-music");
    if (music === state.music) return;
    state.painted?.disconnect();
    state.music = music;
    if (!music) return;
    state.painted = new MutationObserver(() => queue(state, true));
    state.painted.observe(music, { childList: true });
}

function queue(state, all) {
    if (all) state.geo = null;
    state.all ||= all;
    if (state.queued) return;
    state.queued = requestAnimationFrame(() => {
        state.queued = 0;
        if (state.all) drawAll(state);
        else {
            placeThumb(state);
            drawWindow(state);
        }
        state.all = false;
    });
}

function drawAll(state) {
    drawMarks(state);
    drawStrip(state);
    drawThumb(state);
}

const make = (tag, cls, html) => {
    const e = document.createElement(tag);
    e.className = cls;
    if (html) e.innerHTML = html;
    return e;
};

const place = (e, x, y) => {
    e.style.left = `${x}px`;
    e.style.top = `${y}px`;
    return e;
};

// The bars drawn on the music, in order: each one's number and line, and where it is in the sheet's px, from its top staff's
// top to its lowest staff's bottom. Kept until the music is drawn again or the frame changes size; the sheet's box is fresh.
function drawn(state) {
    const sheet = state.frame.querySelector(".sheet");
    const music = sheet?.querySelector(".sheet-music");
    if (!music || !state.choice) return { sheet, list: [], box: null };
    const box = sheet.getBoundingClientRect();
    if (state.geo?.music !== music) {
        const list = [];
        for (const m of music.querySelectorAll(".measure[data-id]")) {
            const n = state.number.get(m.dataset.id);
            const staves = [...m.querySelectorAll(":scope > .staff")].map(s => s.getBoundingClientRect());
            if (n == null || staves.length === 0) continue;
            const r = m.getBoundingClientRect();
            list.push({ n, line: m.closest(".system"), left: r.left - box.left, right: r.right - box.left, top: Math.min(...staves.map(s => s.top)) - box.top, bottom: Math.max(...staves.map(s => s.bottom)) - box.top });
        }
        state.geo = { music, list };
    }
    return { sheet, list: state.geo.list, box };
}

// The drawn bars by line, each line's bars with its top and bottom.
function lines(list) {
    const out = [];
    for (const b of list) {
        const last = out[out.length - 1];
        if (last?.line === b.line) {
            last.bars.push(b);
            last.top = Math.min(last.top, b.top);
            last.bottom = Math.max(last.bottom, b.bottom);
        } else out.push({ line: b.line, bars: [b], top: b.top, bottom: b.bottom });
    }
    return out;
}

// Bars from..to drawn here, as a band a line, a little over and under the staves.
function bands(list, from, to) {
    return lines(list.filter(b => b.n >= from && b.n <= to)).map(l => ({
        first: l.bars[0].n, last: l.bars[l.bars.length - 1].n,
        left: l.bars[0].left - 3, right: l.bars[l.bars.length - 1].right + 3, top: l.top - 12, bottom: l.bottom + 12,
    }));
}

// The bar under a point in the sheet's px: on the nearest line, between its bar lines (or its first or last bar, off its ends).
function barAt(list, x, y) {
    const all = lines(list);
    if (all.length === 0) return null;
    const away = l => y < l.top ? l.top - y : y > l.bottom ? y - l.bottom : 0;
    const line = all.reduce((a, l) => away(l) < away(a) ? l : a);
    return (line.bars.find(b => x >= b.left && x <= b.right) ?? (x < line.bars[0].left ? line.bars[0] : line.bars[line.bars.length - 1])).n;
}

// The bars wholly in the frame's view, first and last.
function onScreen(state, list, box) {
    if (!box) return null;
    const f = state.frame.getBoundingClientRect();
    const seen = list.filter(b => b.top + box.top >= f.top - 4 && b.bottom + box.top <= f.bottom + 4);
    return seen.length ? [seen[0].n, seen[seen.length - 1].n] : null;
}

// ——— On the music ———

function marksLayer(state, sheet) {
    let layer = sheet.querySelector(":scope > .bar-marks");
    if (layer) return layer;
    layer = make("div", "bar-marks", '<div class="bar-bands"></div><div class="bar-peek" hidden></div>'
        + '<div class="bar-tab" hidden><span class="bar-tab-label"></span><button type="button" class="mu-btn bar-tab-key"></button></div>'
        + '<div class="bar-handle" data-end="from" aria-hidden="true" hidden></div><div class="bar-handle" data-end="to" aria-hidden="true" hidden></div>'
        + '<div class="bar-bubble" hidden></div>');
    sheet.appendChild(layer);
    // A tap on the tab or a handle is theirs, not a tap on the bar beneath.
    layer.addEventListener("click", e => e.stopPropagation());
    layer.querySelector(".bar-tab-key").addEventListener("click", e => {
        e.currentTarget.blur();
        state.dotnet.invokeMethodAsync("Loop", state.choice.to == null).catch(() => { });
    });
    for (const h of layer.querySelectorAll(".bar-handle")) h.addEventListener("pointerdown", e => dragHandle(state, e, h));
    return layer;
}

// The chosen bar or the loop (or what a drag would make it), lit with its tab, and a loop's handles where its ends are drawn.
function drawMarks(state) {
    const { sheet, list } = drawn(state);
    if (!sheet || !state.choice) return;
    const layer = marksLayer(state, sheet);
    const c = state.choice, d = state.drag?.kind === "thumb" ? null : state.drag;
    const from = d ? d.from : c.from, to = d ? d.to : c.to;
    const tab = layer.querySelector(".bar-tab"), [start, end] = layer.querySelectorAll(".bar-handle");
    const lit = d || (c.lit && !c.playing && !c.explain);
    const all = lit ? bands(list, from, to ?? from) : [];
    layer.querySelector(".bar-bands").replaceChildren(...all.map(b => {
        const e = place(make("div", "bar-band"), b.left, b.top);
        e.style.width = `${b.right - b.left}px`;
        e.style.height = `${b.bottom - b.top}px`;
        return e;
    }));
    const first = all[0], last = all[all.length - 1];
    tab.hidden = !(first && first.first === from) || d?.kind === "strip";
    if (!tab.hidden) {
        tab.querySelector(".bar-tab-label").innerHTML = to == null ? `${playIcon}From bar ${from}` : `${loopIcon}Loop ${to === from ? `bar ${from}` : `bars ${from}–${to}`}`;
        const key = tab.querySelector(".bar-tab-key");
        key.innerHTML = to == null ? `${loopIcon}Loop` : endIcon;
        key.setAttribute("aria-label", to == null ? `Loop bar ${from}` : "End the loop");
        place(tab, Math.min(Math.max(first.left, 4), sheet.clientWidth - tab.offsetWidth - 4), Math.max(first.top - tab.offsetHeight - 6, 4));
    }
    // Kept on the paper: a loop that starts or ends a line has its handle just inside it.
    const inside = x => Math.min(Math.max(x, 20), sheet.clientWidth - 20);
    start.hidden = to == null || !(first && first.first === from);
    end.hidden = to == null || !(last && last.last === to);
    if (!start.hidden) place(start, inside(first.left), (first.top + first.bottom) / 2);
    if (!end.hidden) place(end, inside(last.right), (last.top + last.bottom) / 2);
}

function bubble(state, text, x, y) {
    const sheet = state.frame.querySelector(".sheet");
    const b = sheet?.querySelector(":scope > .bar-marks > .bar-bubble");
    if (!b) return;
    b.hidden = text == null;
    if (text != null) place(b, x, y).textContent = text;
}

// A loop's end dragged on the music: the bar under the finger named over it, the music scrolling at the frame's top and bottom
// edges, and .NET told the loop when the finger lifts.
function dragHandle(state, e, handle) {
    const c = state.choice;
    if (c.playing || c.to == null) return;
    // Nor is a drag a swipe that turns the page.
    e.preventDefault();
    e.stopPropagation();
    handle.setPointerCapture(e.pointerId);
    handle.classList.add("bar-held");
    state.drag = { kind: "music", end: handle.dataset.end, from: c.from, to: c.to, x: e.clientX, y: e.clientY };
    const move = ev => {
        ev.stopPropagation();
        state.drag.x = ev.clientX;
        state.drag.y = ev.clientY;
        follow(state);
    };
    const done = ev => {
        ev.stopPropagation();
        handle.removeEventListener("pointermove", move);
        handle.removeEventListener("pointerup", done);
        handle.removeEventListener("pointercancel", done);
        handle.classList.remove("bar-held");
        cancelAnimationFrame(state.scrolling);
        const { from, to } = state.drag;
        state.drag = null;
        bubble(state, null);
        if (from !== c.from || to !== c.to) {
            state.choice = { ...state.choice, from, to, lit: true };
            state.dotnet.invokeMethodAsync("Looped", from, to).catch(() => { });
        }
        drawAll(state);
    };
    handle.addEventListener("pointermove", move);
    handle.addEventListener("pointerup", done);
    handle.addEventListener("pointercancel", done);
    // Dragged past the music's top or bottom edge, the music scrolls that way, faster further out; on the music, it stays put.
    const edge = () => {
        const f = state.frame.getBoundingClientRect(), d = state.drag;
        if (!d) return;
        d.moved ||= Math.hypot(d.x - e.clientX, d.y - e.clientY) > 8;
        const by = !d.moved ? 0 : d.y < f.top + 6 ? -Math.min(12, 2 + (f.top + 6 - d.y) / 4) : d.y > f.bottom - 6 ? Math.min(12, 2 + (d.y - f.bottom + 6) / 4) : 0;
        if (by) {
            state.frame.scrollTop += by;
            follow(state);
        }
        state.scrolling = requestAnimationFrame(edge);
    };
    state.scrolling = requestAnimationFrame(edge);
    follow(state);
}

function follow(state) {
    const d = state.drag, { list, box } = drawn(state);
    const n = box && barAt(list, d.x - box.left, d.y - box.top);
    if (n != null) {
        if (d.end === "from") d.from = Math.min(n, d.to);
        else d.to = Math.max(n, d.from);
    }
    drawMarks(state);
    drawStrip(state);
    if (box) bubble(state, `Bar ${d.end === "from" ? d.from : d.to}`, d.x - box.left, d.y - box.top - 26);
}

// ——— The strip ———

function bindStrip(state, strip) {
    state.strip = strip;
    state.cellsKey = null;
    if (!strip) return;
    strip.replaceChildren(make("div", "bar-cells"));
    strip.addEventListener("pointerdown", e => pressStrip(state, e));
}

function drawStrip(state) {
    const s = state.strip, c = state.choice;
    if (!s || !c || c.ids.length === 0) return;
    const cells = s.querySelector(".bar-cells");
    const { list, box } = drawn(state);
    const starts = new Set(lines(list).map(l => l.bars[0].n));
    const count = c.ids.length, width = cells.clientWidth / count;
    const key = `${count}|${c.firstBar}|${[...starts].join(",")}|${Math.round(width)}`;
    if (key !== state.cellsKey) {
        state.cellsKey = key;
        // Every bar's number when there's room. Else each line's first, as the music prints them, when the lines are drawn (a
        // scroll) and far enough apart; or every second, fifth, tenth… bar, as far apart as a number needs.
        const step = [1, 2, 5, 10, 20, 50, 100].find(s => s * width >= 24) ?? 100;
        const byLine = step > 1 && list.length >= count / 2 && starts.size > 1 && count / starts.size * width >= 24;
        const html = [];
        let last = -Infinity;
        for (let n = c.firstBar; n < c.firstBar + count; n++) {
            // Not crowding the number before it: a line of one bar can have its first's number left off.
            const shown = n >= 1 && (step === 1 || (byLine ? starts.has(n) : n % step === 0 || (n === 1 && step > 2))) && (n - last) * width >= 22;
            if (shown) last = n;
            html.push(`<div class="bar-cell${starts.has(n) ? " bar-cell-line" : ""}">${shown ? n : ""}</div>`);
        }
        cells.innerHTML = html.join("") + '<div class="bar-window" hidden></div><div class="bar-strip-loop" hidden><div class="bar-strip-handle" data-end="from"></div><div class="bar-strip-handle" data-end="to"></div></div><div class="bar-strip-start" hidden></div><div class="bar-bubble" hidden></div>';
    }
    const at = n => `${(n - c.firstBar) / count * 100}%`;
    const span = (e, a, b) => {
        e.style.left = at(a);
        e.style.width = `${(b - a + 1) / count * 100}%`;
    };
    const d = state.drag?.kind === "thumb" ? null : state.drag;
    const from = d ? d.from : c.from, to = d ? d.to : c.to;
    const loop = cells.querySelector(".bar-strip-loop"), start = cells.querySelector(".bar-strip-start");
    loop.hidden = to == null;
    if (!loop.hidden) span(loop, from, to);
    start.hidden = !(c.lit || d);
    if (!start.hidden) start.style.left = `${(from - c.firstBar + 0.5) / count * 100}%`;
    drawWindow(state, list, box);
}

// The bars on screen, boxed on the strip.
function drawWindow(state, list, box) {
    const s = state.strip, c = state.choice;
    if (!s || !c || c.ids.length === 0) return;
    if (!list) ({ list, box } = drawn(state));
    const w = s.querySelector(".bar-window");
    const seen = onScreen(state, list, box);
    if (!w) return;
    w.hidden = !seen;
    if (seen) {
        w.style.left = `${(seen[0] - c.firstBar) / c.ids.length * 100}%`;
        w.style.width = `${(seen[1] - seen[0] + 1) / c.ids.length * 100}%`;
    }
}

// A press on the strip: lifted where it went down, a tap, as on the bar on the music, which comes into view; moved along, a loop
// over the bars between (or, from a loop's handle, that end moved), .NET told when the finger lifts. While a run plays, only taps.
function pressStrip(state, e) {
    const c = state.choice;
    if (!c || c.ids.length === 0) return;
    const cells = state.strip.querySelector(".bar-cells"), r = cells.getBoundingClientRect();
    const barAtX = x => c.firstBar + Math.min(c.ids.length - 1, Math.max(0, Math.floor((x - r.left) / r.width * c.ids.length)));
    const press = { x: e.clientX, bar: barAtX(e.clientX), end: c.to == null ? null : e.target.closest(".bar-strip-handle")?.dataset.end, moved: false };
    e.preventDefault();
    state.strip.setPointerCapture(e.pointerId);
    const say = (text, x) => {
        const b = cells.querySelector(".bar-bubble");
        b.hidden = text == null;
        if (text != null) place(b, x - r.left, 0).textContent = text;
    };
    const move = ev => {
        if (c.playing || (!press.moved && Math.abs(ev.clientX - press.x) < 8)) return;
        press.moved = true;
        const n = barAtX(ev.clientX);
        state.drag = press.end === "from" ? { kind: "strip", from: Math.min(n, c.to), to: c.to }
            : press.end === "to" ? { kind: "strip", from: c.from, to: Math.max(n, c.from) }
            : { kind: "strip", from: Math.min(press.bar, n), to: Math.max(press.bar, n) };
        drawStrip(state);
        drawMarks(state);
        const { from, to } = state.drag;
        say(from === to ? `Bar ${from}` : `Bars ${from}–${to}`, ev.clientX);
    };
    const done = () => {
        state.strip.removeEventListener("pointermove", move);
        state.strip.removeEventListener("pointerup", done);
        state.strip.removeEventListener("pointercancel", done);
        say(null);
        const drag = state.drag;
        state.drag = null;
        if (drag) {
            state.choice = { ...state.choice, from: drag.from, to: drag.to, lit: true };
            state.dotnet.invokeMethodAsync("Looped", drag.from, drag.to).catch(() => { });
        }
        else if (!press.moved) {
            bringIntoView(state, press.bar);
            state.dotnet.invokeMethodAsync("Chose", press.bar).catch(() => { });
        }
        drawAll(state);
    };
    state.strip.addEventListener("pointermove", move);
    state.strip.addEventListener("pointerup", done);
    state.strip.addEventListener("pointercancel", done);
}

// A bar on a scrolling music brought into view, its line a quarter of the way down (a page turns from .NET instead).
function bringIntoView(state, n) {
    const { sheet, list } = drawn(state), b = list.find(x => x.n === n), f = state.frame;
    if (!b || f.scrollHeight <= f.clientHeight + 2) return;
    f.scrollTo({ top: Math.max(0, sheet.offsetTop + b.top - f.clientHeight / 4), behavior: "smooth" });
}

// ——— The thumb ———

function bindThumb(state, thumb) {
    state.thumb = thumb;
    if (!thumb) return;
    thumb.innerHTML = '<div class="bar-rail"></div><div class="bar-ticks"></div><div class="bar-rail-loop" hidden></div><div class="bar-rail-start" hidden></div><div class="bar-thumb-key"></div><div class="bar-bubble bar-bubble-side" hidden></div>';
    thumb.addEventListener("pointerdown", e => holdThumb(state, e));
}

// The rail's scale: the music's whole height, from its top, over the rail's height.
function scale(state) {
    const t = state.thumb, f = state.frame;
    const rail = t.clientHeight - 12, whole = Math.max(f.scrollHeight, 1);
    return { rail, whole, y: px => 6 + px / whole * rail };
}

function drawThumb(state) {
    const t = state.thumb, c = state.choice;
    if (!t || !c) return;
    const { sheet, list } = drawn(state), { y } = scale(state), top = sheet?.offsetTop ?? 0;
    t.querySelector(".bar-ticks").replaceChildren(...lines(list).map(l => place(make("div", "bar-tick", `<span>${l.bars[0].n}</span>`), 0, y(top + l.top))));
    const lineOf = n => lines(list).find(l => l.bars.some(b => b.n === n));
    const loop = t.querySelector(".bar-rail-loop"), start = t.querySelector(".bar-rail-start");
    const a = c.to != null && lineOf(c.from), b = c.to != null && lineOf(c.to);
    loop.hidden = !(a && b);
    if (!loop.hidden) {
        loop.style.top = `${y(top + a.top)}px`;
        loop.style.height = `${Math.max(8, y(top + b.bottom) - y(top + a.top))}px`;
    }
    const s = c.lit && lineOf(c.from);
    start.hidden = !s;
    if (s) start.style.top = `${y(top + (s.top + s.bottom) / 2)}px`;
    t.classList.toggle("bar-thumb-off", !!c.playing);
    placeThumb(state);
}

function placeThumb(state) {
    const t = state.thumb, f = state.frame;
    if (!t) return;
    const key = t.querySelector(".bar-thumb-key"), { rail, whole, y } = scale(state);
    key.hidden = f.scrollHeight <= f.clientHeight + 2;
    key.style.top = `${y(f.scrollTop)}px`;
    key.style.height = `${Math.max(56, f.clientHeight / whole * rail)}px`;
}

// The thumb held (or the rail pressed, the thumb jumping there): the music follows it, and the bar at the top of the view is
// named beside it and outlined, until it's let go. The start stays where it was.
function holdThumb(state, e) {
    const t = state.thumb, f = state.frame, key = t.querySelector(".bar-thumb-key");
    if (state.choice?.playing || key.hidden) return;
    e.preventDefault();
    t.setPointerCapture(e.pointerId);
    const { rail, whole } = scale(state), k = key.getBoundingClientRect();
    if (e.clientY < k.top || e.clientY > k.bottom) f.scrollTop = (e.clientY - t.getBoundingClientRect().top - 6) / rail * whole - f.clientHeight / 2;
    const from = { y: e.clientY, top: f.scrollTop };
    key.classList.add("bar-held");
    state.drag = { kind: "thumb" };
    const peek = () => {
        const { sheet, list, box } = drawn(state), seen = onScreen(state, list, box);
        const side = t.querySelector(".bar-bubble-side"), outline = sheet?.querySelector(":scope > .bar-marks > .bar-peek");
        side.hidden = !seen;
        if (outline) outline.hidden = !seen;
        if (!seen) return;
        const b = list.find(x => x.n === seen[0]), kb = key.getBoundingClientRect(), tb = t.getBoundingClientRect();
        place(side, kb.left - tb.left, kb.top - tb.top + kb.height / 2).textContent = `Bar ${seen[0]}`;
        if (outline) {
            place(outline, b.left - 3, b.top - 12);
            outline.style.width = `${b.right - b.left + 6}px`;
            outline.style.height = `${b.bottom - b.top + 24}px`;
        }
    };
    const move = ev => {
        f.scrollTop = from.top + (ev.clientY - from.y) / rail * whole;
        placeThumb(state);
        drawWindow(state);
        peek();
    };
    const done = () => {
        t.removeEventListener("pointermove", move);
        t.removeEventListener("pointerup", done);
        t.removeEventListener("pointercancel", done);
        key.classList.remove("bar-held");
        state.drag = null;
        t.querySelector(".bar-bubble-side").hidden = true;
        const outline = f.querySelector(".sheet > .bar-marks > .bar-peek");
        if (outline) outline.hidden = true;
    };
    t.addEventListener("pointermove", move);
    t.addEventListener("pointerup", done);
    t.addEventListener("pointercancel", done);
    placeThumb(state);
    peek();
}
