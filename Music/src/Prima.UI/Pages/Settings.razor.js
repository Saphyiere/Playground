// A section a link asked for (settings?tab=app&section=problems, from a stopped screen's card), brought into view at the top.
export function show(id) {
    document.getElementById(id)?.scrollIntoView({ block: "start" });
}
