// Whether a label's long words fit on its line, measured as drawn in the look's own type: when they don't, it's marked short and the
// stylesheet draws the plain words instead. Measured again when the type has loaded and when the screen's width changes.
const labels = new Set();

function measure(label) {
    label.classList.remove('short');
    if (label.scrollWidth > label.clientWidth + 1) label.classList.add('short');
}

function measureAll() {
    for (const label of labels) {
        if (label.isConnected) measure(label);
        else labels.delete(label);
    }
}

addEventListener('resize', measureAll);
document.fonts?.ready.then(measureAll);

export function fit(label) {
    if (!label) return;
    labels.add(label);
    measure(label);
}
