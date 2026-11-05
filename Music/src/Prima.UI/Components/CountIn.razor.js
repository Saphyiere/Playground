// The count-in's cells: the first sounds `msUntilFirst` from now and the rest every `beatMs`. Each lights on its beat and stays
// lit, so the row fills like a bar running out to the music.
const frames = new WeakMap();

export function start(element, msUntilFirst, beatMs, beats, beatsPerBar) {
    stop(element);
    const cells = Array.from({ length: beats }, (_, i) => {
        const cell = document.createElement("span");
        cell.className = "count-in-beat";
        cell.textContent = String(i % beatsPerBar + 1);
        return cell;
    });
    element.replaceChildren(...cells);
    const first = performance.now() + msUntilFirst;
    const frame = now => {
        const beat = Math.floor((now - first) / beatMs);
        cells.forEach((cell, i) => {
            cell.classList.toggle("now", i === beat);
            cell.classList.toggle("past", i < beat);
        });
        if (beat < beats) frames.set(element, requestAnimationFrame(frame));
    };
    frames.set(element, requestAnimationFrame(frame));
}

export function stop(element) {
    cancelAnimationFrame(frames.get(element));
    frames.delete(element);
}
