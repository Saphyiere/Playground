// Where each of the Library's lists was left, by its address (its search and filter are in it). The screen scrolls inside
// .shell-main, which nothing puts back by itself, so a list puts it back when it's come back to: by the way back, or by the
// rail's Library key. Kept while Prima is open.
const places = new Map();
let shown = null;
let watched = null;

// The list on the screen, drawn: scrolled to where it was left (the top, the first time), and its place kept from now on.
export function show() {
    const main = document.querySelector(".shell-main");
    if (!main) return;
    if (main !== watched) {
        watched = main;
        let queued = false;
        main.addEventListener("scroll", () => {
            if (queued) return;
            queued = true;
            requestAnimationFrame(() => {
                queued = false;
                // Only while the list is the one shown: another screen, or a list being drawn, mustn't take its place.
                if (shown !== null && shown === here()) places.set(shown, main.scrollTop);
            });
        }, { passive: true });
    }
    shown = here();
    main.scrollTop = places.get(shown) ?? 0;
}

// The list has gone (a piece opened, another screen): its place stays as it was.
export function leave() {
    shown = null;
}

function here() {
    return location.pathname + location.search;
}
