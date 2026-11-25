// Prima open: its mark (index.html) fades as the first screen is drawn and goes once it has; data-opening, which has the rail
// slide in and Today play its moment, stays on <html> until those have played. Once: every screen after asks again.
export function opened() {
    const mark = document.querySelector(".opening");
    if (!mark || mark.classList.contains("gone")) return;
    mark.classList.add("gone");
    setTimeout(() => mark.remove(), 400);
    setTimeout(() => document.documentElement.removeAttribute("data-opening"), 1400);
}

// The look in use: the page follows data-theme on <html>, and index.html restores it from local storage before the first paint.
export function showLook(id) {
    document.documentElement.dataset.theme = id;
    try {
        localStorage.setItem("look", id);
    } catch {
        // No storage: the next start shows Studio until the saved look loads.
    }
}
