using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.Coop.Rules;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>The Co-op window's notes (Umut 2026-11-23): the host's enemy health for everyone to see, the others'
/// requests for more or less and their OK, and who has the least Dark Mirror progress.</summary>
public class PartyNotesTests
{
    private const int Me = 1, Host = 0, Third = 2;

    [Fact]
    public void A_note_arrives_as_it_was_sent()
    {
        var w = new NetWriter();
        new PlayerNote { ClearedLevel = 7, Request = HealthRequest.Less, Ok = false, VotedOn = 225 }.Write(w);
        var r = new NetReader(w.ToSegment());
        Assert.True(PlayerNote.TryRead(r, out var note));
        Assert.True(r.IsAtEnd);
        Assert.Equal((7, HealthRequest.Less, false, 225), (note.ClearedLevel, note.Request, note.Ok, note.VotedOn));

        w.Reset();
        new PlayerNote { ClearedLevel = -1, Ok = true, VotedOn = 450 }.Write(w);
        Assert.True(PlayerNote.TryRead(new NetReader(w.ToSegment()), out note));
        Assert.Equal((-1, HealthRequest.None, true, 450), (note.ClearedLevel, note.Request, note.Ok, note.VotedOn));
    }

    [Fact]
    public void A_broken_or_odd_note_is_refused_or_pulled_into_range()
    {
        Assert.False(PlayerNote.TryRead(new NetReader(new byte[] { 3, 1 }), out _)); // cut short
        Assert.True(PlayerNote.TryRead(new NetReader(new byte[] { 99, 5, 1, 200, 0 }), out var note));
        Assert.Equal(10, note.ClearedLevel); // a level past 10 is 10
        Assert.Equal(HealthRequest.More, note.Request);
    }

    [Fact]
    public void Arrows_toggle_and_exclude_the_ok()
    {
        var notes = new PartyNotes();
        notes.SetHostValue(200);
        notes.Ask(HealthRequest.More);
        Assert.Equal(HealthRequest.More, notes.RequestOf(Me, Me));
        notes.Ask(HealthRequest.More); // the same arrow again takes it back
        Assert.Equal(HealthRequest.None, notes.RequestOf(Me, Me));
        notes.Ask(HealthRequest.Less);
        notes.ToggleOk(); // OK clears the request
        Assert.True(notes.IsOk(Me, Me));
        Assert.Equal(HealthRequest.None, notes.RequestOf(Me, Me));
        notes.Ask(HealthRequest.More); // a request clears the OK
        Assert.False(notes.IsOk(Me, Me));
        Assert.Equal(HealthRequest.More, notes.RequestOf(Me, Me));
    }

    [Fact]
    public void The_host_changing_the_value_clears_this_games_vote_and_makes_old_notes_count_for_nothing()
    {
        var notes = new PartyNotes();
        Assert.True(notes.SetHostValue(200)); // the first value: the note changed (send it)
        Assert.False(notes.SetHostValue(200));
        notes.ToggleOk();
        notes.Heard(Third, new PlayerNote { ClearedLevel = 3, Request = HealthRequest.More, VotedOn = 200 });
        Assert.Equal(HealthRequest.More, notes.RequestOf(Third, Me));

        Assert.True(notes.SetHostValue(225)); // the host answered
        Assert.False(notes.IsOk(Me, Me));
        Assert.Equal(225, notes.Own.VotedOn);
        Assert.Equal(HealthRequest.None, notes.RequestOf(Third, Me)); // the third player's note was about 2x

        notes.Heard(Third, new PlayerNote { ClearedLevel = 3, Ok = true, VotedOn = 225 });
        Assert.True(notes.IsOk(Third, Me));
        notes.Forget(Third);
        Assert.Null(notes.NoteOf(Third, Me));
    }

    [Fact]
    public void The_least_progress_is_marked_only_when_someone_got_further()
    {
        var notes = new PartyNotes();
        var slots = new[] { Host, Me, Third };
        notes.SetProgress(4);
        Assert.Empty(notes.LeastProgress(slots, Me)); // only this game's progress is known

        notes.Heard(Host, new PlayerNote { ClearedLevel = 9 });
        Assert.Equal(new[] { Me }, notes.LeastProgress(slots, Me));

        notes.Heard(Third, new PlayerNote { ClearedLevel = 4 });
        Assert.Equal(new[] { Me, Third }, notes.LeastProgress(slots, Me).OrderBy(s => s)); // a tie: both

        notes.Heard(Host, new PlayerNote { ClearedLevel = 4 });
        Assert.Empty(notes.LeastProgress(slots, Me)); // everyone got as far

        notes.Heard(Third, new PlayerNote { ClearedLevel = PlayerNote.UnknownProgress });
        notes.Heard(Host, new PlayerNote { ClearedLevel = -1 }); // no level cleared yet
        Assert.Equal(new[] { Host }, notes.LeastProgress(slots, Me)); // an unknown one doesn't count
    }

    [Fact]
    public void A_progress_change_is_sent_once()
    {
        var notes = new PartyNotes();
        Assert.True(notes.SetProgress(2));
        Assert.False(notes.SetProgress(2));
        Assert.True(notes.SetProgress(3));
    }
}
