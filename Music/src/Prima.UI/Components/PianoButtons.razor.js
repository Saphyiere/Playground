// The piano's two buttons: presses the screen's button marked for the key, or for go the primary one (the orange key, or Play)
// when none is marked, if it's showing and can be pressed, with the look of a press, as if it were tapped. A button marked
// "off" is never pressed from the piano.
export function press(kind) {
    const usable = b => !b.disabled && b.getClientRects().length > 0;
    const marked = [...document.querySelectorAll(`[data-piano="${kind}"]`)].find(usable);
    const primary = kind === "go" ? [...document.querySelectorAll(".mu-btn.mu-primary, .mu-play")].find(b => usable(b) && !b.dataset.piano) : null;
    const button = marked ?? primary;
    if (!button) return;
    button.classList.add("piano-pressed");
    setTimeout(() => button.classList.remove("piano-pressed"), 160);
    button.click();
}

// The keys' names on the buttons they press, while the piano can press them.
export function showKeys(on) {
    if (on) document.documentElement.dataset.pianoKeys = "";
    else delete document.documentElement.dataset.pianoKeys;
}
