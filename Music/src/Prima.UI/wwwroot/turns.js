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
    // The page out of sight (the tablet asleep, the app put away): a practice session pauses.
    const hidden = () => { if (document.hidden) dotnet.invokeMethodAsync("Hidden"); };
    document.addEventListener("keydown", handler);
    document.addEventListener("visibilitychange", hidden);
    return {
        dispose: () => {
            document.removeEventListener("keydown", handler);
            document.removeEventListener("visibilitychange", hidden);
        },
    };
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

// Full screen: the piece alone, the rail and the head hidden (data-full on <html>; the tablet's own bars go by the host). Where the
// browser can, and motion isn't turned down, the bar and the music move to their places as the rest fades (a view transition;
// prima.css, Motion). They're named for the move only while it lasts. Not when the screen's going: there's no bar then.
export function fullScreen(on) {
    const root = document.documentElement;
    if (root.hasAttribute("data-full") === on) return;
    const toggle = () => root.toggleAttribute("data-full", on);
    const bar = document.querySelector(".play-bar");
    if (!document.startViewTransition || !bar || matchMedia("(prefers-reduced-motion: reduce)").matches) {
        toggle();
        return;
    }
    const named = [[bar, "play-bar"], [document.querySelector(".piece-stage"), "play-music"]].filter(([element]) => element);
    for (const [element, name] of named) element.style.viewTransitionName = name;
    const move = document.startViewTransition(toggle);
    const unname = () => { for (const [element] of named) element.style.viewTransitionName = ""; };
    move.ready.catch(() => { });
    move.finished.then(unname, unname);
}
