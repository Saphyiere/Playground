using Prima.Core.Music;

namespace Prima.Core.Library;

/// <summary>
/// Notation made from a MIDI file, so one can be imported and read. A MIDI file has the notes and when they sound, not how they're
/// written, so this works the writing out:
/// <list type="bullet">
/// <item>The hands come from its tracks (the higher of two is the right hand), or in a single track from middle C.</item>
/// <item>The bars come from its time signatures, the key from its key signature, or from the notes when it has none.</item>
/// <item>Where the file lets its notes go a little early, as notation software does, each is written at the length it would
/// have held in full.</item>
/// <item>Each quarter-note beat is snapped to sixteenths, thirty-seconds or triplets, whichever fits its notes best.</item>
/// <item>Notes a hand holds over others make a second voice; notes are tied across barlines, and off the beat across beats.</item>
/// </list>
/// A file exported from notation software comes out close to its score. One recorded from playing comes out ragged, and a pickup
/// bar comes out as a whole bar with rests.
/// </summary>
public static class MidiNotation
{
    // Note values a single note can take, longest first: whole to thirty-second, plain or dotted.
    private static readonly int[] Values = [1920, 1440, 960, 720, 480, 360, 240, 180, 120, 90, 60];

    // Names that name an instrument or a track, not the piece.
    private static readonly string[] Generic = ["piano", "track", "untitled", "staff", "midi", "grand", "right", "left", "hand", "voice", "instrument", "sequence"];

    // Krumhansl and Kessler's key profiles: how much each degree of the scale is heard in a major and a minor key.
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    private sealed record Chord(long On, long Off, List<int> Keys);

