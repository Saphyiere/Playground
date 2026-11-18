using Prima.Core.Library;

namespace Prima.Core.Tests;

// The owner, 17 November: a star in the Library adds a piece to the repertoire, and the repertoire has collections under names
// of their own ("Learning pieces 5", "My favorite Violet collection").
public class RepertoireTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tuesday = Monday.AddDays(1);

    [Fact]
    public void AStarKeepsAPieceOnceAndTakingItOutLeavesEveryCollection()
    {
        var kept = Repertoire.Empty.With("moonlight-1", Monday).With("moonlight-1", Tuesday).With("hallelujah", Tuesday);

        Assert.Equal(["moonlight-1", "hallelujah"], kept.Pieces.Select(p => p.Piece));
        Assert.Equal(Monday, kept.Pieces[0].Since);
        Assert.True(kept.Has("hallelujah"));

        var learning = kept.Named("Learning pieces 5", "c1").In("c1", "hallelujah", true, Tuesday);
        var without = learning.Without("hallelujah");

        Assert.False(without.Has("hallelujah"));
        Assert.Empty(without.Collections.Single().Pieces);
        Assert.True(without.Has("moonlight-1"));
    }

    [Fact]
    public void PuttingAPieceInACollectionStarsItAndTakingItOutOfTheCollectionKeepsTheStar()
    {
        var kept = Repertoire.Empty.Named("  My favorite   Violet collection ", "c1").In("c1", "violet-1", true, Monday).In("c1", "violet-1", true, Tuesday);

        Assert.Equal("My favorite Violet collection", kept.Collections.Single().Name);
        Assert.Equal(["violet-1"], kept.Collections.Single().Pieces);
        Assert.True(kept.Has("violet-1"));
        Assert.Equal(["My favorite Violet collection"], kept.Holding("violet-1").Select(c => c.Name));

        var out_ = kept.In("c1", "violet-1", false, Tuesday);

        Assert.Empty(out_.Collections.Single().Pieces);
        Assert.True(out_.Has("violet-1"));
        // A collection that isn't there changes nothing.
        Assert.Same(kept, kept.In("nowhere", "violet-2", true, Tuesday));
    }

    [Fact]
    public void ACollectionsNameIsItsOwn()
    {
        var kept = Repertoire.Empty.Named("Learning pieces 5", "c1");

        // Not a second one of the same name, big and small letters aside, nor one without a name.
        Assert.Same(kept, kept.Named("learning PIECES 5", "c2"));
        Assert.Same(kept, kept.Named("   ", "c2"));

        var two = kept.Named("Recital", "c2");
        Assert.Same(two, two.Renamed("c2", "Learning pieces 5"));
        Assert.Equal(["Learning pieces 5", "Spring recital"], two.Renamed("c2", "Spring recital").Collections.Select(c => c.Name));
        // Its own name in other letters is a new name for it.
        Assert.Equal("LEARNING pieces 5", two.Renamed("c1", "LEARNING pieces 5").Collections[0].Name);
    }

    [Fact]
    public void DroppingACollectionKeepsItsPiecesInTheRepertoire()
    {
        var kept = Repertoire.Empty.Named("Recital", "c1").In("c1", "moonlight-1", true, Monday).Dropped("c1");

        Assert.Empty(kept.Collections);
        Assert.True(kept.Has("moonlight-1"));
    }
}
