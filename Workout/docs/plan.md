# What's next

The parts still to build, in the order I'd build them, each with what you'll see, how it works, what it adds to the data, and the
research behind it. Each part ships on its own: built, checked in both looks at phone width, and pushed. Sizes are rough: **S** is a
small change, **M** a Core piece and a screen or two, **L** several screens, a data table and a question to Claude.

| # | Part | Where you'll see it | Size | Claude |
| --- | --- | --- | --- | --- |
| 1 | Heart rate drop on the rest screen (**built**) | The rest screen | S | Feeds the look back |
| 2 | How many more could you have done? (**built**) | Each set's entry | S | Feeds the look back |
| 3 | Plateau check (**built**) | Next on the rest screen, an exercise's Stats, the look back | M | Feeds the look back |
| 4 | Maintenance from the scale (**built**) | Calories | M | No |
| 5 | Odd food days and weigh-ins (**built**) | Calories | M | No |
| 6 | Muscle map | Stats, the exercise list, the look back | L | Tags your exercises once |
| 7 | Ask Claude | The spark icon on Today | M | About 10–25¢ a question |
| 8 | The week, looked back | Today, on Monday | M | About 25¢ a week |

1 to 3 come first: they're about the gym, cost nothing, and each makes the look back smarter. When they're in, the look back's
task goes to version 2 and reads reps left, plateaus and how fast your heart rate falls. 4 and 5 are about food and the scale.
6 is the biggest, and it gives 7 and 8 something real to talk about.

---

## 1. Heart rate drop on the rest screen (S)

**What you'll see.** During a rest, under the live heart rate: **Down 34 from 152 · 1:10**, the reading when the set stopped and
how far it's fallen since. In Logbook it's a margin note in the pen; in Telemetry a falling readout on the heart channel, with the
set's stop marked on the last minute's line.

**How it works.** Core takes the highest reading within 10 seconds of the set's stop and the latest one. Each set also keeps how far
heart rate fell in the first minute after it, so the look back, and later Stats, can see whether you settle faster over the weeks.

**Data.** Nothing new: it's all in the heart-rate readings.

**Why it's information, not a rule.** How fast heart rate falls after exercise is a long-used sign of fitness. Between sets, though,
no study gives a number to wait for, and resting until heart rate reaches a target hasn't beaten a plain fixed rest. So it says
what happened, and the countdown stays the rest.

**Only the phone can show** real readings: it needs the watch.

## 2. How many more could you have done? (S)

**What you'll see.** Each set's entry on the rest screen gets a row under Hit goal: **Left in the tank: 0 · 1 · 2 · 3+**. One tap,
or none: it's optional. The workout's page can change it later, like the set's other numbers.

**How it works.** It's kept with the set, and the look back reads it:

- Every set at its reps with **3+** left: the weight is light for you. Maybe two steps instead of one, still inside the check's 10%,
  or more reps.
- Reps hit with **0** left on the last set: one step is right.
- Reps missed with **2+** left: that's effort, not strength. The same weight, and push closer.

**Data.** `WorkoutSet.RepsLeft`: 0 to 3, where 3 means three or more; empty when you didn't say.

**Why.** Effort is measured as reps in reserve in the research, and muscle growth rises as sets get closer to failure, where strength
hardly changes with it (Robinson 2024). People guess about one rep low on average, and guess better near failure and in later sets
(Halperin 2022). So it's in buckets, with 3+ as one, and judged across an exercise's sets, never from one.

**Later:** the same tap on the watch, after Stop.

## 3. Plateau check (M)

**What you'll see.** When a lift stops moving:

- On the rest screen, Next says **Flat for 4 workouts** in the pen.
- The exercise's Stats page marks **Flat since Oct 18** on its trend.
- The look back's plan for it says what to try.

**How it works.** An exercise is flat when its best estimated one-rep max (the improvement measure the app already uses) hasn't
been beaten in its last 4 workouts. The look back gets that, and tries in this order:

1. Chase reps at the same weight.
2. A small back-off: a step down for a week, then build again.
3. A different rep range for a few weeks: 8s, or 12s.

It never swaps the exercise. While you're eating below maintenance, a flat stretch is expected, and it says so.

**Data.** Nothing new: it's worked out from the sets.

**Why.**

- Adding reps grows muscle about as well as adding weight (Plotkin 2022).
- A full week off cost strength without helping growth (Coleman 2024), so no week off by default.
- Planned changes of rep range can help, where swapping exercises at random can hold gains back (Kassiano 2022).
- A calorie deficit slows muscle gain more than strength (Murphy & Koehler 2022).

## 4. Maintenance from the scale (M)

**What you'll see.** Calories already sets the last four weeks' logs against the scale, and says when the scale lost less. Under that
it will say **By your scale, your maintenance is about 2,150 a day, not 2,400**, with **Use it**. Once used:

- It shows **Adjusted −250 by the scale on Nov 8**, with **Undo**.
- Maintenance everywhere uses it: Today, the goal and maintenance lines, the look back.
- Every four weeks it's checked again, and says if it has drifted.

**How it works.**

- Maintenance by the scale = what you ate on the days you logged, on average, plus the scale's change × 3,500 a pound (7,700 a
  kilogram) ÷ the days.
- It needs 3 weeks, two thirds of the days logged, and weigh-ins at least twice a week over 2 weeks or more.
- Days you forgot to log are left out, since they count at maintenance and would pull it toward itself.
- It's rounded to 50, and a difference under 100 isn't shown.

**Data.** A setting: the adjustment in calories, and the day it was made.

