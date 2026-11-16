// What goes wrong on the page, kept until Prima writes it in its problem log (Settings › Problems). When .NET fails in a way that
// stops the page answering, Blazor says why on the console and shows its error bar, and from then on the page can't reach .NET:
// so what it said is kept in local storage, which outlives Reload and Prima being closed, and MainLayout takes it in the next time
// Prima starts (primaProblems.take). Script errors and promises nobody caught are kept the same way. Loaded before Blazor's own
// script, so it hears everything Blazor says.
(() => {
    const key = "prima.problems";
    const read = () => {
        try { return JSON.parse(localStorage.getItem(key) ?? "[]"); } catch { return []; }
    };
    const write = list => {
        try { localStorage.setItem(key, JSON.stringify(list.slice(-20))); } catch { }
    };
    const text = value => {
        if (value instanceof Error) return value.stack ?? value.message;
        if (typeof value === "string") return value;
        try { return JSON.stringify(value); } catch { return String(value); }
    };
    // A call back into a page that has already gone (the music redrawn as it was left), or the browser saying a size it watches
    // changed again as it was told (the music laid out afresh): nothing went wrong, unless the page stops on it.
    const harmless = said => /There is no tracked object with id|JSDisconnectedException|ResizeObserver loop/.test(said);
    // What the page said last, kept in the list or not, and when: should the error bar show straight after, that's why the page
    // stopped.
    let last = null;
    const keep = (kind, said) => {
        said = String(said ?? "").slice(0, 16000);
        if (!said) return;
        last = { at: new Date().toISOString(), kind, path: location.pathname + location.search, text: said };
        if (harmless(said)) return;
        const list = read();
        list.push(last);
        write(list);
    };

    const say = console.error.bind(console);
    console.error = (...args) => {
        keep("console", args.map(text).join(" "));
        say(...args);
    };
    addEventListener("error", e => keep("script", e.error ? text(e.error) : `${e.message} (${e.filename}:${e.lineno})`));
    addEventListener("unhandledrejection", e => keep("promise", text(e.reason)));

    // Blazor's error bar shown: the page has stopped answering, for what it said just before (Blazor says why, then shows the bar).
    // That's kept as why, though it may be a line the list leaves out any other time, and the bar says it. What was said a while
    // before (two seconds) isn't taken for it, nor is a problem kept earlier: with nothing said just then, the stop is kept all
    // the same.
    const stopped = bar => {
        if (bar.style.display !== "block" || bar.dataset.kept) return;
        bar.dataset.kept = "true";
        const now = new Date();
        const why = last && now - new Date(last.at) < 2000 ? last
            : { at: now.toISOString(), kind: "stopped", path: location.pathname + location.search, text: "It didn't say why." };
        const list = read();
        const kept = list.at(-1);
        if (kept && kept.at === why.at && kept.text === why.text) kept.kind = "stopped";
        else list.push({ ...why, kind: "stopped" });
        write(list);
        const what = bar.querySelector(".problem-what");
        if (what) what.textContent = why.text.split("\n")[0];
    };
    const watch = () => {
        const bar = document.getElementById("blazor-error-ui");
        if (bar) new MutationObserver(() => stopped(bar)).observe(bar, { attributes: true, attributeFilter: ["style"] });
    };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", watch);
    else watch();

    window.primaProblems = {
        take() {
            const list = read();
            try { localStorage.removeItem(key); } catch { }
            return JSON.stringify(list);
        },
    };
})();
