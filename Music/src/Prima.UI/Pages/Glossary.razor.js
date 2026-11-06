// An entry opened from a piece's What's this, brought into view at the top.
export function show(id) {
    document.getElementById(id)?.scrollIntoView({ block: "start" });
}
