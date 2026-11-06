// Turning without hands on the screen: a Bluetooth page turner or a keyboard sends arrow keys, Page Up and Down, or the space
// bar; each calls Turn(+1 or -1) in .NET. And scrolling a sheet a system at a time: the system with a note in it moves to the
// upper third of the view.

export function listenKeys(dotnet) {
    const handler = event => {
        if (event.target.closest?.("input, textarea, select")) return;
        const forward = ["ArrowRight", "ArrowDown", "PageDown", " "].includes(event.key);
        const back = ["ArrowLeft", "ArrowUp", "PageUp"].includes(event.key);
        if (!forward && !back) return;
        event.preventDefault();
        dotnet.invokeMethodAsync("Turn", forward ? 1 : -1);
    };
    document.addEventListener("keydown", handler);
    return { dispose: () => document.removeEventListener("keydown", handler) };
}

export function scrollToSystemOf(frame, id) {
    const note = frame.querySelector(`.sheet-music [data-id="${id}"]`);
    const system = note?.closest(".system");
    if (!system) return;
    const top = system.getBoundingClientRect().top - frame.getBoundingClientRect().top + frame.scrollTop;
    frame.scrollTo({ top: Math.max(0, top - frame.clientHeight / 3), behavior: "smooth" });
}

export function size(element) {
    return [element.clientWidth, element.clientHeight];
}

// Full screen: the piece alone, the rail and the head hidden (data-full on <html>; the tablet's own bars go by the host).
export function fullScreen(on) {
    document.documentElement.toggleAttribute("data-full", on);
}
