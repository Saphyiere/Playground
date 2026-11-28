// Prima open: its mark (index.html) fades as the first screen is drawn and goes once it has; data-opening, which has the rail
// slide in and Today play its moment, stays on <html> until those have played. Once: every screen after asks again.
export function opened() {
    const mark = document.querySelector(".opening");
    if (!mark || mark.classList.contains("gone")) return;
    mark.classList.add("gone");
    setTimeout(() => mark.remove(), 400);
    setTimeout(() => document.documentElement.removeAttribute("data-opening"), 1400);
}

// The look in use, and Porcelain's accent: the page follows data-theme and data-accent on <html> (red, the default, needs
// none), and index.html restores both from local storage before the first paint.
export function showLook(id, accent) {
    const html = document.documentElement;
    html.dataset.theme = id;
    if (accent && accent !== "red") html.dataset.accent = accent;
    else delete html.dataset.accent;
    try {
        localStorage.setItem("look", id);
        localStorage.setItem("accent", accent ?? "red");
    } catch {
        // No storage: the next start shows Porcelain until the saved look loads.
    }
}
