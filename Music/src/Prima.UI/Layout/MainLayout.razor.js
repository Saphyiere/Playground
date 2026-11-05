// The look in use: the page follows data-theme on <html>, and index.html restores it from local storage before the first paint.
export function showLook(id) {
    document.documentElement.dataset.theme = id;
    try {
        localStorage.setItem("look", id);
    } catch {
        // No storage: the next start shows Studio until the saved look loads.
    }
}
