using Prima.Core.Music;
using Prima.Core.Transcription;

namespace Prima.Core.Library;

/// <summary>
/// Notation made from a MIDI file, so one can be imported and read. A MIDI file has the notes and when they sound, not how they're
/// written, so this works the writing out:
/// <list type="bullet">
/// <item>The hands come from its tracks (the higher of two is the right hand), or in a single track from middle C.</item>
/// <item>The bars come from its time signatures, the key from its key signature (less a flat or sharp its notes never play), or
/// from the notes when it has none. A first bar in a shorter time signature of its own is a pickup, bar 0.</item>
/// <item>Where the file lets its notes go a little early, as notation software does, each is written at the length it would
/// have held in full.</item>
/// <item>A small note played just before another a step away, over the notes before it, is a grace note.</item>
/// <item>Each hand's quarter-note beats are snapped to sixteenths, thirty-seconds or triplets, whichever fits that hand's notes
/// there best, so one hand's triplets stay triplets against the other's eighths.</item>
/// <item>Notes a hand holds over others make a second voice; notes are tied across barlines, and off the beat across beats,
/// but one from the half beat stays whole within its half of the bar.</item>
/// <item>The sustain pedal becomes the score's pedal marks, which <see cref="Mei"/> writes out.</item>
/// </list>
/// A file exported from notation software comes out close to its score. One recorded from playing comes out ragged, and a pickup
/// written from the bar's start comes out as a whole bar with rests.
/// </summary>
public static class MidiNotation
{
    // Note values a single note can take, longest first: whole to thirty-second, plain or dotted.
    private static readonly int[] Values = [1920, 1440, 960, 720, 480, 360, 240, 180, 120, 90, 60];

    // Names that name an instrument or a track, not the piece.
    private static readonly string[] Generic = ["piano", "track", "untitled", "staff", "midi", "grand", "right", "left", "hand", "voice", "instrument", "sequence"];

    // The notes a key signature's flats and sharps alter, in the order they're added, as pitch classes: B E A D G C F, F C G D A E B.
    private static readonly int[] FlatOrder = [11, 4, 9, 2, 7, 0, 5];
    private static readonly int[] SharpOrder = [5, 0, 7, 2, 9, 4, 11];

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
        var shift = late != 0 && Near(late) > 2 * Near(0) && Near(late) * 3 >= heard.Count ? late : 0;
        if (shift > 0) heard = [.. heard.Select(n => n with { On = Math.Max(0, n.On - shift), Off = Math.Max(1, n.Off - shift) })];

        // The hands: two or more tracks (or channels) split by how high each lies; a single one at middle C.
        var parts = heard.GroupBy(n => (n.Track, n.Channel)).Select(g => (g.Key, Mean: g.Average(n => n.Midi))).OrderByDescending(p => p.Mean).ToList();
        var right = parts.Where(p => p.Mean >= 60).Select(p => p.Key).ToHashSet();
        if (right.Count == 0 || right.Count == parts.Count) right = parts.Count > 1 ? [parts[0].Key] : [];
        int StaffOf(TickNote n) => (parts.Count > 1 ? right.Contains((n.Track, n.Channel)) : n.Midi >= 60) ? 1 : 2;

        // The grace notes leave the time, to be written before the notes they lean on.
        var leaning = Graces(heard, StaffOf);
        var graces = leaning.Select(g => (Staff: StaffOf(heard[g.Key]), heard[g.Key].Midi, Main: g.Value)).ToList();
        if (leaning.Count > 0) heard = [.. heard.Where((_, i) => !leaning.ContainsKey(i))];

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

