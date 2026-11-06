namespace Prima.UI.Glossary;

/// <summary>Every entry in the glossary, by group, most common first in each.</summary>
public static class GlossaryEntries
{
    public static readonly IReadOnlyList<GlossaryEntry> All =
    [
        new("staff", "Staff", GlossaryGroup.Staff, 1,
            "Five lines, and the four spaces between them, that the notes sit on: the higher a note sits, the higher it sounds.",
            "Each line and each space is a white key, one after another: a note on a line, the next note up in the space above it.",
            ["stave", "five lines", "lines and spaces"],
            GlossaryMusic.Example("G", null, "0", "<note pname=\"e\" oct=\"4\" dur=\"4\"/><note pname=\"f\" oct=\"4\" dur=\"4\"/><note pname=\"g\" oct=\"4\" dur=\"4\"/><note pname=\"a\" oct=\"4\" dur=\"4\"/>"),
            ["staff"]),
        new("slur", "Slur", GlossaryGroup.Touch, 1,
            "A curved line over or under notes of different pitches: play them smoothly, joined, with no gap between them.",
            "Hold each key until the next is down (legato). The last note of a slur is often lifted a little early.",
            ["curved line", "line over the notes", "arc", "legato", "phrase"],
            GlossaryMusic.Example("G", "4/4", "0", "<note xml:id=\"s1\" pname=\"c\" oct=\"5\" dur=\"4\"/><note pname=\"d\" oct=\"5\" dur=\"4\"/><note pname=\"e\" oct=\"5\" dur=\"4\"/><note xml:id=\"s2\" pname=\"f\" oct=\"5\" dur=\"4\"/> | <slur startid=\"#s1\" endid=\"#s2\"/>"),
            ["slur"]),
    ];
}
