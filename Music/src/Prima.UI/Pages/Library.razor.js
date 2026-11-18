// Where each of the Library's columns was left. The Library is columns that scroll by themselves (its list of composers, a
// composer's works, a work's movements, a list of pieces), and nothing puts a column back by itself when it's come back to: by
// the way back, or by the rail's Library key. Each column says what it holds (data-keep, with data-at where that isn't the
// whole address: a composer's works stay where they are while another work is chosen), and is put back to where that was left;
// the first time, to where what's chosen in it shows (a composer far down the list, a work arrived at from a kind's index), or
// its top. Kept while Prima is open.
const places = new Map();
let shown = null;

// The Library on the screen, drawn: each column scrolled to where it was left, and its place kept from now on.
export function show() {
    shown = here();
    for (const column of document.querySelectorAll(".lib [data-keep]")) {
        if (!column.dataset.kept) {
            column.dataset.kept = "yes";
            let queued = false;
            column.addEventListener("scroll", () => {
                if (queued) return;
                queued = true;
                requestAnimationFrame(() => {
                    queued = false;
                    // Only while the Library is the screen shown: a column being taken down mustn't lose its place.
                    if (shown !== null && shown === here()) places.set(what(column), column.scrollTop);
                });
            }, { passive: true });
        }
        column.scrollTop = places.get(what(column)) ?? chosen(column);
    }
}

// Where a column not seen before starts: with the row chosen in it in the middle, or at its top when it's near the top or none
// is. A row taller than the column (a work open upright, with all its movements) starts at the column's top, its name showing.
function chosen(column) {
    // A row of the column itself: the list's switch (Composers, Kinds, Levels) marks what it's turned to as well.
    const row = column.querySelector(".col-item[aria-current], .work[data-open]");
    if (!row) return 0;
    const top = row.getBoundingClientRect().top - column.getBoundingClientRect().top + column.scrollTop;
    return Math.max(0, Math.round(top - Math.max(0, (column.clientHeight - row.offsetHeight) / 2)));
}

// The Library has gone (a piece opened, another screen): its places stay as they were.
export function leave() {
    shown = null;
}

// What a column holds: its kind, and which of that kind (the address, where it doesn't say).
function what(column) {
    return `${column.dataset.keep} ${column.dataset.at ?? here()}`;
}

function here() {
    return location.pathname + location.search;
}