        // Each hand's grid in each beat: of those its notes there start on exactly, if any (as in a file from notation software),
        // or else of all, the one its starts and ends there fit best, a finer grid paying a little per note, so a beat that fits
        // sixteenths stays in sixteenths. An end alone doesn't make a beat triplets: a note can be let go anywhere. A hand has
        // its own grid, as each staff has its own tuplets: one for both would put one hand's triplets on thirty-seconds against
        // the other's eighths. 160 and 80 are triplet eighths and triplet sixteenths.
        (int Grid, int Cost)[] candidates = [(120, 0), (160, 6), (60, 4), (80, 10)];
        static long Away(long tick, int grid) => Math.Min(tick % Ticks.Quarter % grid, grid - tick % Ticks.Quarter % grid);
        var grids = heard.SelectMany(n => new[] { (Staff: StaffOf(n), Tick: n.On, Start: true), (Staff: StaffOf(n), Tick: n.Off, Start: false) })
            .GroupBy(t => (t.Staff, t.Tick / Ticks.Quarter)).ToDictionary(g => g.Key, g =>
            {
                var exact = candidates.Where(c => g.Where(t => t.Start).All(t => Away(t.Tick, c.Grid) <= 2)).ToList();
                return (exact.Count > 0 ? exact : [.. candidates]).MinBy(c => g.Sum(t => Away(t.Tick, c.Grid) + c.Cost)).Grid;
            });
        int GridAt(int staff, long tick) => grids.GetValueOrDefault((staff, tick / Ticks.Quarter), 120);
        long Snap(int staff, long tick) => tick / Ticks.Quarter * Ticks.Quarter + (long)Math.Round(tick % Ticks.Quarter / (double)GridAt(staff, tick), MidpointRounding.AwayFromZero) * GridAt(staff, tick);
        var notes = heard.Select(n => (Staff: StaffOf(n), n.Midi, n.On, n.Off)).Select(n => n with { On = Snap(n.Staff, n.On), Off = Snap(n.Staff, n.Off) })
            .Select(n => n with { Off = Math.Max(n.Off, n.On + GridAt(n.Staff, n.On)) }).ToList();

        // The bars, from the time signatures: one that falls inside a bar starts at the next. A first bar in a time signature of
        // its own, shorter than the next one's and used by no other bar, is a pickup to it, as notation software writes one (a
        // 1/4 bar before the 2/4): bar 0, so the bars are numbered as printed. A metre the music comes back to is its own, as
        // in Mussorgsky's Promenade, which starts with a bar of 5/4 before one of 6/4.
        var meters = midi.Meters.Where(m => m.Count > 0 && m.Unit is 1 or 2 or 4 or 8 or 16 or 32).Select(m => (Tick: Scale(m.Tick), Meter: new Meter(m.Count, m.Unit))).ToList();
        var bars = new List<(long Start, Meter Meter)>();
        var lastEnd = notes.Max(n => n.Off);
        for (var start = 0L; start < lastEnd; start += bars[^1].Meter.BarTicks)
        {
            bars.Add((start, meters.LastOrDefault(m => m.Tick <= start, (0, new Meter(4, 4))).Meter));
        }
        var pickup = bars.Count > 1 && bars[0].Meter.BarTicks < bars[1].Meter.BarTicks && bars.Skip(1).All(b => b.Meter != bars[0].Meter);

        // The key: the file's signature, or the key whose profile the notes fit best (their correlation, by the profile's spread).
        Key Fitted(IEnumerable<(int Staff, int Midi, long On, long Off)> among)
        {
            var weight = new double[12];
            foreach (var n in among) weight[n.Midi % 12] += n.Off - n.On;
            double Fit(double[] profile, int tonic) => Enumerable.Range(0, 12).Sum(d => weight[(d + tonic) % 12] * (profile[d] - profile.Average())) /
                Math.Sqrt(profile.Sum(p => (p - profile.Average()) * (p - profile.Average())));
            var best = Enumerable.Range(0, 24).MaxBy(k => Fit(k < 12 ? MajorProfile : MinorProfile, k % 12));
            var fifths = ((best % 12 + (best < 12 ? 0 : 3)) * 7 % 12 + 12) % 12;
            return new Key(fifths > 6 ? fifths - 12 : fifths, best >= 12);
        }