**Why.** When the logs and the scale disagree, it's usually the logging, not the metabolism. In one study, people whose diets
"didn't work" under-reported their food by 47%, and their metabolism was within 5% of predicted (Lichtman 1992). A number from the
scale takes in both. The 3,500 rule overstates loss over months (Thomas 2013), but over a few weeks, read off a trend line, it's
close (Hall & Chow 2013). So it uses four-week windows, checked again.

## 5. Odd food days and weigh-ins (M)

**What you'll see.**

- A logged day far under your usual (1,000 when you usually log 1,900) is marked on Calories: **Was that all?** It offers **Count it
  at maintenance**, like a forgotten day, **Add food**, or **It's right**.
- A weigh-in far off your trend (over 2%, or 4 lb, from the line) offers **Keep** or **Delete**.

**How it works.** Core flags a day under half your usual logged day, and a weigh-in that far from the line. Your answer is kept, so
it asks once.

**Data.** A small table of days marked to count at maintenance, or as logged.

**Why.** A half-logged day is the commonest slip, and it skews the maintenance check (4), which is why this comes right after it.

## 6. Muscle map (L)

**What you'll see.**

- **Stats › Muscles:** this week's hard sets for each muscle, against a band of 10 to 20, with what's missing: **Abs 0 · Calves 0**.
- Front and back figures shaded by sets: hatched in ink in Logbook, in the training channel's lime in Telemetry.
- Your exercises, each with its muscles, in a list you can fix.
- The look back points out gaps (pointing out only: you said no swaps).

**How it works.**

- Claude tags your exercises once, and each new one as it's made: primary and secondary muscles, from 15.
- The 15: chest, front, side and rear delts, triceps, biceps, forearms, lats, upper back, lower back, abs, glutes, quads,
  hamstrings, calves.
- A set counts 1 for its primary muscles and ½ for its secondary ones.
- You correct any tag, and yours wins.

**Data.** `ExerciseMuscle`: exercise, muscle, primary or secondary.

**Why.** Growth follows how many hard sets each muscle gets a week (Schoenfeld 2017), with diminishing returns. Counting secondary
muscles as half predicted growth best (Pelland 2024).

**Claude.** A new task, `workout.tag-exercises`: a few cents, once.

## 7. Ask Claude (M)

**What you'll see.** The spark icon on Today, left of Stats, where you first wanted it. It opens a question box with starters:

- What should I do next workout?
- What muscles am I missing?
- How's the weight loss going?

The answer comes in like the look back. Where it suggests weights, **Use for next time** takes them, the same way.

**How it works.** One question, one answer, sent with the last 8 weeks summed up: workouts, plateaus, muscles by week, calories
against the scale, and body weight. Your last few questions stay on the screen.

**Cost.** About 10 to 25 cents a question, from the monthly API credits.

**Why after 3 and 6.** So the answers have plateaus and muscles to work from, not just raw sets.

## 8. The week, looked back (M)

**What you'll see.** On Monday, or the first time you open the app after your week's last planned workout, Today shows **Last week**
from Claude:

- gym days against the plan;
- what went up;
- muscles missed;
- calories against the scale;
- one or two things for this week.

It opens in full, and each week's is kept.

**How it works.** It's asked when the app opens, not by a background service, so nothing runs behind your back.

**Cost.** About 25 cents a week.

---

## Later, if you want them

- **A chest strap or arm band** (a Polar H10 or Verity Sense), read by the phone over Bluetooth: per-set heart rate you can trust,
  since a watch reads poorly at the wrist while lifting (Polar M600 against ECG: about a third of readings within 5 bpm during
  circuit weight training). Only if you buy one.
- **On the watch:** "how many more?" after Stop, and the heart rate drop during rests.
- **One-tap fixes** for the look back's "a typo?" sets, instead of opening the workout.

## Decided

1. **The order:** 1 to 8 as above.
2. **Reps left:** 0, 1, 2, 3+, and optional. Only Claude reads it for now; your own +5 rule stays as it is.
3. **A plateau:** flat when the best isn't beaten in 4 workouts.
4. **Maintenance from the scale:** it asks, with **Use it**.
5. **The muscle map:** Claude tags, and you fix.
6. **The week looked back:** on Today on Monday, with no notification.

## Sources

- Plotkin et al. 2022, load or repetition progression: https://peerj.com/articles/14142
- Robinson et al. 2024, proximity to failure, strength and hypertrophy: https://doi.org/10.1007/s40279-024-02069-2
- Halperin et al. 2022 (Sports Medicine), predicting reps to failure, as summarized in Emanuel et al. 2022:
  https://www.ncbi.nlm.nih.gov/pmc/articles/PMC9588140/
- Coleman et al. 2024, a one-week deload: https://peerj.com/articles/16777
- Kassiano et al. 2022, exercise variation: https://pubmed.ncbi.nlm.nih.gov/35438660/
- Murphy & Koehler 2022, energy deficit and training gains:
  https://portal.fis.tum.de/en/publications/energy-deficiency-impairs-resistance-training-gains-in-lean-mass-/
- Lichtman et al. 1992, self-reported against actual intake: https://pubmed.ncbi.nlm.nih.gov/1454084/
- Thomas et al. 2013, the 3,500 kcal rule: https://www.doi.org/10.1038/ijo.2013.51
- Hall & Chow 2013, the reply: https://pmc.ncbi.nlm.nih.gov/articles/PMC3859816/
- Schoenfeld et al. 2017, weekly sets and growth: https://www.pubmed.gov/27433992/
- Pelland et al. 2024, weekly volume and frequency (preprint): https://sportrxiv.org/index.php/server/preprint/download/460/967/908
- Singer et al. 2024, rest between sets: https://www.frontiersin.org/journals/sports-and-active-living/articles/10.3389/fspor.2024.1429789/text
- Polar M600 against ECG during exercise: https://pubmed.ncbi.nlm.nih.gov/29135785/
