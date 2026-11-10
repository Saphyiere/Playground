// A run's details come in a part at a time, so the eye is led through them and not met with them all at once. A part (a panel,
// the expression line, the bars) is an empty frame until most of it is first in view and the part before it has had its turn;
// a turn ends early when its part is scrolled out of view, and a part scrolled past before its turn is simply there. The
// stylesheet draws each part's stages and says how long its turn is (--turn).
const reveals = new Set();

export function stage(details, scrollTo) {
    if (!details?.isConnected) return;

    for (const old of [...reveals]) if (old.details === details || !old.details.isConnected) old.stop();
    const still = matchMedia("(prefers-reduced-motion: reduce)").matches;
    // They open under the key that was tapped, mostly below the screen's edge: brought up just far enough to show.
    if (scrollTo) details.scrollIntoView({ behavior: still ? "auto" : "smooth", block: "nearest" });
    if (still) return;

    const parts = [...details.querySelectorAll(".sum-stage")];
    const inView = new Set();
    const waiting = [];
    let turn = null;
    let timer = 0;
    const next = () => {
        [turn, timer] = [null, 0];
        while (waiting.length > 0 && timer === 0) {
            const part = waiting.shift();
            part.dataset.seen = "";
            if (!inView.has(part)) continue;

            countUp(part);
            [turn, timer] = [part, setTimeout(next, parseFloat(getComputedStyle(part).getPropertyValue("--turn")) || 0)];
        }
        if (timer === 0 && parts.every(part => "seen" in part.dataset)) reveal.stop();
    };
    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            const part = entry.target;
            if (entry.isIntersecting) inView.add(part);
            else inView.delete(part);
            // Seen once most of it shows (260px of a tall one), or as much of it as will: its top has reached the view's top.
            const [whole, shown] = [entry.boundingClientRect, entry.intersectionRect];
            const enough = entry.isIntersecting && (shown.height >= Math.min(260, whole.height * 0.75) || whole.top < shown.top - 1);
            if (enough && !("seen" in part.dataset) && !waiting.includes(part)) waiting.push(part);
        }
        waiting.sort((a, b) => parts.indexOf(a) - parts.indexOf(b));
        // A turn ends early once its part has left the view.
        if (turn && !inView.has(turn)) {
            clearTimeout(timer);
            timer = 0;
        }
        if (timer === 0) next();
    }, { threshold: Array.from({ length: 21 }, (_, i) => i / 20) });
    const reveal = { details, stop: () => { observer.disconnect(); clearTimeout(timer); reveals.delete(reveal); } };
    reveals.add(reveal);
    parts.forEach(part => observer.observe(part));
}

// The bars' map opens downwards, where the screen's edge may be: the page follows it down as it opens.
export function follow(map) {
    const until = performance.now() + 450;
    const frame = now => {
        if (!map?.isConnected) return;

        map.scrollIntoView({ block: "nearest" });
        if (now < until) requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
}

// A panel's figure counts up as its chart fills.
function countUp(part) {
    const text = part.querySelector(".sum-figure")?.firstChild;
    const to = /^(\d+)%$/.exec(text?.nodeValue ?? "")?.[1];
    if (to === undefined) return;

    const whole = text.nodeValue;
    const start = performance.now() + 140;
    const frame = now => {
        const t = Math.min(1, Math.max(0, (now - start) / 460));
        text.nodeValue = t < 1 ? `${Math.round(to * (1 - (1 - t) ** 3))}%` : whole;
        if (t < 1 && text.isConnected) requestAnimationFrame(frame);
    };
    frame(performance.now());
}