        // A signature its notes contradict gives way to them: where its last flat or sharp is never played and the note it alters
        // is played natural, and often, it has one too many (as many times as that holds), when the key the notes fit best agrees.
        // A file from musescore.com says four flats over music with D after D and not one D flat. A real signature stays: its
        // last flat or sharp is played, or the notes fit its key, as Burgmüller's Ballade in C minor fits its three flats with
        // every A raised.
        var keys = midi.Keys.Select(k => (Tick: Scale(k.Tick), Key: new Key(Math.Clamp(k.Fifths, -7, 7), k.Minor))).GroupBy(k => k.Tick).Select(g => g.Last()).ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            var (from, to) = (keys[i].Tick, i + 1 < keys.Count ? keys[i + 1].Tick : long.MaxValue);
            var region = notes.Where(n => n.On >= from && n.On < to).ToList();
            var played = new int[12];
            foreach (var n in region) played[n.Midi % 12]++;
            var key = keys[i].Key;
            while (key.Fifths != 0)
            {
                var natural = key.Fifths < 0 ? FlatOrder[-key.Fifths - 1] : SharpOrder[key.Fifths - 1];
                var altered = (natural + (key.Fifths < 0 ? 11 : 1)) % 12;
                if (played[altered] > 0 || played[natural] < Math.Max(8, region.Count / 50)) break;
                key = key with { Fifths = key.Fifths - Math.Sign(key.Fifths) };
            }
            if (key != keys[i].Key && Fitted(region).Fifths == key.Fifths) keys[i] = (keys[i].Tick, key);
        }
        if (keys.Count == 0) keys = [(0, Fitted(notes))];
        Key KeyAt(long tick) => keys.LastOrDefault(k => k.Tick <= tick, keys[0]).Key;

        // Each hand's notes as chords (the same start and end), in up to two voices. A chord goes to the first voice that's free
        // when it starts; with both busy it cuts the earlier-ending voice's chord short, or joins it when they start together.
        // A note held a little into the next one (legato) is cut to it first.
        var voicesOf = new Dictionary<int, List<Chord>[]>();
        foreach (var staff in new[] { 1, 2 })
        {
            var own = notes.Where(n => n.Staff == staff).OrderBy(n => n.On).ToList();
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

        // Each grace note goes before the chord it leans on, in that chord's voice; several before one chord make a chord of them.
        var gracesBefore = new Dictionary<(int Staff, int Layer, long On), List<int>>();
        foreach (var group in graces.GroupBy(g => (g.Staff, On: Snap(g.Staff, g.Main.On))))
        {
            var (staff, on) = group.Key;
            var voices = voicesOf[staff];
            var layer = Array.FindIndex(voices, v => v.Any(c => c.On == on && c.Keys.Any(k => group.Any(g => g.Main.Key == k))));
            if (layer < 0) layer = Array.FindIndex(voices, v => v.Any(c => c.On == on));
            if (layer >= 0) gracesBefore[(staff, layer, on)] = [.. group.Select(g => g.Midi).Distinct().OrderDescending()];
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
                    // tuplet's values; on the beat, as long as it can go short of a triplet beat; off the beat, up to the next beat.
                    // But a note from the half beat is one note over the next beat when it ends by the beat after, within its half
                    // of the bar (in 2/4 and 3/4, the bar), as printed music writes an eighth, a quarter and an eighth: the bar's
                    // middle still shows.
                    var half = meter.Compound ? 0 : meter.Count switch { 2 or 3 => meter.BarTicks, 4 => meter.BarTicks / 2, _ => 0 };
                    IEnumerable<(int Start, int Length, Tuplet? Tuplet)> Split(int from, int to, bool note)
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
                                if (note && half > 0 && at % meter.BeatTicks == meter.BeatTicks / 2 && limit <= Math.Min((at / half + 1) * half, (at / meter.BeatTicks + 2) * meter.BeatTicks)
                                    && Values.Contains(limit - at)) reach = limit;
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
                        else foreach (var (start, length, tuplet) in Split(position, from, note: false)) events.Add(new Rest($"r{++count}", start, length) { Hidden = layer == 1, Tuplet = tuplet });
                        if (chord.Keys.Count == 0) break;

                        var to = (int)Math.Min(meter.BarTicks, chord.Off - barStart);
                        var pieces = Split(from, to, note: true).ToList();

                        // Its grace notes first, printed as small eighths: in its triplet when the triplet starts before it.
                        if (chord.On >= barStart && gracesBefore.TryGetValue((staff, layer, chord.On), out var small))
                        {
                            var id = $"n{++count}";
                            var tuplet = pieces[0].Tuplet is { } running && events.Count > 0 && ReferenceEquals(events[^1].Tuplet, running) ? running : null;
                            var tones = small.Select((k, j) => new Tone(small.Count == 1 ? id : $"{id}-{j}", key.Spell(k)));
                            events.Add(new Notes(id, from, Ticks.Eighth * (tuplet?.Normal ?? 1) / (tuplet?.Actual ?? 1), [.. tones]) { Grace = true, Tuplet = tuplet });
                        }

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
            built.Add(new Bar(pickup ? b : b + 1, voices)
            {
                Length = pickup && b == 0 ? meter.BarTicks : null,
                NewKey = b > 0 && KeyAt(bars[b - 1].Start) != key ? key : null,
                NewMeter = b > (pickup ? 1 : 0) && bars[b - 1].Meter != meter ? meter : null,
                Tempos = b == 0 && midi.MarkedTempo is { } tempo and >= 20 and <= 300 ? [(0, tempo)] : [],
            });
        }

        // The sustain pedal, as the piece's own marks: each press and lift, from the track (and channel) with the most of them, as
        // a file can give each track a copy. Each goes to the note it comes with when one starts within a sixty-fourth of it, as
        // notation software writes a pedal change a tick or two after its note, or else onto the bass staff's grid. A lift and a
        // press at one note is a change.
        var pedal = new List<Mark>();
        var starts = notes.Select(n => n.On).Distinct().Order().ToList();
        var down = false;
        foreach (var (_, _, tick, press) in midi.Pedals.Where(p => p.Channel != 9).GroupBy(p => (p.Track, p.Channel)).MaxBy(g => g.Count())?.ToList() ?? [])
        {
            if (press == down) continue;
            down = press;

            var at = Math.Max(0, Scale(tick) - shift);
            var after = From(starts, at);
            var nearest = new[] { after - 1, after }.Where(i => i >= 0 && i < starts.Count).Select(i => starts[i]).Where(o => Math.Abs(o - at) <= Ticks.Quarter / 16).OrderBy(o => Math.Abs(o - at)).ToList();
            at = Math.Min(nearest.Count > 0 ? nearest[0] : Snap(2, at), bars[^1].Start + bars[^1].Meter.BarTicks);
            var b = bars.FindLastIndex(x => x.Start <= at);
            if (Anchor(built[b], (int)(at - bars[b].Start)) is { } anchor) pedal.Add(new Mark(press ? MarkKind.PedalDown : MarkKind.PedalUp, anchor.Id) { StartOffset = anchor.Offset });
        }

        // The first track's name is the file's own; the others name a hand or a voice ("one", "up").
        var named = midi.Names.Where(n => n.Track == 0).Select(n => n.Name).FirstOrDefault(n => n.Length > 1 && n.Any(char.IsLetter) && !Generic.Any(g => n.Contains(g, StringComparison.OrdinalIgnoreCase)));
        return new Score(KeyAt(0), bars[pickup ? 1 : 0].Meter, built) { Title = named ?? fallbackTitle, FileMarks = pedal };
    }

    /// <summary>
    /// The notation as MEI for Verovio, with the score's file marks (its pedal) as <see cref="ReadingScore.Mei"/> writes them, where
    /// <see cref="MeiWriter"/> alone would leave them out.
    /// </summary>
    public static string Mei(ReadOnlySpan<byte> data, string fallbackTitle) => ReadingScore.Mei(Read(data, fallbackTitle));

    // The note a mark at a tick of a bar is tied to, and the ticks from it to the mark: the last at or before it (or else the first)
    // on the bass staff, among the notes struck there when there are any, as MeiReader ties a mark placed by the beat.
    private static (string Id, long Offset)? Anchor(Bar bar, int tick)
    {
        var played = bar.Voices.SelectMany(v => v.Events.OfType<Notes>().Where(n => !n.Grace).Select(n => (v.Staff, Notes: n))).ToList();
        static bool Struck((int Staff, Notes Notes) p) => p.Notes.Tones.Any(t => !t.TiedFromPrevious);
        var pool = new[] { played.Where(p => p.Staff == 2 && Struck(p)), played.Where(Struck), played }.Select(c => c.Select(p => p.Notes).ToList()).FirstOrDefault(c => c.Count > 0);
        if (pool is null) return null;

        var anchor = pool.Where(n => n.Start <= tick).MaxBy(n => n.Start) ?? pool.MinBy(n => n.Start)!;
        return (anchor.Tones.Count == 1 ? anchor.Tones[0].Id : anchor.Id, tick - anchor.Start);
    }

    // The index of the first tick at or after one in a sorted list (the list's length when there's none).
    private static int From(List<long> sorted, long tick)
    {
        var (low, high) = (0, sorted.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle] < tick) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    // Whether a tick is on a sixteenth's or a triplet eighth's place in its beat, give or take two.
    private static bool OnPlace(long tick) => tick % 120 is <= 2 or >= 118 || tick % 160 is <= 2 or >= 158;

    /// <summary>
    /// The grace notes among a file's notes, by index, each with the note it leans on: when it starts and its key. A grace note is
    /// played just before that note, over what its hand struck before it, which sounds on to that note, as Musicnotes' and
    /// LilyPond's files play a small note before the beat. So it's no longer than a thirty-second, starts off the beat's sixteenths
    /// and triplets, and ends where a longer note of its hand starts on its place; everything its hand struck last before it sounds
    /// to its end, nothing else starts with it but other grace notes, and nothing of its hand ends where it starts, as the note
    /// before a written thirty-second would. And it leans on a note a step away, a semitone or a tone, as a figure's quick notes
    /// played over a held one needn't (Chopin's Prelude in F sharp minor is full of them); grace notes struck together lean as one.
    /// </summary>
    private static Dictionary<int, (long On, int Key)> Graces(List<TickNote> heard, Func<TickNote, int> staffOf)
    {
        var graces = new Dictionary<int, (long, int)>();
        foreach (var hand in Enumerable.Range(0, heard.Count).GroupBy(i => staffOf(heard[i])))
        {
            var own = hand.OrderBy(i => heard[i].On).ToList();
            var ons = own.Select(i => heard[i].On).ToList();
            var offs = own.Select(i => heard[i].Off).Order().ToList();
            var found = new List<(int Index, List<TickNote> Mains)>();
            foreach (var i in own)
            {
                var grace = heard[i];
                if (grace.Off - grace.On is > Ticks.Quarter / 8 or <= 2 || OnPlace(grace.On)) continue;

                var mains = Enumerable.Range(From(ons, grace.Off - 2), own.Count).TakeWhile(k => k < own.Count && ons[k] <= grace.Off + 2).Select(k => heard[own[k]])
                    .Where(m => m.Off - m.On > Ticks.Quarter / 8 && OnPlace(m.On)).ToList();
                var alongside = Enumerable.Range(From(ons, grace.On - 2), own.Count).TakeWhile(k => k < own.Count && ons[k] <= grace.On + 2).Select(k => heard[own[k]]);
                var before = From(ons, grace.On - 2) - 1;
                var struck = Enumerable.Range(0, before + 1).Reverse().TakeWhile(k => ons[k] >= ons[before] - 2).Select(k => heard[own[k]]);
                var endsAtStart = From(offs, grace.On - 2) is var e && e < offs.Count && offs[e] <= grace.On + 2;
                if (mains.Count == 0 || before < 0 || endsAtStart || alongside.Any(n => Math.Abs(n.Off - grace.Off) > 2) || struck.Any(n => Math.Abs(n.Off - grace.Off) > 2)) continue;

                found.Add((i, mains));
            }

            foreach (var together in found.GroupBy(f => (heard[f.Index].On, heard[f.Index].Off)))
            {
                var steps = together.SelectMany(f => f.Mains.Select(m => (Step: Math.Abs(m.Midi - heard[f.Index].Midi), Main: m))).Where(s => s.Step is 1 or 2).ToList();
                if (steps.Count == 0) continue;

                var main = steps.MinBy(s => s.Step).Main;
                foreach (var f in together) graces[f.Index] = (main.On, main.Midi);
            }
        }
        return graces;
    }
}
