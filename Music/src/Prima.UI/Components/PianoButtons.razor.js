// The piano's two buttons: presses the screen's button marked for the key, if one is showing and can be pressed, with the look
// of a press, as if it were tapped.
export function press(kind) {
    const button = [...document.querySelectorAll(`[data-piano="${kind}"]`)].find(b => !b.disabled && b.getClientRects().length > 0);
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
