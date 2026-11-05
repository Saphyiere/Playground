using Prima.Core.Keyboard;

namespace Prima.Core.Tests;

public class MidiParserTests
{
    private static List<MidiEvent> Parse(params byte[][] calls)
    {
        var parser = new MidiParser();
        var events = new List<MidiEvent>();
        for (var i = 0; i < calls.Length; i++) parser.Read(calls[i], 1000 * (i + 1), events);
        return events;
    }

    [Fact]
    public void ReadsANoteOnAndItsNoteOff()
    {
        var events = Parse([0x90, 60, 100, 0x80, 60, 64]);

        Assert.Equal([new MidiEvent(MidiKind.NoteOn, 1, 60, 100, 1000), new MidiEvent(MidiKind.NoteOff, 1, 60, 64, 1000)], events);
    }

    [Fact]
    public void ANoteOnWithVelocityZeroIsANoteOff()
    {
        var events = Parse([0x91, 64, 0]);

        Assert.Equal(new MidiEvent(MidiKind.NoteOff, 2, 64, 0, 1000), Assert.Single(events));
    }

    [Fact]
    public void RunningStatusRepeatsTheLastStatus()
    {
        var events = Parse([0x90, 60, 90, 64, 80, 60, 0]);

        Assert.Equal([MidiKind.NoteOn, MidiKind.NoteOn, MidiKind.NoteOff], events.Select(e => e.Kind));
        Assert.Equal([60, 64, 60], events.Select(e => e.Note));
    }

    [Fact]
    public void AMessageSplitAcrossCallsCompletesInTheLaterOne()
    {
        var events = Parse([0x90, 60], [100]);

        Assert.Equal(new MidiEvent(MidiKind.NoteOn, 1, 60, 100, 2000), Assert.Single(events));
    }

    [Fact]
    public void RealTimeBytesInsideAMessageAreTakenOutFirst()
    {
        var events = Parse([0x90, 0xFE, 60, 0xF8, 100]);

        Assert.Equal([MidiKind.ActiveSensing, MidiKind.NoteOn], events.Select(e => e.Kind));
        Assert.Equal(60, events[1].Note);
        Assert.Equal(100, events[1].Velocity);
    }

    [Fact]
    public void SysExIsSkipped()
    {
        var events = Parse([0xF0, 0x43, 0x10, 0x4C, 0x00, 0xF7, 0x90, 62, 70]);

        Assert.Equal(new MidiEvent(MidiKind.NoteOn, 1, 62, 70, 1000), Assert.Single(events));
    }

    [Fact]
    public void SysExSplitAcrossCallsIsSkippedToo()
    {
        var events = Parse([0xF0, 0x43, 0x10], [0x4C, 0x00], [0x00, 0xF7, 0x90, 62, 70]);

        Assert.Equal(62, Assert.Single(events).Note);
    }

    [Fact]
    public void SystemCommonEndsRunningStatus()
    {
        // A song position (F2) between running-status notes: its two data bytes, and any after, have no status and are dropped.
        var events = Parse([0x90, 60, 90, 0xF2, 0x10, 0x20, 64, 80]);

        Assert.Equal(60, Assert.Single(events).Note);
    }

    [Fact]
    public void PedalsArriveAsControlChanges()
    {
        var events = Parse([0xB0, MidiEvent.Damper, 127, 0xB0, MidiEvent.Soft, 0]);

        Assert.Equal([new MidiEvent(MidiKind.Control, 1, 64, 127, 1000), new MidiEvent(MidiKind.Control, 1, 67, 0, 1000)], events);
    }

    [Fact]
    public void ProgramChangesAndPitchBendAreReadButNotKept()
    {
        var events = Parse([0xC0, 5, 0xE0, 0, 64, 0xD0, 30, 0x90, 60, 1]);

        Assert.Equal(MidiKind.NoteOn, Assert.Single(events).Kind);
    }

    [Fact]
    public void DataWithoutAStatusIsDropped()
    {
        Assert.Empty(Parse([60, 100]));
    }
}
