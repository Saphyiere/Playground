# Sight-reading and piano practice: what the evidence says

This report gathers what research and the main exam boards say about learning to sight-read and building clean technique. It is
meant to guide the design of a personal practice app: a Lenovo Android tablet running .NET MAUI Blazor, played on a MIDI
keyboard, or through the tablet microphone for single notes. The app's screens are Today, Drills, Sight-read, Library, Tools and
You.

**How sure we can be.** Most music-practice research is small. Studies often have 10 to 60 people, are correlational, and
are rarely repeated by another lab. Each section says how much weight a finding can carry. The labels are:

- **Strong**: a meta-analysis, or several studies that agree.
- **Moderate**: one or a few decent controlled studies.
- **Weak**: a single small study, a survey, or a correlation only.
- **Tradition**: teaching books and expert opinion, with no data.
- **Weak source**: app marketing, company blogs, review sites, forums.
- **[K]**: the researcher cited it from memory with its DOI, and it was not re-checked. Check the exact numbers before quoting them.
- **[unverified]**: the paper exists, but this particular detail wasn't confirmed in a source the researchers read.

---

## Summary: ten findings that matter most for the app

1. **Reading a lot of easy music in time is the main thing that works.** In 92 studies, sight-reading improved in control
   groups as well as treatment groups (within-group d ≈ 0.48). Specific methods added only a small extra effect on top
   (d ≈ 0.18) ([Mishra 2014](https://doi.org/10.1177/0305735612463770)). The best predictor of skill is how much sight-reading
   and accompanying someone has done, not their general practice hours
   ([Lehmann & Ericsson 1996](https://doi.org/10.1037/h0094082); [Kopiez & Lee 2008](https://doi.org/10.1080/14613800701871363)).
   So the Sight-read screen is the core of the app. Daily volume of fresh material at a level the owner can play without
   stopping is the main lever.

2. **A few extras beat plain reading:** ear training, "controlled reading" (paced or limited views of the music), creative work
   (improvising), and singing ([Mishra 2014](https://doi.org/10.1177/0305735612463770)). For rhythm, counting plus movement or
   drill helps. Colour-coded or simplified notation does not help
   ([Mishra 2016](https://doi.org/10.1177/0305735615610925)). The best piano program combined rhythm training, reading in
   several styles, and duet playing, and it beat each of those on its own
   ([Zhukov et al. 2016](https://doi.org/10.1177/0305735614550229)).

3. **Good readers see patterns, so generate musical material, not random notes.** Experts read in chunks: scales, chord
   shapes, tonal patterns. Their advantage shrinks on atonal music
   ([Perra et al. 2025b](https://doi.org/10.1037/xlm0001358); [Sloboda 1984](https://doi.org/10.2307/40285292)). The eyes
   run about **1 second** ahead of the hands at every skill level. Experts fit more notes into that second
   ([Furneaux & Land 1999](https://doi.org/10.1098/rspb.1999.0943); [Penttinen et al. 2015](https://doi.org/10.1177/0255761413515813)).
   Any read-ahead or hiding mode must leave at least about 1 s of music visible.

4. **Note drills should aim at an automatic "see the note, press the key" response.** Measure response time per note, not
   only right or wrong ([Stewart et al. 2003](https://research.gold.ac.uk/id/eprint/51)). Schedule review with spacing, and
   mix confusable notes (interleaving). Both ideas rest on strong general-learning evidence. Neither has been tested on note
   reading itself ([Cepeda et al. 2008](https://pubmed.ncbi.nlm.nih.gov/19076480/);
   [Brunmair & Richter 2019](https://pubmed.ncbi.nlm.nih.gov/31556629/)).

5. **Rhythm is where sight-reading breaks.** Rhythm reading was the best single predictor of sight-reading in one study
   ([Elliott 1982](https://doi.org/10.2307/3344862)). An older analysis found more than half of sight-reading errors were rhythm
   errors ([Boyle 1970](https://doi.org/10.2307/3344498), citing earlier work). Give rhythm its own drill (RCM tests
   it separately at every level). Grade timing as a **percentage of the beat**. Listeners notice a single shifted note at
   about 2.5–5% of the interval ([Friberg & Sundberg 1995](https://doi.org/10.1121/1.413218)), and the sense of a steady pulse
   starts to break at about 8.6% ([Madison & Merker 2002](https://doi.org/10.1007/s00426-001-0085-y)).

6. **Show feedback after the phrase, not live while playing.** Two controlled studies found live visual timing feedback did not
   improve timing, and the authors suspect it adds mental load
   ([Sadakata et al. 2008](https://doi.org/10.1080/09298210802322401); [Brandmeyer et al. 2011](https://doi.org/10.1007/s00426-010-0291-6)).
   Motor-learning research also favours summaries and fading feedback, with a caveat for complex skills
   ([Winstein & Schmidt 1990](https://doi.org/10.1037/0278-7393.16.4.677); [Wulf & Shea 2002](https://doi.org/10.3758/bf03196276)).
   Precise millisecond feedback after each try *did* help skilled pianists on a short, isolated passage
   ([Kimoto et al. 2025](https://doi.org/10.1038/s42003-025-07562-6)).

7. **The exam boards give a ready-made difficulty ladder.** ABRSM, Trinity and RCM publish grade-by-grade tables: keys, time
   signatures, note values, hand position, texture, length. They add one kind of difficulty at a time, and once something is
   introduced it stays ([ABRSM 2027–28](https://www.abrsm.org/sites/default/files/2026-06/Piano%20Practical%20Grades%20Qualification%20Specification%202027%20&%202028.pdf);
   [Trinity](https://www.trinitycollege.com/resource?id=9079);
   [RCM 2022](https://teacherportal.rcmusic.com/getattachment/57f3734d-97e5-4777-b67e-4b1111ee31a3/piano-syllabus-2022-edition.pdf)).
   A merged 12-level ladder (in section 6) can drive the generator. No study compares ways of stepping up difficulty.

8. **"Smooth, even, clean" is measurable from MIDI, and researchers have done it.** The standard is MIDI scale analysis: the
   spread (SD) of the gaps between notes in a fast scale. Healthy professionals score about **8–9 ms at a 125 ms note gap**.
   Listeners start to hear unevenness at about **10 ms** ([van Vugt et al. 2013](https://doi.org/10.3389/fpsyg.2013.00134);
   [Jabusch et al. 2004](https://doi.org/10.1002/mds.10671)). Thumb crossings and octave boundaries are predictable weak spots
   ([van Vugt et al. 2014](https://doi.org/10.1007/s00221-014-4036-4); [van Vugt et al. 2012](https://doi.org/10.3389/fpsyg.2012.00495)).
   Legato can be measured as key overlap ([Repp 1997](https://doi.org/10.1121/1.420110)).

9. **Technique traditions are untested, and practice should be short, spaced and accurate.** No controlled study tests Hanon,
   Czerny, Taubman or "weight" schools. Finger training transfers only partly to other patterns and not to the other hand
   ([Furuya et al. 2013](https://doi.org/10.1186/1471-2202-14-133)). Retention tracks the share of *correct* tries, not minutes
   or repetitions ([Duke, Simmons & Cash 2009](https://doi.org/10.1177/0022429408328851)). Playing-related pain is common in
   pianists (26–93% across studies), but its risk factors are poorly established
   ([Bragge et al. 2006](https://doi.org/10.1093/occmed/kqi177)). Breaks and caps on repetition are sensible precautions, not
   proven ones.

10. **Motivation: a forgiving streak, real progress numbers, and a plan.** A broken streak lowers motivation, less so when the
    streak can be repaired ([Silverman & Barasch 2023](https://doi.org/10.1093/jcr/ucac029)). Missing one day didn't hurt
    habit forming ([Lally et al. 2010](https://www.thebehavioralscientist.com/articles/how-long-to-form-a-habit)). "When X, I
    will Y" plans raise follow-through (d = 0.65, [Gollwitzer & Sheeran 2006](https://kops.uni-konstanz.de/entities/publication/2e749bfb-8533-437c-8203-7e788c910c5f)).
    Self-belief predicts music exam results ([McPherson & McCormick 2006](https://doi.org/10.1177/0305735606064841)), so honest
    competence feedback beats points and badges. The microphone can judge single notes from about C3 up in a quiet room after
    a latency calibration. MIDI should stay the main input for timing, chords and low notes.

---

## 1. Sight-reading: what predicts it, what training helps, how difficulty should step up

### What's known

**Practice-related skills matter most.** A meta-analysis of 92 correlational studies (597 variables) found sight-reading went
most closely with improvising, ear-training ability, technique and music knowledge. Personality and attitude were unrelated.
Skills that grow with practice predicted sight-reading better than fixed traits
([Mishra 2014, JRME](https://doi.org/10.1177/0022429413508585)). Among 16 expert pianists, the best predictors were time spent
accompanying and the size of the accompanying repertoire, not solo practice
([Lehmann & Ericsson 1996](https://doi.org/10.1037/h0094082)).

**Speed and working memory add a smaller, real part.** Kopiez and Lee tested 52 piano students. They accompanied a recorded
violin part on a MIDI piano, and a note counted as right if it matched within ±0.25 s. Four things together explained about
60% of the differences between players: trill speed, sight-reading experience up to age 15, mental processing speed, and
inner hearing ([Kopiez & Lee 2008](https://doi.org/10.1080/14613800701871363); the 59.6% comes from a summary of the abstract).
Working memory added to practice in two independent samples
([Meinz & Hambrick 2010](https://doi.org/10.1177/0956797610373933), N = 57 [unverified];
[Herrero & Carriedo 2019](https://doi.org/10.3389/fpsyg.2019.01080), N = 131). In Herrero & Carriedo, working memory uniquely
predicted *keeping the tempo*. Working memory and basic speed are largely fixed. The app can't train them. It can train the
rest.

**Keyboard geography matters.** When pianists couldn't see the keyboard, they made significantly more errors on neighbouring
notes. Taking away the sound made no difference ([Banton 1995](https://doi.org/10.1177/0305735695231001), N [unverified]).
Experts glance down less often ([Cara 2023](https://doi.org/10.16910/jemr.16.2.5), N = 22).

**The eye runs about 1 second ahead.** In 8 pianists, the eyes led the hands by about 1 s at every skill level. Professionals
fitted about 4 notes into that second; amateurs about 2. Fast tempos shortened it to 0.7 s, slow ones lengthened it to 1.3 s
([Furneaux & Land 1999](https://doi.org/10.1098/rspb.1999.0943)). A study of 38 readers found the same "about one second or
less" ([Penttinen et al. 2015](https://doi.org/10.1177/0255761413515813)). A short practice phase barely changed it
([Rosemann et al. 2016](https://doi.org/10.1177/0305735615585398), N = 9). Good readers *adjust* the span. They shorten it at
hard spots such as accidentals or big leaps, rather than always reading far ahead
([Lim et al. 2019](https://doi.org/10.1038/s41598-019-54364-y), N = 30;
[Perra et al. 2025a](https://doi.org/10.1177/17470218251325245), N = 30). A review of 16 eye-tracking studies (N from 6 to 40)
agrees: the best span is "neither too long nor too short"
([Perra et al. 2021](https://doi.org/10.16910/jemr.14.4.4)).

**Experts read patterns.** Better readers have better memory for notation and sense its structure
([Sloboda 1984](https://doi.org/10.2307/40285292)). Experts match melodic fragments faster, especially tonal ones
([Waters & Underwood 1998](https://doi.org/10.1177/0305735698261005)). The expert advantage is larger for tonal than atonal
music ([Perra et al. 2025b](https://doi.org/10.1037/xlm0001358)). Skilled pianists who had heard a piece relied less on the
printed details than novices did ([Drai-Zerbib & Baccino 2005](https://doi.org/10.3406/psy.2005.29702)). The often-told story that
pianists "correct" deliberate misprints without noticing comes from Sloboda 1976
([doi](https://doi.org/10.1037/h0082064)), but that detail is [unverified] here.

**Training that helps.** Across 92 quasi-experimental studies, interventions beat control only slightly (d ≈ 0.18). Both groups
improved a lot just by reading (d ≈ 0.48). Four kinds of treatment helped: aural training, controlled reading, creative
activities, and singing/solfège ([Mishra 2014, Psychology of Music](https://doi.org/10.1177/0305735612463770)). Most of those
studies were school or college theses, not piano-specific. For rhythm (46 studies) counting systems with movement or drill
helped. Changing the notation did not ([Mishra 2016](https://doi.org/10.1177/0305735615610925)). For piano, Zhukov's 10-week
hybrid course mixed rhythm training, solo pieces in four styles and duet playing every week. Its 25 students improved on all
four measures, more than the single-method groups from an earlier study
([Zhukov et al. 2016](https://doi.org/10.1177/0305735614550229); [Zhukov 2014](https://doi.org/10.1080/14613808.2013.819845)).
That comparison was not randomized; it compared against earlier groups. The same team ran it twice.

**How the research scores sight-reading.** Zhukov used four measures: tempo drift ("beat adjustment"), extra notes, missing
notes, and the timing error (RMS) of correct notes. Informal reading hours over 10 weeks predicted gains only in timing error
([Zhukov 2017](https://doi.org/10.1177/1029864916684193), 80 practice diaries). The research tasks don't let players stop.
They accompany a recording that keeps going.

**Stepping up difficulty.** No study compares difficulty schedules. Kopiez & Lee split pieces into five difficulty levels and
found the predictors shift with difficulty, but the per-level results weren't retrieved
([Kopiez & Lee 2006](https://doi.org/10.1080/14613800600570785)). Complexity (accidentals, distant leaps, atonal music) shortens
the eye span and slows the tempo ([Perra et al. 2021](https://doi.org/10.16910/jemr.14.4.4)). The usual advice to "read lots
of easy music" is often credited to Lehmann & McArthur's chapter in *The Science and Psychology of Music Performance* (2002).
That chapter was not retrieved [unverified].

### How sure we can be

Predictor studies are **correlational** with N between 16 and 131. The ~1 s eye span is **replicated** across several small
labs. "Reading improves reading" and "small add-on effects" rest on a **strong** meta-analysis, but its studies are mostly
not piano and mostly not peer-reviewed. Zhukov's hybrid course is **moderate**: one team, two runs, no randomization. The
step-up rules are **inference**.

---

## 2. Note-reading fluency and practice scheduling

### What's known

**Reading becomes an automatic mapping from staff position to key.** Adults scanned before and after 15 weeks of learning to
read and play showed new parietal activity. A second experiment suggested reading turns a spatial code into key presses
automatically ([Stewart et al. 2003](https://research.gold.ac.uk/id/eprint/51), about a dozen people). Pianists' finger
responses are pulled by note positions even when told to ignore them
([Stewart, Walsh & Frith 2004](https://eprints-gro.gold.ac.uk/45/1/PSY-Stewart2004_GRO.pdf)). A "musical Stroop" effect shows
that note *naming* becomes automatic in musicians and lasts after years without playing
([LEAD lab, Grégoire et al.](https://lead.ube.fr/?p=5351)). Critics question the method, and most of that work comes from one
lab.

**Very little tests how to drill notes.** A review calls learning to read notation a neglected field
([Gudmundsdottir 2010](https://helgarut.hi.is/wp-content/uploads/2015/09/Advances-in-Music-Reading-Research-2010-Gudmundsdottir.pdf)).
No peer-reviewed study compares reaction times for treble vs bass clef or for ledger-line notes. No trial compares landmark
notes with mnemonics ("Every Good Boy…"). Teachers prefer a few landmarks (Middle C, treble G, bass F, then high and low C) read
by step or skip (**Tradition**: [Curious Piano Teachers](https://thecuriouspianoteachers.org/blog/post/6959/how-to-use-landmark-notes-for-note-reading);
[Tenuto School](https://thetenutoschool.com/blog/is-it-best-to-use-the-mnemonic-or-landmark-system-for-note-reading/)). For
ledger lines, teachers suggest counting by thirds from a known anchor. One site suggests naming a note within 2 seconds,
with no data behind it (**Tradition**: [music-theory-practice.com](https://music-theory-practice.com/guides/how-to-read-ledger-lines)).
A retrospective survey found musicians taught note names early had the fewest reading problems
(**Weak**, [ResearchGate](https://www.researchgate.net/publication/305617694_The_Effect_of_Music_Teaching_Method_on_Music_Reading_Skills_and_Music_Participation_An_Online_Study)).

**Speed vs accuracy.** "Fluency" training (correct answers per minute) is popular
([Binder 1996](https://doi.org/10.1007/BF03393163) [K]). A review found little evidence that timed practice beats the same
amount of accurate practice (Doughty, Chase & O'Shields 2004, *The Behavior Analyst* [K]). So response time is a good *measure*. A countdown timer
is not a proven *method*.

**Spacing and testing work well.** Spreading practice over days beats cramming. The best gap grows with how long you need to
remember ([Cepeda et al. 2006](https://doi.org/10.1037/0033-2909.132.3.354) [K]). In an online study of more than 1,350 people,
the best gap was about 20–40% of the test delay for a 1-week test and about 5–10% for a 1-year test
([Cepeda et al. 2008](https://pubmed.ncbi.nlm.nih.gov/19076480/)). Testing yourself beats re-studying for retention
([Roediger & Karpicke 2006](https://doi.org/10.1111/j.1467-9280.2006.01693.x) [K]; meta-analysis about g = 0.50,
[Rowland 2014](https://doi.org/10.1037/a0037559) [K]). Every drill trial is already a test. The open question is when each note
comes back.

**Mixing (interleaving) helps when the items are easily confused.** A meta-analysis of 59 studies found an overall benefit
(g = 0.42), larger for visual categories (about 0.67), but blocking was *better* for word lists (g = −0.39). Mixing worked best
when categories looked alike ([Brunmair & Richter 2019](https://pubmed.ncbi.nlm.nih.gov/31556629/)). Neighbouring lines and
spaces, and the same position in two clefs, are exactly that kind of confusable set. In music, 25 piano-trained adults played
melodies practised in random order faster two days later, while believing fixed order had worked better
([Abushanab & Bishara 2013](https://pubmed.ncbi.nlm.nih.gov/23508339/)). Beginner clarinettists (N = 41) showed the same
pattern ([Stambaugh 2010](https://doi.org/10.1177/0022429410385945)). In 10 advanced clarinettists the results were modest,
and they still preferred blocked practice ([Carter & Grahn 2016](https://pmc.ncbi.nlm.nih.gov/articles/PMC4989027)).

**Adaptive difficulty.** The "85% rule" says about 85% correct is the best training level. It is a result for machine-learning
models on simple tasks, not a human experiment ([Wilson et al. 2019](https://pmc.ncbi.nlm.nih.gov/articles/PMC6831579)).
Duolingo's half-life model fitted about 13 million practice records and beat simpler schedulers
([Settles & Meeder 2016](https://doi.org/10.18653/v1/P16-1174) [K]). Leitner boxes and SM-2 are widely used rules of thumb,
not tested in experiments [K].

### How sure we can be

Spacing and testing: **strong**, but shown on words and facts, not on staff notes. Interleaving: **strong** in general, **weak**
in music (small studies, n = 10–41). Automatic reading: **weak to moderate** (small brain studies, one lab for the Stroop work).
Landmarks, ledger-line tips, the 2-second target and the 85% target: **tradition or theory**.

---

## 3. Rhythm teaching and timing measurement

### What's known

**How rhythm is taught.** The tested comparisons are old, small and with children. Counting syllables that tell duple from
triple subdivisions helped recognition in 160 second and third graders ([Colley 1987](https://doi.org/10.2307/3345075)). Seeing
the notation while hearing the rhythm beat either alone ([Shehan 1987](https://doi.org/10.2307/3344987)). Gordon and
Kodály-derived methods both beat no instruction ([Palmer 1976](https://doi.org/10.2307/3345154)). Takadimi, which names a
note by its place in the beat, is a teaching proposal with no trial
([Hoffman, Pelto & White 1996](https://doi.org/10.71156/2994-7073.1082)). Tapping the foot or clapping the beat during rhythm
training is reported to help sight-reading ([Boyle 1970](https://doi.org/10.2307/3344498); result [unverified]). No study
compares counting systems in adults at a keyboard. Which counting system to use is a matter of taste.

**Metronome and slow practice.** Players who said they used a metronome, slowed down, or repeated sections scored higher
(60 high-school wind players, [Miksza 2007](https://doi.org/10.1177/0022429408317513)). Nearly all musicians report slow
practice (99% of 256 classical players surveyed, [Allingham & Wöllner 2022](https://doi.org/10.1177/03057356211073481)).
Metronome use rises with level in 3,325 young musicians ([Hallam et al. 2012](https://doi.org/10.1177/0305735612443868)).
All of this is correlational. No trial shows that step-by-step tempo raises beat other schedules. Teachers warn about leaning
on the click (**Weak source**, [The Instrumentalist](https://theinstrumentalist.com/august-2019/tempo-maintenance-for-percussionists/)).

**What normal timing looks like.** When tapping along with a beat, people tap slightly early. Musicians are closer to the beat
and steadier ([Repp 2005](https://doi.org/10.3758/bf03206433); [Repp & Su 2013](https://doi.org/10.3758/s13423-012-0371-2)).
The often-quoted size, roughly 20–80 ms early for non-musicians and less for musicians, is [unverified]. In 31 students versus
9 trained musicians, musicians had smaller early bias, less variability and faster correction after a tempo change
([Repp 2010](https://doi.org/10.1016/j.humov.2009.08.002)). Practice cuts timing variability fast: most of the gain came in the
first hour, little after that ([Madison et al. 2013](https://doi.org/10.1016/j.actpsy.2013.02.010)).

**What players can hear.** In 30 listeners, a single displaced note was noticed at about 6 ms for gaps under 240 ms, and at
about 2.5% of the gap above that. Musical training made no difference
([Friberg & Sundberg 1995](https://doi.org/10.1121/1.413218)). In 28 people, an unevenness of 3.5% was detectable, and a
pulse was still felt up to 8.6% ([Madison & Merker 2002](https://doi.org/10.1007/s00426-001-0085-y)). Rhythm games use fixed
"perfect" windows of ±16–35 ms (**Weak source**: [osu! wiki](https://osu.ppy.sh/wiki/en/Gameplay/Judgement/osu%21mania)).

**Feedback on timing.** In 24 amateurs imitating rhythms, live visual feedback helped loudness but not timing
([Sadakata et al. 2008](https://doi.org/10.1080/09298210802322401)). In percussion students, neither raw nor high-level
feedback reduced timing errors. The authors suggest visual feedback may disrupt timing by adding mental load
([Brandmeyer et al. 2011](https://doi.org/10.1007/s00426-010-0291-6)).

### How sure we can be

Rhythm-teaching evidence is **weak**: 1970s–80s school studies, not repeated. Timing norms and perception thresholds are
**moderate to strong**: many tapping studies agree. The live-feedback result is **weak to moderate**: two studies from one
group, but it fits the motor-learning findings in section 5.

---

## 4. Technique: scales, arpeggios, independence, schools, slow practice, injury, and what MIDI can measure

### What's known

**Scales and the thumb.** Thumb crossings leave a timing mark. At fast tempos, scale timing showed three delay peaks caused by
the thumb passing under. At slow tempos, players slowed at the start and end instead. A 4-parameter model predicted each
note's deviation (R² = 0.70) ([van Vugt et al. 2014](https://doi.org/10.1007/s00221-014-4036-4), N [unverified]). The hand
starts preparing the thumb-under up to about 500 ms early ([Engel et al. 1997](https://doi.org/10.1007/bf02450317)). Octave
boundaries are noisier too, separate from the thumb, and listeners hear deviations there less well
([van Vugt et al. 2012](https://doi.org/10.3389/fpsyg.2012.00495)). The thumb-under versus thumb-over debate is teaching
opinion. Ortmann (1929) observed that smooth scales mix both: a partial thumb-under plus a hand shift (**Tradition/early lab**,
[summary](https://pianoeducation.org/pnovtscl.html)). Chang argues thumb-over is needed for fast runs
(**Tradition**, [Fundamentals of Piano Practice](https://fundamentals-of-piano-practice.readthedocs.io/chapter1/ch1_topics/III.5.html)).

**Hanon and Czerny.** No controlled study tests either. Hanon's book promises agility, independence, strength and evenness with
no evidence ([Wikipedia](https://en.wikipedia.org/wiki/Hanon_exercises); critique in
[Chang](https://mail.pianofundamentals.com/book/en/1.III.7.8), **Tradition**). The nearest lab work: daily practice of finger
sequences at a *submaximal* speed raised top speed, and it lasted two months. But it carried over only partly to other
sequences in the same hand and not to the other hand ([Furuya et al. 2013](https://doi.org/10.1186/1471-2202-14-133), about
6 per group [unverified]). That fits the critics: you get best at what you drill. In 19 experts followed for 27 months, scale
evenness improved with total practice time. Practice *content* didn't matter
([Jabusch et al. 2009](https://doi.org/10.1016/j.humov.2008.08.001); correlational).

**Taubman, Matthay, Leschetizky, "weight" and relaxation.** The evidence is testimonials. A thesis calls Taubman "anecdotally
successful" ([Sabo, uOttawa](https://ruor.uottawa.ca/items/a993ed6f-bec0-47e5-87a0-a227bcbb0bba)). The Golandsky Institute's
material is press and testimony (**Weak source**,
[Golandsky](https://www.golandskyinstitute.org/wp-content/uploads/2019/04/2012-07-19_-_musical_athletes_nytimes.pdf)). In 54
pianists with musician's dystonia, 87% had tried retraining. 81.5% felt better, but only 42.9% showed measurable scale
improvement. This is a retrospective, uncontrolled study ([van Vugt et al. 2014, PRD](https://doi.org/10.1016/j.parkreldis.2013.08.009)).
Biomechanics shows experts use gravity and arm movement more efficiently
([Furuya & Kinoshita 2008](https://doi.org/10.1016/j.neuroscience.2008.07.028)). Pressing hard into the key bed wastes force at
loud dynamics ([Kinoshita et al. 2007](https://doi.org/10.1121/1.2717493)). That's consistent with "use the arm, release the
key", but it doesn't test any school. MIDI can't see tension.

**Finger independence.** No one's fingers are fully independent. In 10 adults, the middle and ring fingers were least
independent ([Häger-Ross & Schieber 2000](https://doi.org/10.1523/JNEUROSCI.20-22-08542.2000)). Pianists' ring and little
fingers are much less slow than non-musicians', with no difference in strength
([Aoki et al. 2005](https://doi.org/10.1123/mcj.9.1.23)). So training builds speed and control, not strength. Four days of
practice reduced finger coupling in 10 novices, most for the ring and little fingers
([Furuya et al. 2014, Neuroscience](https://doi.org/10.1016/j.neuroscience.2014.06.031)). No study shows that special
"independence" exercises beat ordinary playing.

**Slow practice, variants, chunking.** In 17 advanced pianists, next-day retention did not depend on practice time or
repetitions. It depended on the share of correct tries. The best fixed errors at once, varied the tempo at the problem spot,
and stopped before repeating an error ([Duke, Simmons & Cash 2009](https://doi.org/10.1177/0022429408328851); strategy details
from secondary summaries). Splitting a hard finger sequence into parts helped skilled pianists only with millisecond timing
feedback after each try. Without the feedback, or for less skilled players, there was no gain
([Kimoto, Hirano & Furuya 2025](https://doi.org/10.1038/s42003-025-07562-6)). Dotted and accented rhythm variants are a teaching
staple, but no study tests them for pianists (**Tradition**). Good tempo-ramp step sizes are unstudied.

**Practice length, rest and injury.** Playing-related pain is common in pianists: 26–93% across studies, depending on the
definition. No study combined a clear definition with established risk factors
([Bragge et al. 2006](https://doi.org/10.1093/occmed/kqi177)). In 5,424 professional musicians, 12-month prevalence was 41–93%,
higher in women, mostly neck and shoulders ([Kok et al. 2016](https://doi.org/10.1007/s00420-015-1090-6)). Musician's dystonia
affects about 1% of professionals. It's linked to intense fine-motor work, perfectionism and inheritance
([Altenmüller & Jabusch 2010](https://doi.org/10.1111/j.1468-1331.2010.03048.x)). Common advice such as "take 5 minutes every
25–30" or "avoid sudden increases in practice" could not be traced to primary studies here. No study covers amateur adult
pianists.

**What "smooth and clean" means in MIDI, and yes, studies measured it that way.**

- **Evenness (IOI SD).** Jabusch's MIDI scale analysis records two-octave scales at 120 bpm in sixteenths (a 125 ms target
  gap). It measures the SD of the gaps between note onsets (IOI SD). It separated 8 dystonic from 8 healthy professionals, went
  with a clinical disability score, and tracked recovery after treatment
  ([Jabusch et al. 2004](https://doi.org/10.1002/mds.10671)).
- **Healthy values.** 8 professionals: right hand 8.44 ms, left hand 9.19 ms (about 7% of the gap). 10 listeners heard
  unevenness from about 10.22 ms (about 8.2% of the gap). Each pianist's small, consistent deviations were a personal
  "fingerprint" that a computer could recognise but listeners couldn't hear. Velocity wasn't analysed
  ([van Vugt et al. 2013](https://doi.org/10.3389/fpsyg.2013.00134)).
- **Method.** Fit an even grid to the onsets, so tempo drift isn't counted as unevenness. Then separate consistent deviations
  (phrasing, habits) from random trial-to-trial noise ([van Vugt et al. 2012](https://doi.org/10.3389/fpsyg.2012.00495)).
- **Legato.** Key overlap time is the next note-on minus the previous note-off. Skilled pianists' overlaps grew with slower
  tempos and longer notes, and varied a lot between people. No single "correct" overlap exists
  ([Repp 1995](https://doi.org/10.1121/1.413065); [Repp 1997](https://doi.org/10.1121/1.420110)). Millisecond values weren't
  extracted.
- **Velocity evenness.** Jabusch 2004 included velocity measures. No norms were found.
- **Missing.** No norms for amateurs or intermediates, and no velocity SD norms.

### How sure we can be

MIDI evenness metrics: **moderate** (small samples, n = 8–24, but several papers from the Hanover group agree). Finger
independence: **moderate**. Hanon, Czerny and the schools: **no trials**; testimony only. Practice strategies: **weak to
moderate** (n = 17, correlational). Injury prevalence: **strong** that pain is common, **weak** on causes.

---

## 5. Practice structure, feedback timing and deliberate practice

### What's known

**Amount of practice matters, but less than claimed.** The famous claim that deliberate practice explains almost all skill
differences ([Ericsson et al. 1993](https://doi.org/10.1037/0033-295x.100.3.363)) did not survive. A meta-analysis found
practice explained 21% of the variance in music ([Macnamara et al. 2014](https://doi.org/10.1177/0956797614535810)). A
music-only meta-analysis with stricter rules found about 37% (r = 0.61, 13 studies, N = 788)
([Platz et al. 2014](https://doi.org/10.3389/fpsyg.2014.00646)). A direct replication found a "substantial, but considerably
smaller" effect ([Macnamara & Maitra 2019](https://doi.org/10.1098/rsos.190327)). Minutes are worth logging. How you practise
is where the app can help.

**How you practise beats how long.** In 17 pianists, retention followed the proportion of correct tries, not time or tries
([Duke, Simmons & Cash 2009](https://doi.org/10.1177/0022429408328851)). Better players in 3,325 young musicians used more
systematic strategies, more immediate error correction, and more recording and metronome use
([Hallam et al. 2012](https://doi.org/10.1177/0305735612443868)).

**Spread practice over days, with breaks.** In 29 musicians learning a keyboard sequence, accuracy improved at the second
session only when sessions were 24 h apart; speed improved with 6 h and 24 h gaps
([Simmons 2012](https://doi.org/10.1177/0022429411424798)). An extra 5-minute break early in practice gave the largest
overnight gains in two small experiments ([Duke et al. 2009, Annals NYAS](https://doi.org/10.1111/j.1749-6632.2009.04795.x)).
The claim that sleep *improves* motor skills ([Walker et al. 2002](https://doi.org/10.1016/s0896-6273(02)00746-8)) shrank in a
meta-analysis of 1,296 people: the gain is explained by artifacts, and sleep may only stabilise memory
([Pan & Rickard 2015](https://doi.org/10.1037/bul0000009)). No study fixes the best session length.

**Mental practice works, less than playing.** It has a reliable positive effect
([Driskell et al. 1994](https://doi.org/10.1037/0021-9010.79.4.481)). In 16 pianists, mental plus physical practice matched
physical practice alone ([Bernardi et al. 2012](https://doi.org/10.1525/mp.2012.30.3.275)). A "look before you play" step is
cheap mental practice.

**Feedback timing.** Frequent, immediate feedback helps during practice but can hurt later retention, because learners lean on
it ([Salmoni et al. 1984](https://doi.org/10.1037/0033-2909.95.3.355)). Feedback on 50% of tries, or faded, gave equal or
better retention ([Winstein & Schmidt 1990](https://doi.org/10.1037/0278-7393.16.4.677)). But complex, high-load skills can need
*more* feedback ([Wulf & Shea 2002](https://doi.org/10.3758/bf03196276)). Sight-reading at a keyboard is high-load. Letting
learners choose when to get feedback looked useful, but after correcting for publication bias the effect is about g = 0.1,
not distinguishable from zero ([McKay et al. 2022](https://conferences.lnu.se/index.php/metapsychology/article/view/2803)).
Phrasing corrections negatively or as directions made no difference
([Duke & Henninger 1998](https://doi.org/10.2307/3345345)).

### How sure we can be

Deliberate-practice figures: **strong** (several meta-analyses). Spacing in music: **weak to moderate** (small keyboard
studies). Sleep: **strong** critique, so say "stabilises" at most. Feedback principles: **moderate**, from simple lab tasks.
Music-specific feedback evidence: **weak**.

---

## 6. Graded syllabi: a ladder the app can generate from

### What's known

All three boards publish official sight-reading tables, read directly from their PDFs.

- **ABRSM** (2027–28, unchanged from 2025–26) is cumulative from Initial to Grade 8. Each feature stays once introduced. The
  candidate gets 30 seconds to look and try out. Sight-reading is 14% of the marks
  ([ABRSM 2027–28, pp. 14–16](https://www.abrsm.org/sites/default/files/2026-06/Piano%20Practical%20Grades%20Qualification%20Specification%202027%20&%202028.pdf)).
- **Trinity** pitches tests "approximately two grades lower than the exam being taken", with 30 seconds to prepare. It gives
  keys, time signatures, note values, dynamics and articulation, but no length or hand-position rules
  ([Trinity from 2023, pp. 80–81](https://www.trinitycollege.com/resource?id=9079)).
- **RCM** tests a **rhythm-only** part at every level: tap one bar of beat, then tap or clap the rhythm. "A steady pulse and
  metric accentuation are expected." From Level 5 the playing test is "comparable to Level N−3 repertoire", with an optional
  lead-sheet alternative ([RCM 2022](https://teacherportal.rcmusic.com/getattachment/57f3734d-97e5-4777-b67e-4b1111ee31a3/piano-syllabus-2022-edition.pdf)).

**ABRSM, what each grade adds** (from the official table):

| Grade | Bars | New time sigs | New keys | Hands | Other new things |
|---|---|---|---|---|---|
| Initial | 4 (6 in 2/4) | 4/4, 2/4 | C maj, D min | Separate, 5-finger position | Half, quarter, paired eighths; half rest; legato, staccato; *f*, *p* |
| 1 | — | 3/4 | G, F maj; A min | Any 5-finger position | Accidentals in minor keys; dotted half; 4 eighths; whole and quarter rests; slurs, accents; *mf*, *mp*, hairpins |
| 2 | — | — | D maj; E, G min | **Hands together** | Whole note; dotted quarter + eighth; ties; *pp* |
| 3 | up to 8 | 3/8 | A, B♭, E♭ maj; B min | **Outside 5-finger position** | 2-note chords; dotted eighth + sixteenth; simple sixteenths; eighth rest |
| 4 | c. 8 | 6/8 | — | — | Pickup; chromatic notes; pause; tenuto |
| 5 | c. 8–12 | — | E, A♭ maj; F♯, C min | — | 4-part chords (≤2 per hand); simple syncopation; slowing at the end; *ff* |
| 6 | c. 12–16 | 9/8, 5/8, 5/4 | C♯, F min | — | Triplets; clef changes; sustain pedal |
| 7 | c. 16–20 | 7/8, 7/4 | — | — | Tempo changes; 8va; una corda |
| 8 | c. 1 page | 12/8 | B, D♭ maj | — | 3-note chords per hand; spread chords; simple ornaments; speeding up |

**Merged ladder** (the researcher's synthesis of the three boards; "inf" means a value no board gives):

| Level | Roughly | Bars | Keys | Time sigs | Rhythm added | Hands / texture |
|---|---|---|---|---|---|---|
| L1 | RCM Prep A | ~2 (inf) | C position | 4/4 | Whole, half, quarter | One hand; steps in one direction; fixed 5-finger position |
| L2 | RCM Prep B / Trinity Initial | 4 | C | 4/4, 2/4 | Half rest | Melody split between the hands |
| L3 | ABRSM Initial / RCM 1 | 4 (2/4: 6) | C, G, F; A, D min | 4/4, 2/4 | Paired eighths | Hands separate; tonic to dominant |
| L4 | ABRSM 1 / RCM 2 | 4–6 | ≤1 accidental | + 3/4 | Dotted half, quarter rest, ties, 4 eighths | Any 5-finger position; may move beyond (RCM) |
| L5 | ABRSM 2 / RCM 3 | 4–8 | ≤2 | 2/4, 3/4, 4/4 | Whole note, dotted quarter + eighth | **Hands together** |
| L6 | ABRSM 3 / RCM 4 | up to 8 | ≤3 | + 3/8 | Dotted eighth + sixteenth, sixteenths | Outside 5-finger position; 2-note chords |
| L7 | ABRSM 4 / RCM 5 | c. 8 | ≤3 | + 6/8 | Pickup, chromatic notes | Position shifts |
| L8 | ABRSM 5 / Trinity 5 | 8–12 | ≤4; modulation to dominant (Trinity) | | Syncopation | 4-part chords; accompaniment patterns (inf) |
| L9 | ABRSM 6 / RCM 8 | 12–16 | ≤4 | + 9/8, 5/8, 5/4 | **Triplets** | Clef changes; pedal |
| L10 | ABRSM 7 / RCM 9 | 16–20 | up to 5 | + 7/8, 7/4 | | 8va; wider range (inf) |
| L11 | ABRSM 8 / RCM 10 | ~1 page | All to 5 (Trinity incl. double sharps/flats) | + 12/8, 2/2, changing | Duplets | 3-note chords per hand, spread chords, ornaments |
| L12 | Beyond Grade 8 (inf) | 1+ page | All | Mixed | Free | Full textures |

Use 30 seconds of look time at every level, as ABRSM and Trinity do. RCM's rhythm track gives a separate ladder: one bar of
beat, then 2 bars (early levels), 4 bars (middle), 4–6 bars (top), using the matching level's rhythms. **The boards disagree**
on when hands play together (RCM L3, ABRSM Grade 2), eighth notes (ABRSM Initial, Trinity Grade 3), triplets (ABRSM 6, Trinity
8) and 6/8 (ABRSM 4, Trinity 5). **No board gives range or ledger-line limits.** Those values must be invented and labelled
as such.

**Scales as a technique ladder.** ABRSM moves from 1 octave hands separately (Initial) to 2 octaves hands together (Grade 2+), 4
octaves (Grade 6+), then 3rds, 6ths and inversions. Its guide speeds rise from ♩=54 (Initial) to ♩=100 (Grade 4), then half
note = 60 to 88 for Grades 5–8 ([ABRSM 2027–28, p. 14](https://www.abrsm.org/sites/default/files/2026-06/Piano%20Practical%20Grades%20Qualification%20Specification%202027%20&%202028.pdf)).
The rhythmic grouping these speeds refer to wasn't extracted.

### How sure we can be

The tables are **official and current**. They are expert consensus about what to test, not research on how people learn best.
The merged ladder is the researcher's synthesis.

---

## 7. Motivation and habit

### What's known

**Game features help a little.** Gamification improved learning (g = 0.49, 19 studies). Effects on motivation (0.36) and
behaviour (0.25) were smaller and didn't hold up in the most rigorous studies. Story and social features drove behaviour most
([Sailer & Homner 2020](https://doi.org/10.1007/s10648-019-09498-w)). Another meta-analysis found g = 0.50
([Bai et al. 2020](https://www.gbl.uzh.ch/quartz/references/Bai-et-al.-(2020))). These are classroom studies over weeks. A
solo app has no social side and should expect the smaller effects. Points, badges and leaderboards aren't shown to be the
active part.

**Streaks.** Across seven studies, an unbroken streak raised later engagement and a broken one lowered it, even with the same
amount of past activity. The drop was larger when people blamed themselves and smaller when the streak could be repaired
([Silverman & Barasch 2023](https://doi.org/10.1093/jcr/ucac029)). In 96 people forming a daily habit, the median time to
automaticity was 66 days (range 18–254). Missing one day didn't matter
([Lally et al. 2010](https://www.thebehavioralscientist.com/articles/how-long-to-form-a-habit), via secondary summaries).
Duolingo says learners with a 7-day streak are 2.4 times more likely to return the next day
(**Weak source**, [Duolingo blog](https://blog.duolingo.com/how-duolingo-streak-builds-habit)). The "what-the-hell" effect
(giving up after a slip) comes from dieting research and may be weaker than first thought
(**weak to moderate**, [EFPSA summary](https://more.efpsa.org/rpblog/?p=1172)).

**Goals and plans.** Specific, hard goals with feedback beat "do your best" goals
([Locke & Latham 2002](https://doi.org/10.1037/0003-066X.57.9.705)). If-then plans ("when I sit down after dinner, I'll do
5 minutes of reading first") raise follow-through, d = 0.65 across 94 tests
([Gollwitzer & Sheeran 2006](https://doi.org/10.1016/S0065-2601(06)38002-1)).

**Rewards and self-belief.** Expected, tangible rewards for doing a task lower intrinsic motivation. Positive, informative
feedback raises it ([Deci, Koestner & Ryan 1999](https://depts.washington.edu/techdocs/papers/deciExtrinsicRewardsAndIntrinsicMotivation99.pdf),
128 experiments; disputed by Cameron & Pierce). Self-determination theory (autonomy, competence, relatedness) is the main
proposed framework for music motivation, still mostly untested
([Evans 2015](https://doi.org/10.1177/1029864914568044)). Self-efficacy was the strongest predictor of exam ratings in 686
students, repeating an earlier study of 332 ([McPherson & McCormick 2006](https://doi.org/10.1177/0305735606064841)). That's a
correlation; skill may raise self-belief too.

**Adult learners.** The research is mostly interviews. Adults learn for satisfaction and self-development, and struggle with
time and self-criticism. In 68 older beginners followed for 12 months online, learning was measurable
([PLOS ONE 2025](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11975106/)). No peer-reviewed evaluation of commercial piano apps
was found.

### How sure we can be

Gamification: **strong** that a small effect exists, **moderate** on which parts matter. Streaks: **moderate** (one paper,
seven studies, not about music). Implementation intentions: **strong**. Self-efficacy: **strong** for a correlation. Nothing
here is specific to adult piano self-learners.

---

## 8. Existing apps: ideas, not copies

All of this is **weak source** material: vendor pages, reviews and forums.

- **Sight Reading Factory** generates exercises from settings (range, rhythms, leaps, accidentals). It has a
  disappearing-measure option and a rhythm-only mode with lenient, moderate and strict grading
  ([SRF](https://www.sightreadingfactory.com/blog/customization); [MusicFirst](https://www.musicfirst.com/blog/sight-reading-factory-adds-rhythm-only-auto-assessment)).
- **Piano Marvel's SASR** test has 18 levels × 5 sublevels, a score from 100 to 1900, three strikes to end, and a progress chart.
  Its wait-for-me mode is *not allowed* during the test ([Piano Marvel](https://pianomarvel.com/en/feature/sasr)). Forum users
  track SASR and map it onto exam grades ([Piano World](https://forum.pianoworld.com/ubbthreads.php/topics/3180697/pianomarvel-what-does-the-sasr-mean-according-to-grades.html)).
- **Read Ahead** hid bars to force reading ahead. It appears discontinued
  ([pianosightreading.com.au](https://www.pianosightreading.com.au/read-ahead-app/)).
- **Note Rush** has you play the note shown, with mic or MIDI, and tracks speed ([noterush.music](https://noterush.music/)).
  **Tenuto** has 24 drills, but answers are taps on screen, and a user wants real-instrument input
  ([App Store](https://apps.apple.com/us/app/tenuto/id459313476)).
- **Song apps** (Simply Piano, Flowkey, Yousician, Synthesia) teach pieces. Users say wait mode helps early but lets rhythm slide,
  that falling notes don't build staff reading, and that mic detection mishears chords and octaves
  ([Piano World](https://forum.pianoworld.com/ubbthreads.php/topics/1278143/Learning_with_Synthesia.html);
  [Bearded Skeptic](https://beardedskeptic.com/review/simply-piano-review/)). The lab result for projected falling notes
  (faster learning of a piece) is about learning pieces, not reading notation
  ([Rogers et al. 2014](https://www.uni-ulm.de/fileadmin/website_uni_ulm/iui.inst.100/1-hci/hci-paper/2014/2014-gugenheimer-piano.pdf)).
- **No app found** gives technique feedback such as evenness or touch.

**Ideas worth taking (as ideas):** a single long-scale reading score with a trend line; generation from constraints; forced
reading ahead; separate rhythm-only grading with adjustable strictness; and a rule that assessment never waits for you. Gaps
to fill: honest rhythm scoring, eyes on the staff, musical generated material, a forgiving streak, and MIDI technique metrics.

---

## 9. Microphone single-note pitch detection on a tablet

### What's known

**Algorithms.** On clean synthetic audio, classic YIN/pYIN and neural CREPE get 90–99% of frames within a quarter-tone
(50 cents). For example, on 230 instrument stems: CREPE 96.7%, pYIN 91.9%. Neural models pull ahead at 10-cent precision and
in noise ([Kim et al. 2018, CREPE](https://arxiv.org/abs/1802.06182)). The test sets are resynthesised, so these numbers are
optimistic. A small 2025 model, SwiftF0 (~96k parameters), reports 90.1% clean and 89.9% in noise versus pYIN 87.5% and 87.0%,
and runs about 42× faster than CREPE on CPU. It's a preprint and the author's own evaluation. Its CREPE numbers contradict
CREPE's paper, so cross-paper comparisons don't hold ([Nieradzik 2025](https://arxiv.org/abs/2508.18440)). YIN's large-error
rate on speech was 1.03% ([de Cheveigné & Baskind 2003](https://www.isca-archive.org/eurospeech_2003/cheveigne03_eurospeech.pdf)).
Judging "was it C4?" only needs semitone accuracy, so YIN, pYIN or MPM is likely enough in a quiet room.

**Piano is a hard case.** Piano strings are stiff, so overtones sit sharp of where they "should" be. Bass notes have weak
fundamentals. Both cause octave errors, worst in the lowest two octaves
([Emiya et al. 2007](https://typeset.io/pdf/a-parametric-method-for-pitch-estimation-of-piano-tones-55u11yj4ug.pdf);
[Acta Acustica 2021](https://doi.org/10.1051/aacus/2021002)). Notes die away fast and pedal keeps them ringing. Repeated
same-pitch notes can only be told apart by an onset detector ([Bello et al. 2005](https://doi.org/10.1109/TSA.2005.851998)).
SwiftF0 can't report below 46.9 Hz, which excludes the lowest ~10 keys.

**Latency.** Android's "pro audio" flag means round-trip latency of 20 ms or less. Input latency is about half the round trip.
The `VOICE_RECOGNITION` input preset avoids extra processing ([Android NDK](https://developer.android.com/ndk/guides/audio/audio-latency),
**Industry**). Google reported the average round trip on popular phones fell from 109 ms (2017) to 39 ms (2021)
(**Industry**, [Android blog](https://android-developers.googleblog.com/2021/03/an-update-on-androids-audio-latency.html)). On top
of that, the analysis window must hold about two periods of the lowest note. That's about 31 ms for C2 and 73 ms for A0. Most of
this delay is constant per device and can be calibrated out. No figures were found for Lenovo tablets.

**Chords.** Single-pitch detectors fail on chords. Research piano transcription does well on clean recordings: onset F1 82.3
on MAPS ([Onsets and Frames](https://arxiv.org/abs/1710.11153)) and 96.7% on MAESTRO
([Kong et al.](https://arxiv.org/abs/2010.01815)). Those are big models and good microphones. Basic Pitch is a lightweight
option that can run on-device ([spotify/basic-pitch](https://github.com/spotify/basic-pitch)). Its accuracy on a tablet in a
room is unknown.

**Practical.** Vendors' own help pages admit mic mode needs headphones (so the app's sound doesn't leak in), a tuned piano and a
quiet room (**Weak source**, [Simply Piano help](https://piano-help.hellosimply.com/en/articles/6210583-ways-to-improve-note-recognition-in-simply-piano-for-ios-acoustic-piano)).
Libraries: TarsosDSP (Java, GPL; YIN, MPM, onset detectors), Oboe (C++, low-latency capture), and Essentia (pYIN)
([TarsosDSP](https://github.com/JorenSix/TarsosDSP); [Oboe](https://github.com/google/oboe)). In .NET, `AudioRecord` with the
`VoiceRecognition` or `Unprocessed` source at 48 kHz plus YIN/MPM in C# is a short, dependency-free path. That's an untested
design inference.

### How sure we can be

Algorithm accuracy: **strong to moderate**, but none of it was measured on a real piano through a tablet mic. Piano-specific
failure modes: **moderate**. Latency figures: **industry documentation**. Real-world behaviour on the owner's tablet:
**unknown until measured**.

---

## What this means for the app

### Proposed catalogue of drills

Shared rules for all drills, drawn from sections 1–5:

- **Accuracy first, then speed.** Track correct *and* response time.
- **Feedback after the try**, not a live meter. Offer a detailed timeline on demand.
- **Timing in % of the beat.** Suggested bands: under about 5% "in time", 5–10% "audible", over 10% "off"
  ([Friberg & Sundberg 1995](https://doi.org/10.1121/1.413218); [Madison & Merker 2002](https://doi.org/10.1007/s00426-001-0085-y)).
  These bands are a design inference.
- **Subtract the player's own average early/late bias** before grading steadiness. Tapping slightly early is normal
  ([Repp 2005](https://doi.org/10.3758/bf03206433)).
- **Aim for about 80–90% success.** Treat 85% as a tunable default, not a law
  ([Wilson et al. 2019](https://pmc.ncbi.nlm.nih.gov/articles/PMC6831579)).
- **Musical material, not random notes.** Draw from scales, chord shapes, cadences, sequences and repeated rhythm cells.

#### 1. Note reading (staff to key)

- **Purpose:** an automatic position-to-key response in both clefs.
- **Sees and does:** one note, or a short group, on the staff. Plays the key on the keyboard, not by tapping a name.
- **Measures (MIDI or mic):** right/wrong pitch, and response time from display to note-on. Per note: accuracy, median response
  time, and last-seen date. Octave errors are logged separately. The mic works for single notes from about C3 up.
- **Steps up:** landmarks first (Middle C, treble G, bass F, then high and low C). Then neighbours by step and skip. Then mixed
  clefs, and wider range. A new note is introduced in a short block next to one or two contrasting notes, then moves to mixed
  review.
- **Scheduling:** expanding review gaps. Correct but slow counts as "not yet". Slow notes come back sooner (Leitner- or
  half-life-style).
- **Evidence:** automatic mapping ([Stewart et al. 2003](https://research.gold.ac.uk/id/eprint/51)). Spacing
  ([Cepeda et al. 2008](https://pubmed.ncbi.nlm.nih.gov/19076480/)). Interleaving confusable items
  ([Brunmair & Richter 2019](https://pubmed.ncbi.nlm.nih.gov/31556629/)). Landmarks are **tradition**. Nothing tests note drills
  directly.

#### 2. Ledger lines

- **Purpose:** reading above and below the staff without counting up from the staff.
- **Sees and does:** notes on 1–4 ledger lines or spaces. Plays them.
- **Measures:** same as note reading, kept per ledger position, so slow "counting" shows up as long response times.
- **Steps up:** one ledger line, then two, and so on. Anchor first on middle C and the C's two ledger lines out. Then read by
  thirds from those anchors. Ranges are the app's own choice, since no board sets ledger limits.
- **Evidence:** **tradition only** (teacher advice; the 2-second target has no data). No study compares ledger notes with
  staff notes.

#### 3. Intervals and shapes

- **Purpose:** read by distance and contour ("up a third", "same shape a step higher") rather than note by note.
- **Sees and does:** a two- to five-note figure, with the first note given. Plays it.
- **Measures:** whether each step is correct. The *interval* error (played a 4th for a 3rd) is logged separately from wrong
  starting notes. Response time to the first note and the gaps between notes.
- **Steps up:** steps, then 3rds, then 4ths and 5ths, then 6ths to octaves. One direction, then mixed directions. Then
  transposed shapes and sequences.
- **Evidence:** pattern reading in experts ([Sloboda 1984](https://doi.org/10.2307/40285292);
  [Waters & Underwood 1998](https://doi.org/10.1177/0305735698261005); [Gromko 2004](https://doi.org/10.2307/3345521)). In one weak
  child study, the intervallic method gave better keyboard and pattern scores
  ([Academia.edu](https://www.academia.edu/71070754/A_Comparison_of_the_Middle_C_and_the_Mixed_Intervallic_Reading_Approaches_on_Music_Reading_Among_Beginner_Piano_Students)).
  No trial tests interval drills for transfer to sight-reading.

#### 4. Chords

- **Purpose:** see a stack as one shape and play it at once.
- **Sees and does:** 2-note intervals, then triads, then inversions and 4-part chords split between the hands.
- **Measures (MIDI only):** the set of notes against the target, the spread of onsets within the chord (how together it is),
  missing and extra notes, and response time. The mic can't do this reliably.
- **Steps up:** follow the boards: 2-note chords (ABRSM 3), 4-part with up to 2 notes per hand (ABRSM 5), 3 notes per hand
  and spread chords (ABRSM 8).
- **Evidence:** chunking ([Perra et al. 2025b](https://doi.org/10.1037/xlm0001358)). The steps come from the board tables. No
  chord-reading study.

#### 5. Key signatures

- **Purpose:** know the key, and which notes are sharp or flat, before playing.
- **Sees and does:** a key signature. Names or plays the tonic (major or minor). Then a short phrase where an affected note
  appears. The player must play it with its sharp or flat.
- **Measures:** correct tonic, and accidental errors on affected notes (MIDI pitch tells F from F♯). This is the main metric:
  "forgot the key" errors, logged per key.
- **Steps up:** the board key ladder: C/Am, then ≤1, ≤2, ≤3, ≤4, then 5 sharps or flats.
- **Evidence:** board tables only. Accidentals are a known local difficulty that shortens the eye span
  ([Perra et al. 2025a](https://doi.org/10.1177/17470218251325245)). No direct study.

#### 6. Rhythm (tap or clap)

- **Purpose:** hold a steady pulse and read rhythm with no pitch load.
- **Sees and does:** a one- to six-bar rhythm. Taps one bar of beat (RCM style), then taps the rhythm on any key. Clapping
  into the mic is possible as an onset-only mode. The click can fade out.
- **Measures:** onset vs grid. Signed mean offset (rushing/dragging), SD of offsets (steadiness), tempo drift with no click
  (fitted tempo vs set tempo), and missing or extra taps. All in % of the beat.
- **Steps up:** the merged ladder's rhythm vocabulary. Quarters and halves, then eighths, then dotted rhythms, then sixteenths,
  then syncopation, then triplets and compound time. Also step up by fading the click: on throughout, then count-in only, then
  silent.
- **Evidence:** rhythm predicts sight-reading ([Elliott 1982](https://doi.org/10.2307/3344862)). Counting plus movement or drill
  helps ([Mishra 2016](https://doi.org/10.1177/0305735615610925)). Telling duple from triple helps
  ([Colley 1987](https://doi.org/10.2307/3345075)). See and hear together ([Shehan 1987](https://doi.org/10.2307/3344987)). RCM's
  test format. Transfer of rhythm-only drills to full sight-reading is **untested in adults**.

#### 7. Sight-reading passages (in time)

- **Purpose:** the core skill: keep going at a set tempo with fresh music.
- **Sees and does:** a generated passage at the current ladder level. 30 seconds to look (and tap the rhythm or try a bar if
  wanted). One bar of count-in. Then plays it in time with a click that never waits. The keyboard strip shows what was played.
  After the passage, a timeline shows each note as right/wrong and early/late.
- **Measures (MIDI):** a note is a match if the pitch is right within a timing window. Kopiez used ±0.25 s; the app can scale it to
  tempo. Zhukov's four measures: tempo drift, extra notes, missing notes, RMS timing of correct notes. Plus "kept going" (no
  gaps over a bar) as its own score, and **correct notes per minute**. Errors on neighbouring keys (±1–2 semitones) are logged
  as a "lost on the keyboard" sign, separate from reading errors.
- **Steps up:** keep the owner at a level they can play continuously at about 85–90% in-time accuracy. If they drop below,
  lower tempo first, then complexity. Raise one dimension at a time: rhythm, hand independence, key/accidentals, range and
  leaps, texture (single line, intervals, chords). Vary the style (Zhukov's four styles).
- **Variants:** *Duet/accompany*: play one part against a backing part that keeps time, the classic research task and part of
  Zhukov's course. *Hear first*: listen to the passage once, then read it (aural training, the Drai-Zerbib link).
- **Evidence:** reading volume ([Mishra 2014](https://doi.org/10.1177/0305735612463770);
  [Lehmann & Ericsson 1996](https://doi.org/10.1037/h0094082)). Hybrid course ([Zhukov et al. 2016](https://doi.org/10.1177/0305735614550229)).
  Scoring ([Zhukov 2017](https://doi.org/10.1177/1029864916684193); [Kopiez et al. report](https://eldorado.tu-dortmund.de/handle/2003/20088?locale=en)).
  Feedback after the phrase ([Brandmeyer et al. 2011](https://doi.org/10.1007/s00426-010-0291-6)). No trial of a sight-reading
  app exists.

#### 8. Read-ahead / controlled reading

- **Purpose:** keep the eyes ahead of the hands.
- **Sees and does:** a passage where bars fade or are covered once the playhead reaches them. Or a window that only shows the
  next stretch of music.
- **Measures:** the same as passages. Compare in-time accuracy with and without the window at the same level and tempo.
- **Steps up:** start with generous lookahead. Never show less than about **1 s plus a margin** of music ahead of the playhead
  at the current tempo, because about 1 s is the normal span at every level. Then hide already-played bars sooner, and raise
  tempo. Don't punish slowing at hard spots.
- **Evidence:** "controlled reading" was one of four treatment types that beat control
  ([Mishra 2014](https://doi.org/10.1177/0305735612463770)). The exact methods inside that category weren't verified. Eye span
  ~1 s ([Furneaux & Land 1999](https://doi.org/10.1098/rspb.1999.0943)). The span hardly changes with short practice
  ([Rosemann et al. 2016](https://doi.org/10.1177/0305735615585398)). Good readers adjust it rather than maximise it
  ([Lim et al. 2019](https://doi.org/10.1038/s41598-019-54364-y)). No study trains eye span directly and measures transfer.

#### 9. Keyboard geography (no looking)

- **Purpose:** find keys by feel so the eyes can stay on the score.
- **Sees and does:** a note or interval on screen. Plays it without looking down, starting from a stated hand position, with
  hands covered or eyes shut. Then jumps: octaves, fifths, moving to a new five-finger position.
- **Measures:** neighbouring-key errors (±1–2 semitones, the "lost on the keyboard" signal), and response time. Self-report
  of looking ("did you look?") is the only check, unless the owner tests the tablet camera.
- **Steps up:** within a 5-finger position, then steps out of position, then octave jumps, then larger leaps and both hands.
- **Evidence:** removing sight of the keyboard raised neighbouring-note errors, and reliance depended on reading experience
  ([Banton 1995](https://doi.org/10.1177/0305735695231001)). Experts glance down less
  ([Cara 2023](https://doi.org/10.16910/jemr.16.2.5)). No training study.

#### 10. Scales

- **Purpose:** even, clean runs, smooth thumb crossings, key geography.
- **Sees and does:** a named scale with set fingering, hands separate or together, a number of octaves, and a tempo. Plays in
  sixteenths to a click (or a count-in only).
- **Measures (MIDI only):**
  - **IOI SD**, in ms and as % of the target gap, after fitting an even grid (so drift isn't counted).
  - **Tempo drift**: fitted tempo vs set tempo, and its trend across the run.
  - **Thumb-crossing delay**: the gap deviation at each crossing, shown separately.
  - **Octave-seam noise**: kept apart from the within-octave figure.
  - **Velocity SD** (dynamic evenness), and a per-finger velocity breakdown, since the fingering is fixed.
  - **Legato overlap** at each transition: next note-on minus previous note-off. Positive is overlap, negative is a gap.
  - **Hands-together sync**: onset difference between the hands on the same beat.
  - **Wrong or missed notes**: the run doesn't count for evenness (as Jabusch does).
  - **Consistent vs random error** across repeats. Consistent patterns under about 10 ms are shown as "below hearing", not
    as faults.
- **Steps up:** follow ABRSM's order of keys and octaves. Raise tempo in small steps from a comfortable *sub-maximal* speed
  towards the board's guide speeds. Move on when IOI SD stays below a target % at that tempo. For reference, healthy
  professionals score about 7% of a 125 ms gap, and about 8% is where listeners start to hear it. There are no amateur norms,
  so set targets from the owner's own baseline. Rhythmic variants (dotted, accents in 2s, 3s, 4s) can be offered, labelled as
  traditional.
- **Evidence:** MIDI scale analysis ([Jabusch et al. 2004](https://doi.org/10.1002/mds.10671);
  [van Vugt et al. 2013](https://doi.org/10.3389/fpsyg.2013.00134)). Thumb and octave effects
  ([van Vugt et al. 2014](https://doi.org/10.1007/s00221-014-4036-4); [van Vugt et al. 2012](https://doi.org/10.3389/fpsyg.2012.00495)).
  Legato overlap ([Repp 1997](https://doi.org/10.1121/1.420110)). Submaximal practice
  ([Furuya et al. 2013](https://doi.org/10.1186/1471-2202-14-133)). Feedback after each try helps skilled players on isolated
  parts ([Kimoto et al. 2025](https://doi.org/10.1038/s42003-025-07562-6)). Rhythmic variants: **tradition**.

#### 11. Arpeggios

- **Purpose:** wider stretches and thumb crossings over bigger gaps.
- **Sees and does:** a named arpeggio, set fingering, octaves, tempo.
- **Measures:** as scales. The crossing gaps are larger, so look closely at crossing delay and legato overlap at the crossing.
- **Steps up:** ABRSM: range of a 5th, then 1 octave hands separate, then 2 octaves, then hands together, then inversions. Guide
  speeds run slightly slower than scales at each grade.
- **Evidence:** Repp measured key overlap in arpeggios as well as scales
  ([Repp 1997](https://doi.org/10.1121/1.420110)). The rest follows scale research by analogy.

#### 12. Finger independence and evenness

- **Purpose:** even timing and touch across all five fingers, especially 3, 4 and 5.
- **Sees and does:** five-finger patterns, two-finger trills (3–4, 4–5), and held-note exercises (hold some keys, repeat others).
  Patterns are taken from the owner's Library pieces where possible.
- **Measures:** per-finger IOI and velocity, with the fingering fixed. Trill rate and its SD. For held-note drills, whether the
  held keys stay down (no note-off) and whether neighbouring keys sound by accident.
- **Steps up:** slow and even first, then faster, then the weaker pairs (3–4, 4–5), then both hands. Cap repetitions per set
  and per day.
- **Evidence:** fingers 3 and 4 are least independent ([Häger-Ross & Schieber 2000](https://doi.org/10.1523/JNEUROSCI.20-22-08542.2000)).
  Pianists differ in 4th- and 5th-finger speed, not strength ([Aoki et al. 2005](https://doi.org/10.1123/mcj.9.1.23)). Coupling
  drops within days ([Furuya et al. 2014](https://doi.org/10.1016/j.neuroscience.2014.06.031)). Transfer is partial
  ([Furuya et al. 2013](https://doi.org/10.1186/1471-2202-14-133)), which is why patterns should come from the owner's pieces.
  No study shows special independence exercises beat ordinary playing. Hanon-style loops are **tradition**; use them as
  warm-ups at most.

#### 13. Listen and play back (optional aural drill)

- **Purpose:** build the link between sound and notation. Aural training was one of the four treatments that beat control.
- **Sees and does:** hears a short phrase (no notation), plays it back. Then sees the notation and checks.
- **Measures:** pitch and rhythm match, as in passages.
- **Steps up:** RCM's playback ladder: first five notes of a scale, 6–8 notes, then more keys. Improvising an answer phrase
  is the creative extension.
- **Evidence:** [Mishra 2014](https://doi.org/10.1177/0305735612463770) (aural training, creative activities). RCM ear tests.

### Proposed structure for a daily ~20-minute session

No study fixes the best split between warm-up, technique, reading and pieces. This order is a design proposal, built from the
evidence on spacing, mixing, short breaks and feedback.

| Step | Time | What | Why |
|---|---|---|---|
| 1. Technique warm-up | 3–4 min | One scale and one arpeggio of the day, at sub-maximal tempo, 3–5 runs. Evenness result after each run. | Submaximal practice raises top speed; feedback after each try; limited repetition. |
| 2. Note drill | 3–4 min | Spaced review of due notes (including yesterday's slow and missed ones), mixed clefs, plus 1–3 new notes. | Spacing and testing; next-day retest; mixing confusable notes. |
| 3. Rhythm | 2–3 min | 3–4 rhythms at the current level: tap the beat, then the rhythm. Click fades on later tries. | Rhythm is the main error source; counting plus movement helps. |
| (short pause) | ~1 min | Stand, shake out hands, look away. | An early break helped overnight gains in small studies; a gentle precaution. |
| 4. Sight-reading | 7–8 min | 4–6 new passages at the current level. Each: 30 s look, count-in, play without stopping, see the summary. One passage per day is a variant (read-ahead, duet, or hear-first), rotating. | Reading volume is the main driver; varied styles and duet/aural variants are the best-supported extras. |
| 5. Fix one spot, or a piece | 2–3 min | Either replay the bar missed most today slowly, then the whole line, or play from the Library. Close with the day's summary. | Correcting errors, whole-part-whole, slowing down. |

**Rules around the session:**

- **Tempo before complexity.** If a passage falls apart, the next one goes slower before it gets simpler.
- **Mixed order across days.** Rotate which variant, scale and rhythm type appears, rather than repeating one block.
- **Short is fine.** On a busy day, Today can offer a 5-minute version (note drill plus two passages). It still counts.
- **Caps.** Limit continuous technique repetition. Add a gentle "stop if anything hurts" note. Don't raise daily minutes
  sharply from week to week. These are precautions, not proven rules.
- **If-then plan.** Ask once for a practice cue ("When I ___, I'll open the app"), and show it on Today.

### What to measure and show as progress

Show **trends across days and weeks**, not within one session. Mixed practice feels worse in the moment and works better later
([Abushanab & Bishara 2013](https://pubmed.ncbi.nlm.nih.gov/23508339/)). Timing gains are fast in the first hour, then
plateau ([Madison et al. 2013](https://doi.org/10.1016/j.actpsy.2013.02.010)). Say so on the You screen, so a plateau doesn't
look like failure.

**Sight-reading:**
- Current ladder level, plus a single long-scale reading score with a trend line.
- In-time accuracy at that level.
- **Correct notes per minute**, at the level.
- Kept-going rate.
- Zhukov's four: tempo drift, extra notes, missing notes, RMS timing.
- Neighbouring-key errors, kept apart from reading errors.
- Volume: passages and bars read per week. This is the main lever.

**Note reading:**
- Per-note accuracy and median response time, shown as a heat map on a grand staff.
- **Most-missed and slowest notes.**
- Number of notes rated "fluent" (accurate, fast and stable over several sessions).

**Rhythm and timing:**
- Rushing or dragging bias, steadiness (offset SD as % of beat), and drift without a click.
- Most-missed rhythm figures (for example, dotted eighth + sixteenth).

**Technique, per scale or arpeggio and hand:**
- IOI SD in % of the gap at a given tempo, and the fastest tempo at which it stays under target.
- Thumb-crossing delay and octave-seam noise.
- Velocity SD.
- Legato overlap.
- Hands-together sync.
- Mark anything under about 10 ms as "below what anyone can hear".

**Habit:**
- Minutes this week, and days practised out of the last 7.
- A **repairable** streak: allow a freeze, or count "5 of 7 days". Frame breaks neutrally, not as the owner's fault.
- Percentage of clean passes per session ([Duke et al. 2009](https://doi.org/10.1177/0022429408328851)).
- Feedback should be informative ("92% in time at level 6, up from 84%"), not points for showing up.

### Open questions

**Only the owner's devices can settle these:**

- **MIDI keyboard timing jitter and scan latency.** The expert evenness signal is about 8–10 ms. A consumer keyboard plus the
  Android MIDI path could add jitter of a similar size. Measure it before trusting differences under about 10 ms (for example,
  record a hardware-sequenced pattern, or compare repeated identical runs). USB and Bluetooth MIDI may differ.
- **The keyboard's velocity curve.** Velocity SD depends on the keyboard. Compare the owner's results only with the owner's own
  past results.
- **Tablet microphone latency and jitter.** No Lenovo figures were found. Calibrate once: play along to a click, or play a
  loopback beep, and take the median offset. Then check that what's left is within ±10–20 ms.
- **The tablet mic's lowest usable note.** Test real piano notes from A0 up, and find where octave errors start. Then set mic
  mode's range (probably about C2–C3 and up) and use pitch-class-only judging below it.
- **Mic in a real room.** Test with the piano's pedal, fast repeated notes, room noise, and app audio through the speaker vs
  headphones.
- **Whether the tablet camera could check "no looking" for the keyboard-geography drill.** Untested.

**Open in the research:**

- No study compares difficulty schedules ("many easy" vs "fewer hard", adaptive vs fixed). The 85% target is a theoretical
  default.
- Does controlled reading or a read-ahead window transfer to normal sight-reading? Mishra's category is supportive, but the
  methods weren't verified and no eye-span training study exists.
- Do rhythm-only drills carry over to full sight-reading in adults?
- Spacing, mixing and response-time scheduling have never been tested on staff-note reading itself.
- Which counting system works best for adults? No evidence; leave it to preference.
- No IOI SD or velocity SD norms for amateurs. The owner's own baseline will have to serve.
- Good tempo-ramp step sizes, and whether dotted or accented variants help, are untested.
- Taubman, Matthay, weight and relaxation claims are untested. MIDI can't see tension.
- Safe practice doses and break schedules for adult amateurs are unknown.
- Whether streaks or progress charts make adult music learners persist is untested. The evidence comes from other domains.

---

## References

Labels: **[K]** cited from memory by the researcher with its DOI, not re-checked. **[unverified]** a detail not confirmed.
**Weak source** for marketing, blogs, reviews and forums. Everything else was checked to exist (DOI or publisher record), and
most findings were read in the abstract or full text.

### Sight-reading

- Banton, L. J. (1995). The role of visual and auditory feedback during the sight-reading of music. *Psychology of Music* 23(1). https://doi.org/10.1177/0305735695231001 (N [unverified])
- Cara, M. A. (2023). *Journal of Eye Movement Research* 16(2). https://doi.org/10.16910/jemr.16.2.5
- Drai-Zerbib, V. & Baccino, T. (2005). *L'Année psychologique* 105(3). https://doi.org/10.3406/psy.2005.29702
- Furneaux, S. & Land, M. F. (1999). The effects of skill on the eye–hand span during musical sight-reading. *Proc. R. Soc. B* 266. https://doi.org/10.1098/rspb.1999.0943
- Gromko, J. E. (2004). *JRME* 52(1). https://doi.org/10.2307/3345521
- Herrero, L. & Carriedo, N. (2019). *Frontiers in Psychology*. https://doi.org/10.3389/fpsyg.2019.01080
- Huovinen, E., Ylitalo, A.-K. & Puurtinen, M. (2018). *JEMR* 11(2). https://doi.org/10.16910/jemr.11.2.3
- Kopiez, R., Galley, N. & Lee, J. I. (2006). *Neuropsychologia*. https://doi.org/10.1016/j.neuropsychologia.2005.10.023
- Kopiez, R. & Lee, J. I. (2006). Towards a dynamic model of skills involved in sight reading music. *Music Education Research* 8(1). https://doi.org/10.1080/14613800600570785
- Kopiez, R. & Lee, J. I. (2008). Towards a general model of skills involved in sight reading music. *Music Education Research* 10(1). https://doi.org/10.1080/14613800701871363 (59.6% from a summary of the abstract)
- Kopiez, R., Weihs, C., Ligges, U. & Lee, J. I. TU Dortmund technical report. https://eldorado.tu-dortmund.de/handle/2003/20088?locale=en
- Lehmann, A. C. & Ericsson, K. A. (1996). *Psychomusicology* 15. https://doi.org/10.1037/h0094082
- Lehmann, A. C. & McArthur, V. (2002). Sight-reading. In Parncutt & McPherson (eds.), *The Science and Psychology of Music Performance*. OUP. [unverified; not retrieved]
- Lim, Y. et al. (2019). *Scientific Reports*. https://doi.org/10.1038/s41598-019-54364-y
- Meinz, E. J. & Hambrick, D. Z. (2010). *Psychological Science* 21(7). https://doi.org/10.1177/0956797610373933 (N = 57 [unverified])
- Mishra, J. (2014). Factors related to sight-reading accuracy: a meta-analysis. *JRME* 61(4). https://doi.org/10.1177/0022429413508585
- Mishra, J. (2014). Improving sightreading accuracy: a meta-analysis. *Psychology of Music* 42(2). https://doi.org/10.1177/0305735612463770
- Mishra, J. (2016). *Psychology of Music* 44(5). https://doi.org/10.1177/0305735615610925
- Penttinen, M., Huovinen, E. & Ylitalo, A.-K. (2013). *Musicae Scientiae* 17(2). https://doi.org/10.1177/1029864912474288
- Penttinen, M., Huovinen, E. & Ylitalo, A.-K. (2015). *International Journal of Music Education*. https://doi.org/10.1177/0255761413515813
- Perra, J. et al. (2021). Review of eye-hand span. *JEMR* 14(4). https://doi.org/10.16910/jemr.14.4.4
- Perra, J. et al. (2025a). *Quarterly Journal of Experimental Psychology*. https://doi.org/10.1177/17470218251325245
- Perra, J. et al. (2025b). *J. Exp. Psych.: Learning, Memory, and Cognition*. https://doi.org/10.1037/xlm0001358 (N [unverified])
- Puurtinen, M. (2018). *JEMR* 11(2). https://doi.org/10.16910/jemr.11.2.2
- Rosemann, S., Altenmüller, E. & Fahle, M. (2016). *Psychology of Music* 44(4). https://doi.org/10.1177/0305735615585398
- Sloboda, J. A. (1976). *Canadian Journal of Psychology* 30(4). https://doi.org/10.1037/h0082064 (misprint finding [unverified])
- Sloboda, J. A. (1984). Experimental studies of music reading: a review. *Music Perception* 2(2). https://doi.org/10.2307/40285292
- Waters, A. J. & Underwood, G. (1998). *Psychology of Music* 26(1). https://doi.org/10.1177/0305735698261005
- Waters, A. J., Underwood, G. & Findlay, J. M. (1997). *Perception & Psychophysics* 59(4). https://doi.org/10.3758/bf03211857 (finding [unverified])
- Zhukov, K. (2014). *Music Education Research* 16(1). https://doi.org/10.1080/14613808.2013.819845 (design details [unverified])
- Zhukov, K., Viney, L., Riddle, G., Teniswood-Harvey, A. & Fujimura, K. (2016). *Psychology of Music* 44(5). https://doi.org/10.1177/0305735614550229
- Zhukov, K. (2017). *Musicae Scientiae* 21(4). https://doi.org/10.1177/1029864916684193

### Note reading and scheduling

- Abushanab, B. & Bishara, A. J. (2013). *Memory & Cognition* 41(6). https://pubmed.ncbi.nlm.nih.gov/23508339/
- Binder, C. (1996). *The Behavior Analyst* 19(2). https://doi.org/10.1007/BF03393163 [K]
- Brunmair, M. & Richter, T. (2019). Similarity matters: a meta-analysis of interleaved learning. *Psychological Bulletin* 145(11). https://pubmed.ncbi.nlm.nih.gov/31556629/
- Carter, C. E. & Grahn, J. A. (2016). *Frontiers in Psychology* 7:1251. https://doi.org/10.3389/fpsyg.2016.01251
- Cepeda, N. J. et al. (2006). *Psychological Bulletin* 132(3). https://doi.org/10.1037/0033-2909.132.3.354 [K]
- Cepeda, N. J., Vul, E., Rohrer, D., Wixted, J. T. & Pashler, H. (2008). *Psychological Science* 19(11). https://pubmed.ncbi.nlm.nih.gov/19076480/
- Doughty, S. S., Chase, P. N. & O'Shields, E. M. (2004). *The Behavior Analyst* 27(1):7–23. [K; no link in notes]
- Dunlosky, J. et al. (2013). *Psychological Science in the Public Interest* 14(1). https://doi.org/10.1177/1529100612453266 [K]
- Grégoire, L., Perruchet, P. & Poulin-Charronnat, B. (2013). The musical Stroop effect. *Experimental Psychology*. https://doi.org/10.1027/1618-3169/a000197 [K for DOI]; lab page https://lead.ube.fr/?p=5351
- Gudmundsdottir, H. R. (2010). Advances in music-reading research. *Music Education Research* 12(4). https://helgarut.hi.is/wp-content/uploads/2015/09/Advances-in-Music-Reading-Research-2010-Gudmundsdottir.pdf
- Kornell, N. & Bjork, R. A. (2008). *Psychological Science* 19(6). https://doi.org/10.1111/j.1467-9280.2008.02127.x [K]
- Roediger, H. L. & Karpicke, J. D. (2006). *Psychological Science* 17(3). https://doi.org/10.1111/j.1467-9280.2006.01693.x [K]
- Rowland, C. A. (2014). *Psychological Bulletin*. https://doi.org/10.1037/a0037559 [K]
- Settles, B. & Meeder, B. (2016). A trainable spaced repetition model for language learning. *ACL 2016*. https://doi.org/10.18653/v1/P16-1174 [K]
- Shea, J. B. & Morgan, R. L. (1979). *JEP: Human Learning & Memory* 5(2). https://doi.org/10.1037/0278-7393.5.2.179 [K]
- Stambaugh, L. A. (2010/2011). *JRME*. https://doi.org/10.1177/0022429410385945
- Stewart, L. et al. (2003). *NeuroImage* 20(1). https://research.gold.ac.uk/id/eprint/51
- Stewart, L., Walsh, V. & Frith, U. (2004). https://eprints-gro.gold.ac.uk/45/1/PSY-Stewart2004_GRO.pdf
- Wilson, R. C., Shenhav, A., Straccia, M. & Cohen, J. D. (2019). The Eighty Five Percent Rule for optimal learning. *Nature Communications* 10:4646. https://pmc.ncbi.nlm.nih.gov/articles/PMC6831579
- Middle C vs intervallic reading, children (Weak; peer review unconfirmed). https://www.academia.edu/71070754/A_Comparison_of_the_Middle_C_and_the_Mixed_Intervallic_Reading_Approaches_on_Music_Reading_Among_Beginner_Piano_Students
- Online survey on teaching method and reading (Weak; authors unconfirmed). https://www.researchgate.net/publication/305617694_The_Effect_of_Music_Teaching_Method_on_Music_Reading_Skills_and_Music_Participation_An_Online_Study
- Landmark-note teaching (Tradition / weak source): https://thecuriouspianoteachers.org/blog/post/6959/how-to-use-landmark-notes-for-note-reading ; https://thetenutoschool.com/blog/is-it-best-to-use-the-mnemonic-or-landmark-system-for-note-reading/ ; https://music-theory-practice.com/guides/how-to-read-ledger-lines

### Rhythm, timing and feedback

- Allingham, E. & Wöllner, C. (2022). *Psychology of Music*. https://doi.org/10.1177/03057356211073481
- Boyle, J. D. (1970). *JRME*. https://doi.org/10.2307/3344498 (result [unverified])
- Brandmeyer, A., Timmers, R., Sadakata, M. & Desain, P. (2011). *Psychological Research*. https://doi.org/10.1007/s00426-010-0291-6
- Colley, B. (1987). *JRME*. https://doi.org/10.2307/3345075
- Elliott, C. A. (1982). *JRME*. https://doi.org/10.2307/3344862
- Friberg, A. & Sundberg, J. (1995). *JASA*. https://doi.org/10.1121/1.413218
- Hoffman, R., Pelto, W. & White, J. W. (1996). Takadimi. *J. Music Theory Pedagogy*. https://doi.org/10.71156/2994-7073.1082
- Madison, G. & Merker, B. (2002). *Psychological Research*. https://doi.org/10.1007/s00426-001-0085-y
- Madison, G., Karampela, O., Ullén, F. & Holm, L. (2013). *Acta Psychologica*. https://doi.org/10.1016/j.actpsy.2013.02.010
- Miksza, P. (2007). *JRME*. https://doi.org/10.1177/0022429408317513
- Palmer, M. (1976). *JRME*. https://doi.org/10.2307/3345154
- Repp, B. H. (2005). Sensorimotor synchronization: a review. *Psychonomic Bulletin & Review*. https://doi.org/10.3758/bf03206433 (asynchrony ranges [unverified])
- Repp, B. H. (2010). *Human Movement Science*. https://doi.org/10.1016/j.humov.2009.08.002
- Repp, B. H. & Su, Y.-H. (2013). *Psychonomic Bulletin & Review*. https://doi.org/10.3758/s13423-012-0371-2
- Sadakata, M., Hoppe, D., Brandmeyer, A., Timmers, R. & Desain, P. (2008). *J. New Music Research*. https://doi.org/10.1080/09298210802322401
- Shehan, P. K. (1987). *JRME*. https://doi.org/10.2307/3344987
- osu!mania judgement windows (weak source: game wiki). https://osu.ppy.sh/wiki/en/Gameplay/Judgement/osu%21mania
- The Instrumentalist (2019), tempo maintenance (weak source: opinion). https://theinstrumentalist.com/august-2019/tempo-maintenance-for-percussionists/

### Practice structure and feedback

- Bernardi, N. F. et al. (2012). *Music Perception*. https://doi.org/10.1525/mp.2012.30.3.275
- Driskell, J. E., Copper, C. & Moran, A. (1994). *J. Applied Psychology*. https://doi.org/10.1037/0021-9010.79.4.481
- Duke, R. A., Allen, S. E., Cash, C. D. & Simmons, A. L. (2009). *Annals NYAS*. https://doi.org/10.1111/j.1749-6632.2009.04795.x
- Duke, R. A. & Henninger, J. C. (1998). *JRME*. https://doi.org/10.2307/3345345
- Duke, R. A., Simmons, A. L. & Cash, C. D. (2009). It's not how much; it's how. *JRME* 56(4). https://doi.org/10.1177/0022429408328851 (strategy details from secondary summaries)
- Ericsson, K. A., Krampe, R. T. & Tesch-Römer, C. (1993). *Psychological Review*. https://doi.org/10.1037/0033-295x.100.3.363
- Hallam, S., Rinta, T., Varvarigou, M. & Creech, A. (2012). *Psychology of Music*. https://doi.org/10.1177/0305735612443868
- Macnamara, B. N., Hambrick, D. Z. & Oswald, F. L. (2014). *Psychological Science*. https://doi.org/10.1177/0956797614535810
- Macnamara, B. N. & Maitra, M. (2019). *Royal Society Open Science*. https://doi.org/10.1098/rsos.190327
- McKay, B. et al. (2022). Self-controlled motor learning meta-analysis. *Meta-Psychology*. https://conferences.lnu.se/index.php/metapsychology/article/view/2803
- Pan, S. C. & Rickard, T. C. (2015). *Psychological Bulletin*. https://doi.org/10.1037/bul0000009
- Platz, F., Kopiez, R., Lehmann, A. C. & Wolf, A. (2014). *Frontiers in Psychology*. https://doi.org/10.3389/fpsyg.2014.00646
- Salmoni, A. W., Schmidt, R. A. & Walter, C. B. (1984). *Psychological Bulletin*. https://doi.org/10.1037/0033-2909.95.3.355
- Simmons, A. L. (2012). *JRME*. https://doi.org/10.1177/0022429411424798
- Walker, M. P. et al. (2002). *Neuron*. https://doi.org/10.1016/s0896-6273(02)00746-8
- Winstein, C. J. & Schmidt, R. A. (1990). *JEP: LMC*. https://doi.org/10.1037/0278-7393.16.4.677
- Wulf, G. & Shea, C. H. (2002). *Psychonomic Bulletin & Review*. https://doi.org/10.3758/bf03196276

### Technique

- Altenmüller, E. & Jabusch, H.-C. (2010). Focal dystonia in musicians. *European Journal of Neurology*. https://doi.org/10.1111/j.1468-1331.2010.03048.x
- Aoki, T., Furuya, S. & Kinoshita, H. (2005). *Motor Control*. https://doi.org/10.1123/mcj.9.1.23
- Bragge, P., Bialocerkowski, A. & McMeeken, J. (2006). Systematic review of PRMD in pianists. *Occupational Medicine*. https://doi.org/10.1093/occmed/kqi177
- Chang, C. C. *Fundamentals of Piano Practice* (Tradition, not peer-reviewed). https://fundamentals-of-piano-practice.readthedocs.io/chapter1/ch1_topics/III.5.html ; Hanon critique https://mail.pianofundamentals.com/book/en/1.III.7.8
- Engel, K. C., Flanders, M. & Soechting, J. F. (1997). *Experimental Brain Research*. https://doi.org/10.1007/bf02450317
- Furuya, S. & Kinoshita, H. (2008). *Neuroscience*. https://doi.org/10.1016/j.neuroscience.2008.07.028
- Furuya, S., Nakamura, A. & Nagata, N. (2013). *BMC Neuroscience*. https://doi.org/10.1186/1471-2202-14-133 (n per group [unverified])
- Furuya, S., Nakamura, A. & Nagata, N. (2014). *Neuroscience*. https://doi.org/10.1016/j.neuroscience.2014.06.031
- Golandsky Institute press material (weak source). https://www.golandskyinstitute.org/wp-content/uploads/2019/04/2012-07-19_-_musical_athletes_nytimes.pdf
- Häger-Ross, C. & Schieber, M. H. (2000). *Journal of Neuroscience*. https://doi.org/10.1523/JNEUROSCI.20-22-08542.2000
- Hanon exercises, Wikipedia (weak source). https://en.wikipedia.org/wiki/Hanon_exercises
- Jabusch, H.-C., Vauth, H. & Altenmüller, E. (2004). Quantification of focal dystonia in pianists using scale analysis. *Movement Disorders*. https://doi.org/10.1002/mds.10671
- Jabusch, H.-C., Alpers, H., Kopiez, R., Vauth, H. & Altenmüller, E. (2009). *Human Movement Science*. https://doi.org/10.1016/j.humov.2008.08.001
- Kimoto, Y., Hirano, M. & Furuya, S. (2025). *Communications Biology*. https://doi.org/10.1038/s42003-025-07562-6 (n [unverified])
- Kinoshita, H., Furuya, S., Aoki, T. & Altenmüller, E. (2007). *JASA*. https://doi.org/10.1121/1.2717493
- Kok, L. M. et al. (2016). *Int. Arch. Occup. Environ. Health*. https://doi.org/10.1007/s00420-015-1090-6
- Ortmann, O. (1929). *The Physiological Mechanics of Piano Technique* (Tradition/early lab; secondary summary). https://pianoeducation.org/pnovtscl.html
- Repp, B. H. (1995). *JASA*. https://doi.org/10.1121/1.413065
- Repp, B. H. (1997). *JASA*. https://doi.org/10.1121/1.420110
- Sabo, uOttawa thesis on the Taubman Approach. https://ruor.uottawa.ca/items/a993ed6f-bec0-47e5-87a0-a227bcbb0bba
- Silva, A. G., Lã, F. M. B. & Afreixo, V. (2015). *MPPA*. https://doi.org/10.21091/mppa.2015.1002
- van Vugt, F. T., Jabusch, H.-C. & Altenmüller, E. (2012). Fingers phrase music differently. *Frontiers in Psychology*. https://doi.org/10.3389/fpsyg.2012.00495
- van Vugt, F. T., Jabusch, H.-C. & Altenmüller, E. (2013). Individuality that is unheard of. *Frontiers in Psychology*. https://doi.org/10.3389/fpsyg.2013.00134
- van Vugt, F. T., Boullet, L., Jabusch, H.-C. & Altenmüller, E. (2014). *Parkinsonism & Related Disorders*. https://doi.org/10.1016/j.parkreldis.2013.08.009
- van Vugt, F. T., Furuya, S., Vauth, H., Jabusch, H.-C. & Altenmüller, E. (2014). Playing beautifully when you have to be fast. *Experimental Brain Research*. https://doi.org/10.1007/s00221-014-4036-4 (n [unverified])

### Graded syllabi

- ABRSM. *Piano Practical Grades Qualification Specification 2027 & 2028*. https://www.abrsm.org/sites/default/files/2026-06/Piano%20Practical%20Grades%20Qualification%20Specification%202027%20&%202028.pdf
- ABRSM. *Piano 2025 & 2026 syllabus*. https://www.abrsm.org/sites/default/files/2024-06/Piano%202025%20&%202026%20Prac%20syllabus%2020240524_access.pdf
- Trinity College London. *Piano Syllabus, graded exams from 2023* (Feb 2026 edition). https://www.trinitycollege.com/resource?id=9079
- Royal Conservatory of Music. *Piano Syllabus, 2022 Edition*. https://teacherportal.rcmusic.com/getattachment/57f3734d-97e5-4777-b67e-4b1111ee31a3/piano-syllabus-2022-edition.pdf ; errata https://teacherportal.rcmusic.com/getattachment/e006ae03-875a-4772-837b-cc0760149c49/rcm-piano-syllabus-2022-errata.pdf

### Motivation and habit

- Bai, S., Hew, K. F. & Huang, B. (2020). *Educational Research Review* 30. https://www.gbl.uzh.ch/quartz/references/Bai-et-al.-(2020)
- Deci, E. L., Koestner, R. & Ryan, R. M. (1999). *Psychological Bulletin* 125(6). https://depts.washington.edu/techdocs/papers/deciExtrinsicRewardsAndIntrinsicMotivation99.pdf
- Duolingo blog on streaks (weak source: company blog). https://blog.duolingo.com/how-duolingo-streak-builds-habit
- EFPSA blog summary of the "what-the-hell" effect (secondary). https://more.efpsa.org/rpblog/?p=1172
- Evans, P. (2015). *Musicae Scientiae* 19(1). https://doi.org/10.1177/1029864914568044
- Gollwitzer, P. M. & Sheeran, P. (2006). *Advances in Experimental Social Psychology* 38. https://doi.org/10.1016/S0065-2601(06)38002-1 ; https://kops.uni-konstanz.de/entities/publication/2e749bfb-8533-437c-8203-7e788c910c5f
- Lally, P., van Jaarsveld, C. H. M., Potts, H. W. W. & Wardle, J. (2010). *European Journal of Social Psychology* 40(6) (via secondary summary). https://www.thebehavioralscientist.com/articles/how-long-to-form-a-habit
- Locke, E. A. & Latham, G. P. (2002). *American Psychologist* 57(9). https://doi.org/10.1037/0003-066X.57.9.705
- McPherson, G. E. & McCormick, J. (2006). *Psychology of Music* 34(3). https://doi.org/10.1177/0305735606064841
- Older adult novices learning an instrument online (2025). *PLOS ONE*. https://www.ncbi.nlm.nih.gov/pmc/articles/PMC11975106/
- Rogers, K. et al. (2014). P.I.A.N.O. *ITS 2014*. https://www.uni-ulm.de/fileadmin/website_uni_ulm/iui.inst.100/1-hci/hci-paper/2014/2014-gugenheimer-piano.pdf
- Sailer, M. & Homner, L. (2020). *Educational Psychology Review* 32. https://doi.org/10.1007/s10648-019-09498-w
- Silverman, J. & Barasch, A. (2023). On or off track: how (broken) streaks affect consumer decisions. *Journal of Consumer Research* 49(6). https://doi.org/10.1093/jcr/ucac029

### Existing apps (all weak sources: vendors, reviews, forums)

- Sight Reading Factory: https://www.sightreadingfactory.com/blog/customization ; https://www.musicfirst.com/blog/sight-reading-factory-adds-rhythm-only-auto-assessment
- Piano Marvel SASR: https://pianomarvel.com/en/feature/sasr ; forum https://forum.pianoworld.com/ubbthreads.php/topics/3180697/pianomarvel-what-does-the-sasr-mean-according-to-grades.html
- Read Ahead: https://www.pianosightreading.com.au/read-ahead-app/
- Note Rush: https://noterush.music/
- Tenuto: https://apps.apple.com/us/app/tenuto/id459313476
- Simply Piano review: https://beardedskeptic.com/review/simply-piano-review/
- Piano World, "Learning with Synthesia?": https://forum.pianoworld.com/ubbthreads.php/topics/1278143/Learning_with_Synthesia.html

### Pitch detection

- Android NDK, Audio latency (industry). https://developer.android.com/ndk/guides/audio/audio-latency
- Android Developers Blog (2021), audio latency update (industry; via search summary). https://android-developers.googleblog.com/2021/03/an-update-on-androids-audio-latency.html
- Bello, J. P. et al. (2005). A tutorial on onset detection in music signals. *IEEE TSAP* 13(5). https://doi.org/10.1109/TSA.2005.851998
- Bittner, R. et al. (2022). Basic Pitch. *ICASSP 2022*. https://arxiv.org/abs/2203.09893 ; https://github.com/spotify/basic-pitch
- de Cheveigné, A. & Kawahara, H. (2002). YIN. *JASA* 111(4). https://doi.org/10.1121/1.1458024
- de Cheveigné, A. & Baskind, A. (2003). *Eurospeech 2003*. https://www.isca-archive.org/eurospeech_2003/cheveigne03_eurospeech.pdf
- Emiya, V., David, B. & Badeau, R. (2007). *ICASSP 2007*. https://typeset.io/pdf/a-parametric-method-for-pitch-estimation-of-piano-tones-55u11yj4ug.pdf
- Hawthorne, C. et al. (2018). Onsets and Frames. *ISMIR 2018*. https://arxiv.org/abs/1710.11153
- Kim, J. W., Salamon, J., Li, P. & Bello, J. P. (2018). CREPE. *ICASSP 2018*. https://arxiv.org/abs/1802.06182
- Kong, Q. et al. (2021). High-resolution piano transcription. https://arxiv.org/abs/2010.01815
- Mauch, M. & Dixon, S. (2014). pYIN. *ICASSP 2014*. https://doi.org/10.1109/ICASSP.2014.6853678
- Nieradzik, L. (2025). SwiftF0 (preprint, author's own evaluation). https://arxiv.org/abs/2508.18440
- Piano inharmonicity in the low register. *Acta Acustica* (2021). https://doi.org/10.1051/aacus/2021002
- Simply Piano help center (weak source). https://piano-help.hellosimply.com/en/articles/6210583-ways-to-improve-note-recognition-in-simply-piano-for-ios-acoustic-piano
- TarsosDSP (industry). https://github.com/JorenSix/TarsosDSP
- Oboe (industry; not fetched). https://github.com/google/oboe
