// What's this, on every screen. While it's on, a tap says what's tapped instead of pressing it: every tap, touch and press is
// caught on its way down (in the capture phase, on the window, before Blazor's handlers or a link see it), and a tap on what's
// marked data-explain asks .NET for its card. What What's this draws itself (data-explain-keep: its key, its panel, its cards)
// and the music, which says what's printed on it by itself, are left alone. A tap made by a script (the piano's keys pressing
// a button) is stopped too, and says nothing.

let dotnet = null;
const kept = "[data-explain-keep], .sheet";
const caught = ["click", "pointerdown", "pointerup", "mousedown", "mouseup", "touchstart", "touchend", "dblclick", "contextmenu"];

function guard(event) {
    if (!dotnet) return;
    const target = event.target instanceof Element ? event.target : event.target?.parentElement;
    if (!target || target.closest(kept)) return;
    event.stopPropagation();
    event.stopImmediatePropagation();
    if (event.type !== "click") return;
    event.preventDefault();
    if (!event.isTrusted) return;
    const marked = target.closest("[data-explain]");
    const r = (marked ?? target).getBoundingClientRect();
    dotnet.invokeMethodAsync("Tapped", marked?.getAttribute("data-explain") ?? null, r.left, r.top, r.right, r.bottom).catch(() => { });
}

export function on(reference) {
    if (!dotnet) for (const type of caught) window.addEventListener(type, guard, { capture: true });
    dotnet = reference;
    document.documentElement.setAttribute("data-explaining", "");
}

export function off() {
    for (const type of caught) window.removeEventListener(type, guard, { capture: true });
    dotnet = null;
    document.documentElement.removeAttribute("data-explaining");
}

// A card beside what it's about: under it where there's room, otherwise over it, its middle over the thing's within the screen,
// and its pointer at it. Shown only once it's placed.
export function place(card, left, top, right, bottom) {
    if (!card?.isConnected) return;
    const [w, h] = [card.offsetWidth, card.offsetHeight];
    const [vw, vh] = [document.documentElement.clientWidth, document.documentElement.clientHeight];
    const gap = 12;
    const below = bottom + gap + h <= vh - 8 || top - gap - h < 8;
    const x = Math.min(Math.max(8, (left + right) / 2 - w / 2), vw - w - 8);
    const y = below ? Math.min(bottom + gap, vh - h - 8) : Math.max(8, top - gap - h);
    card.style.left = `${x}px`;
    card.style.top = `${y}px`;
    card.style.setProperty("--point", `${Math.min(Math.max(16, (left + right) / 2 - x), w - 16)}px`);
    card.dataset.side = below ? "below" : "above";
    card.style.visibility = "visible";
}
