# The Virtuoso Pianist in Prima

Charles-Louis Hanon’s sixty studies follow the three parts of the G. Schirmer edition, New York, 1900, plate 15538. The original music is in the public domain in the United States. [Historical score](https://archive.org/details/hanonvirtuosopia00hano)

The numbered MEI files and the corresponding note previews are adaptations of the following editions. Their licences apply to this score data wherever it is distributed, including the APK. The repository’s software licence applies to the application code.

| Studies | Source and attribution | Licence of Prima’s adapted score data |
| --- | --- | --- |
| 1–20 | C. L. Hanon. Mutopia edition by Steve Taylor and Javier Ruiz-Alma, adapted by RANOTE (ZZXXCCVV), re-engraved by Prima. [Mutopia edition](https://www.mutopiaproject.org/cgibin/piece-info.cgi?id=2037) · [RANOTE credits](https://ranote.zzxxccvv.com/en/about/sources) | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| 21–38 | C. L. Hanon. Schirmer score data entered by RANOTE (ZZXXCCVV), re-engraved by Prima. Additional fingering from James Brigham’s public-domain edition, matched by hand, note and beat. [RANOTE credits](https://ranote.zzxxccvv.com/en/about/sources) · [Brigham’s edition and dedication](https://musescore.com/james_brigham/hanon-part-2) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| 39–60 | C. L. Hanon. Schirmer score data entered by RANOTE (ZZXXCCVV), re-engraved by Prima. [RANOTE credits](https://ranote.zzxxccvv.com/en/about/sources) · [No. 39](https://ranote.zzxxccvv.com/en/s/hanon-039) · [No. 60](https://ranote.zzxxccvv.com/en/s/hanon-060) | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |

Prima separates the piano hands, places the finger numbers on playable notes, represents held notes with ties, and creates transposed practice versions and small catalogue previews. The final rolled chord in No. 43 is scored as a chord. Use the historical edition for the original page layout and editorial directions. The practice cues are Prima’s own. The rotation routine draws on Graham Fitch’s discussion of Nos. 6 and 5. [Practising the Piano](https://practisingthepiano.com/history-piano-technique-hanon/)

## Preparing the assets

The app reads the bundled gzip-compressed MEI offline. To reproduce it, put RANOTE’s published `notes.json` and `score-1100.svg` for each numbered study in source directories `01` through `60`. The public player for No. 1, for example, uses `/scores/hanon-001/` for these files. Save Brigham’s MusicXML for Nos. 21–38 as `book2a.musicxml` in the source root. The preparation tool reads the musical symbols and writes fresh MEI and card previews.

```text
python Music/tools/hanon/prepare.py SOURCE_DIRECTORY Music/src/Prima.Core/Generation/HanonScores
```

The reference in `Music/tests/Prima.Core.Tests/Fixtures/hanon-reference.txt` was calculated separately from the published playback maps. It checks the bar counts, every written onset, piano hand, MIDI pitch and sounding duration, and every bar in repeat order. Performance timestamps are normalized to the notated grid, including the closing roll in No. 43. Printed reference checks for No. 39 and No. 60 use Schirmer pages 50 and 110.