    public static Score Read(ReadOnlySpan<byte> data, string fallbackTitle)
    {
        var midi = MidiFile.Read(data);
        long Scale(long tick) => (tick * Ticks.Quarter + midi.Division / 2) / midi.Division;

        // Channel 10 is the drums.
        var heard = midi.Ticked.Where(n => n.Channel != 9 && n.Off > n.On).Select(n => n with { On = Scale(n.On), Off = Scale(n.Off) }).ToList();
        if (heard.Count == 0) throw new FormatException("No notes.");

        // A file that starts late has every note off the beat by the same amount: LilyPond's MIDI, say, which plays a grace note
        // before the first beat by holding the whole piece back. That amount is where most notes start in their sixteenth.
        var phases = heard.GroupBy(n => n.On % 120).ToDictionary(g => g.Key, g => g.Count());
        int Near(int phase) => Enumerable.Range(-2, 5).Sum(d => phases.GetValueOrDefault((phase + d + 120) % 120));
        var late = Enumerable.Range(0, 120).MaxBy(Near);
        if (late != 0 && Near(late) > 2 * Near(0) && Near(late) * 3 >= heard.Count)
        {
            heard = [.. heard.Select(n => n with { On = Math.Max(0, n.On - late), Off = Math.Max(1, n.Off - late) })];
        }

        // Notation software lets each note go a little before its written end, all by about the same share: a file from
        // MuseScore holds a note for 95% of its length, and written as it sounds its half note is a dotted quarter tied to a
        // dotted sixteenth, and a rest. So where notes stop a little short of their hand's next note, off any sixteenth's or
        // triplet's place, the share they're held is taken from them (the middle one: some may be held in full, as notes
        // under a slur are said to be), and each note is written up to the half beat, or the next note, nearest where it would
        // end held in full. A little is a fifth of the time held: a staccato note stops well short and stays short, a written
        // rest stays, and a file that holds its notes in full is left as it is.
        var hands = heard.GroupBy(n => (n.Track, n.Channel)).ToDictionary(g => g.Key, g => g.Select(n => n.On).Distinct().Order().ToList());
        var reaching = heard.Select(n =>
        {
            var own = hands[(n.Track, n.Channel)];
            long From(long tick)
            {
                var found = own.BinarySearch(tick);
                var at = found < 0 ? ~found : found;
                return at < own.Count ? own[at] : long.MaxValue;
            }

            // A note held a little into its hand's next one (legato) is cut to it further on: here it's one held in full.
            var following = From(n.On + 1);
            var legato = following < n.Off && n.Off - following <= Math.Min(120, (n.Off - n.On) / 4);
            return (Note: n, Next: legato ? n.Off : From(n.Off));
        }).ToList();
        var near = reaching.Where(r => r.Next - r.Note.Off <= (r.Note.Off - r.Note.On) / 5).ToList();
        var offPlace = near.Where(r => r.Note.Off % 120 is > 2 and < 118 && r.Note.Off % 160 is > 2 and < 158);
        var shares = offPlace.Select(r => (r.Note.Off - r.Note.On) / (double)(r.Next - r.Note.On)).Where(share => share < 0.99).Order().ToList();
        if (shares.Count >= 3 && shares.Count * 50 >= near.Count)
        {
            var share = shares[shares.Count / 2];
            heard = [.. reaching.Select(r =>
            {
                var (n, next) = r;
                var held = n.Off - n.On;
                var full = n.On + held / share;
                var last = Math.Min(next, n.Off + held / 5);
                var first = (n.Off / Ticks.Eighth + 1) * Ticks.Eighth;
                var halfBeats = Enumerable.Range(0, (int)((last - first + Ticks.Eighth) / Ticks.Eighth)).Select(k => first + k * Ticks.Eighth);
                return n with { Off = halfBeats.Append(last == next ? next : n.Off).MinBy(p => Math.Abs(p - full)) };
            })];
        }

        // Each beat's grid: the one its starts and ends fit best, a finer grid paying a little per note, so a beat that fits
        // sixteenths stays in sixteenths. 160 and 80 are triplet eighths and triplet sixteenths.
        (int Grid, int Cost)[] candidates = [(120, 0), (160, 6), (60, 4), (80, 10)];
        var grids = heard.SelectMany(n => new[] { n.On, n.Off }).GroupBy(t => t / Ticks.Quarter).ToDictionary(g => g.Key, g => candidates
            .MinBy(c => g.Sum(t => Math.Min(t % Ticks.Quarter % c.Grid, c.Grid - t % Ticks.Quarter % c.Grid) + c.Cost)).Grid);
        int GridAt(long tick) => grids.GetValueOrDefault(tick / Ticks.Quarter, 120);
        long Snap(long tick) => tick / Ticks.Quarter * Ticks.Quarter + (long)Math.Round(tick % Ticks.Quarter / (double)GridAt(tick), MidpointRounding.AwayFromZero) * GridAt(tick);
        var notes = heard.Select(n => (n.Track, n.Channel, n.Midi, On: Snap(n.On), Off: Snap(n.Off))).Select(n => n with { Off = Math.Max(n.Off, n.On + GridAt(n.On)) }).ToList();

        // The hands: two or more tracks (or channels) split by how high each lies; a single one at middle C.
        var parts = notes.GroupBy(n => (n.Track, n.Channel)).Select(g => (g.Key, Mean: g.Average(n => n.Midi))).OrderByDescending(p => p.Mean).ToList();
        var right = parts.Where(p => p.Mean >= 60).Select(p => p.Key).ToHashSet();
        if (right.Count == 0 || right.Count == parts.Count) right = parts.Count > 1 ? [parts[0].Key] : [];

        // The bars, from the time signatures: one that falls inside a bar starts at the next.
        var meters = midi.Meters.Where(m => m.Count > 0 && m.Unit is 1 or 2 or 4 or 8 or 16 or 32).Select(m => (Tick: Scale(m.Tick), Meter: new Meter(m.Count, m.Unit))).ToList();
        var bars = new List<(long Start, Meter Meter)>();
        var lastEnd = notes.Max(n => n.Off);
        for (var start = 0L; start < lastEnd; start += bars[^1].Meter.BarTicks)
        {
            bars.Add((start, meters.LastOrDefault(m => m.Tick <= start, (0, new Meter(4, 4))).Meter));
        }

        // The key: the file's signature, or the key whose profile the notes fit best (their correlation, by the profile's spread).
        var keys = midi.Keys.Select(k => (Tick: Scale(k.Tick), Key: new Key(Math.Clamp(k.Fifths, -7, 7), k.Minor))).ToList();
        if (keys.Count == 0)
        {
            var weight = new double[12];
            foreach (var n in notes) weight[n.Midi % 12] += n.Off - n.On;
            double Fit(double[] profile, int tonic) => Enumerable.Range(0, 12).Sum(d => weight[(d + tonic) % 12] * (profile[d] - profile.Average())) /
                Math.Sqrt(profile.Sum(p => (p - profile.Average()) * (p - profile.Average())));
            var best = Enumerable.Range(0, 24).MaxBy(k => Fit(k < 12 ? MajorProfile : MinorProfile, k % 12));
            var fifths = ((best % 12 + (best < 12 ? 0 : 3)) * 7 % 12 + 12) % 12;
            keys = [(0, new Key(fifths > 6 ? fifths - 12 : fifths, best >= 12))];
        }
        Key KeyAt(long tick) => keys.LastOrDefault(k => k.Tick <= tick, keys[0]).Key;

        // Each hand's notes as chords (the same start and end), in up to two voices. A chord goes to the first voice that's free
        // when it starts; with both busy it cuts the earlier-ending voice's chord short, or joins it when they start together.
        // A note held a little into the next one (legato) is cut to it first.
        var voicesOf = new Dictionary<int, List<Chord>[]>();
        foreach (var staff in new[] { 1, 2 })
        {
            var own = notes.Where(n => (parts.Count > 1 ? right.Contains((n.Track, n.Channel)) : n.Midi >= 60) == (staff == 1)).OrderBy(n => n.On).ToList();
            var onsets = own.Select(n => n.On).Distinct().Order().ToList();
            for (var i = 0; i < own.Count; i++)
            {
                var after = onsets.BinarySearch(own[i].On + 1);
                var next = (after < 0 ? ~after : after) is var at && at < onsets.Count ? onsets[at] : long.MaxValue;
                if (own[i].Off > next && own[i].Off - next <= Math.Min(120, (own[i].Off - own[i].On) / 4)) own[i] = own[i] with { Off = next };
            }

            List<Chord>[] voices = [[], []];
            foreach (var group in own.GroupBy(n => (n.On, n.Off)).OrderBy(g => g.Key.On).ThenByDescending(g => g.Max(n => n.Midi)))
            {
                var chord = new Chord(group.Key.On, group.Key.Off, [.. group.Select(n => n.Midi).Distinct().OrderDescending()]);
                var free = Array.FindIndex(voices, v => v.Count == 0 || v[^1].Off <= chord.On);
                if (free >= 0)
                {
                    voices[free].Add(chord);
                    continue;
                }

                var busy = voices[0][^1].Off <= voices[1][^1].Off ? 0 : 1;
                var held = voices[busy][^1];
                if (held.On == chord.On) voices[busy][^1] = held with { Off = Math.Min(held.Off, chord.Off), Keys = [.. held.Keys.Union(chord.Keys).OrderDescending()] };
                else
                {
                    voices[busy][^1] = held with { Off = chord.On };
                    voices[busy].Add(chord);
                }
            }
            voicesOf[staff] = voices;
        }

        // Bar by bar: each voice's chords clipped to the bar and rests between them (hidden in the second voice), each split into
        // values that can be written, and tied where a note goes on.
        var count = 0;
        var built = new List<Bar>();
        for (var b = 0; b < bars.Count; b++)
        {
            var (barStart, meter) = bars[b];
            var barEnd = barStart + meter.BarTicks;
            var key = KeyAt(barStart);
            var voices = new List<Voice>();
            foreach (var staff in new[] { 1, 2 })
            {
                for (var layer = 0; layer < 2; layer++)
                {
                    var inBar = voicesOf[staff][layer].Where(c => c.Off > barStart && c.On < barEnd).ToList();
                    if (layer == 1 && inBar.Count == 0) continue;

                    // A beat is in triplets in this voice when a note starts or ends inside it off the beat's sixteenths and
                    // thirty-seconds: 3 in 2 on triplet eighths, 6 in 4 on triplet sixteenths.
                    var inside = inBar.SelectMany(c => new[] { c.On, c.Off }).Where(t => t > barStart && t < barEnd && t % Ticks.Quarter != 0).GroupBy(t => t / Ticks.Quarter);
                    var triplets = inside.Where(g => g.Any(t => t % Ticks.Quarter % 60 != 0)).ToDictionary(g => g.Key, g => g.All(t => t % Ticks.Quarter % 160 == 0) ? new Tuplet(3, 2) : new Tuplet(6, 4));

                    // Splits a stretch of the bar into notes or rests that can each be written: within a triplet beat, in its
                    // tuplet's values; off the beat, up to the next beat; on the beat, as long as it can go short of a triplet beat.
                    IEnumerable<(int Start, int Length, Tuplet? Tuplet)> Split(int from, int to)
                    {
                        var at = from;
                        while (at < to)
                        {
                            var beat = (barStart + at) / Ticks.Quarter;
                            var beatEnd = (int)((beat + 1) * Ticks.Quarter - barStart);
                            int length;
                            if (triplets.TryGetValue(beat, out var tuplet))
                            {
                                var room = Math.Min(to, beatEnd) - at;
                                var grid = Ticks.Quarter / tuplet.Actual;
                                length = Values.Select(v => v * tuplet.Normal / tuplet.Actual).FirstOrDefault(v => v <= room && v % grid == 0, grid);
                                yield return (at, length, tuplet);
                            }
                            else
                            {
                                var limit = triplets.Keys.Where(t => t > beat).Select(t => (int)(t * Ticks.Quarter - barStart)).Where(t => t < to).DefaultIfEmpty(to).Min();
                                var reach = at % meter.BeatTicks == 0 ? limit : Math.Min(limit, (at / meter.BeatTicks + 1) * meter.BeatTicks);
                                length = Values.FirstOrDefault(v => v <= reach - at, 60);
                                yield return (at, length, null);
                            }
                            at += length;
                        }
                    }

                    var events = new List<Event>();
                    var position = 0;
                    foreach (var chord in inBar.Append(new Chord(barEnd, barEnd, [])))
                    {
                        // The rest before it: a bar's rest when the voice is silent the whole bar.
                        var from = (int)Math.Max(0, chord.On - barStart);
                        if (position == 0 && from == meter.BarTicks) events.Add(new Rest($"r{++count}", 0, meter.BarTicks) { Hidden = layer == 1 });
                        else foreach (var (start, length, tuplet) in Split(position, from)) events.Add(new Rest($"r{++count}", start, length) { Hidden = layer == 1, Tuplet = tuplet });
                        if (chord.Keys.Count == 0) break;

                        var to = (int)Math.Min(meter.BarTicks, chord.Off - barStart);
                        var pieces = Split(from, to).ToList();
                        for (var p = 0; p < pieces.Count; p++)
                        {
                            var id = $"n{++count}";
                            var tiedIn = p > 0 || chord.On < barStart;
                            var tiedOn = p < pieces.Count - 1 || chord.Off > barEnd;
                            var tones = chord.Keys.Select((k, j) => new Tone(chord.Keys.Count == 1 ? id : $"{id}-{j}", key.Spell(k)) { TiedFromPrevious = tiedIn, TieToNext = tiedOn });
                            events.Add(new Notes(id, pieces[p].Start, pieces[p].Length, [.. tones]) { Tuplet = pieces[p].Tuplet });
                        }
                        position = to;
                    }
                    voices.Add(new Voice(staff, events, layer + 1));
                }
            }

            // The file's tempo, as the piece's own: on the first bar, and only the first (a file's later tempos can be a whole
            // performance's give and take, which a click shouldn't follow).
            built.Add(new Bar(b + 1, voices)
            {
                NewKey = b > 0 && KeyAt(bars[b - 1].Start) != key ? key : null,
                NewMeter = b > 0 && bars[b - 1].Meter != meter ? meter : null,
                Tempos = b == 0 && midi.MarkedTempo is { } tempo and >= 20 and <= 300 ? [(0, tempo)] : [],
            });
        }

        // The first track's name is the file's own; the others name a hand or a voice ("one", "up").
        var named = midi.Names.Where(n => n.Track == 0).Select(n => n.Name).FirstOrDefault(n => n.Length > 1 && n.Any(char.IsLetter) && !Generic.Any(g => n.Contains(g, StringComparison.OrdinalIgnoreCase)));
        return new Score(KeyAt(0), bars[0].Meter, built) { Title = named ?? fallbackTitle };
    }
}
