// Brings a part of the edit screen into view: the line a Change opened it on, which may be far down a long workout, or the
// picker Add more opened it on. Its top lands a little under the screen's (the stylesheet's scroll margin), so the line before shows.
export function show(id) {
    document.getElementById(id)?.scrollIntoView({ block: 'start' });
}
