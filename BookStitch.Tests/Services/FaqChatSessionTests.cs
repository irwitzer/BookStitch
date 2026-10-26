using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FaqChatSessionTests
{
    private static FaqChatContent Content => FaqCatalog.LoadEnglish().Single(entry => entry.Id == "icon-design").Chat!;

    [Fact]
    public async Task Four_sends_preserve_exact_turn_order_and_only_three_images_then_stop()
    {
        using var session = new FaqChatSession(Content, (_, _) => Task.CompletedTask);
        Assert.Empty(session.History);
        Assert.False(session.CanSend);
        await session.StartAsync();
        foreach (var turn in Content.Turns)
        {
            Assert.Equal(turn.Human, session.PendingHuman);
            await session.SendAsync();
        }
        Assert.True(session.IsComplete);
        Assert.False(session.CanSend);
        Assert.Null(session.PendingHuman);
        var text = session.History.OfType<FaqChatText>().ToArray();
        Assert.Equal(Content.Turns.SelectMany(turn => new[] { turn.Human, turn.Assistant }), text.Select(item => item.Text));
        Assert.Equal(new[] { true, false, true, false, true, false, true, false }, text.Select(item => item.IsHuman));
        Assert.Equal(Content.Turns.Take(3).Select(turn => turn.Image), session.History.OfType<FaqChatImage>().Select(item => item.Source));
        Assert.DoesNotContain(session.History, item => item is FaqChatGenerating);
        Assert.Equal(11, session.History.Count);
        await session.SendAsync();
        Assert.Equal(11, session.History.Count);
        using var reopened = new FaqChatSession(Content, (_, _) => Task.CompletedTask);
        await reopened.StartAsync();
        Assert.Empty(reopened.History);
        Assert.Equal(Content.Turns[0].Human, reopened.PendingHuman);
    }

    [Fact]
    public async Task Repeated_send_during_delay_cannot_duplicate_messages()
    {
        var gate = new TaskCompletionSource();
        var armed = false;
        using var session = new FaqChatSession(Content, (_, token) => armed ? gate.Task.WaitAsync(token) : Task.CompletedTask);
        await session.StartAsync();
        armed = true;
        var first = session.SendAsync();
        await session.SendAsync();
        Assert.Single(session.History);
        Assert.False(session.CanSend);
        Assert.Null(session.PendingHuman);
        gate.SetResult();
        await first;
        Assert.Equal(3, session.History.Count);
        Assert.Equal(Content.Turns[1].Human, session.PendingHuman);
    }

    [Theory]
    [InlineData("thinking")]
    [InlineData("typing")]
    [InlineData("generating")]
    public async Task Closing_during_any_animation_prevents_late_messages(string phase)
    {
        var gate = new TaskCompletionSource();
        var armed = false;
        var session = new FaqChatSession(Content, (duration, token) =>
        {
            var stop = phase switch
            {
                "thinking" => duration == FaqChatTiming.Thinking,
                "typing" => duration == FaqChatTiming.TextStep,
                _ => duration == FaqChatTiming.Generating
            };
            return armed && stop ? gate.Task.WaitAsync(token) : Task.CompletedTask;
        });
        await session.StartAsync();
        armed = true;
        var send = session.SendAsync();
        Assert.Equal(phase == "thinking" ? 1 : phase == "typing" ? 2 : 3, session.History.Count);
        session.Dispose();
        var count = session.History.Count;
        var notifications = 0;
        session.ContentAdded += (_, _) => notifications++;
        session.PropertyChanged += (_, _) => notifications++;
        gate.TrySetResult();
        await send;
        await session.SendAsync();
        session.Dispose();
        Assert.Equal(count, session.History.Count);
        Assert.Equal(0, notifications);
        Assert.False(session.CanSend);
    }

    [Fact]
    public async Task Typing_reveals_prefixes_then_preserves_the_complete_approved_reply()
    {
        var prefixes = new List<string>();
        using var session = new FaqChatSession(Content, (_, _) => Task.CompletedTask);
        session.ContentAdded += (_, _) =>
        {
            if (session.CurrentReply is { } reply) prefixes.Add(reply.DisplayText);
        };
        await session.StartAsync();
        await session.SendAsync();
        var text = session.CurrentReply!;
        Assert.Contains(prefixes, prefix => prefix.Length > 0 && prefix.Length < text.Text.Length);
        Assert.All(prefixes, prefix => Assert.StartsWith(prefix, text.Text));
        Assert.Equal(text.Text, text.DisplayText);
    }

    [Fact]
    public async Task First_prompt_types_before_send_and_image_finishes_before_next_prompt_types()
    {
        var pendingPrefixes = new List<string>();
        var imageStages = new List<TimeSpan>();
        FaqChatSession session = null!;
        session = new FaqChatSession(Content, (duration, _) =>
        {
            if (duration == FaqChatTiming.ImageFade || duration == FaqChatTiming.ImagePause)
            {
                Assert.Null(session.PendingHuman);
                Assert.False(session.CanSend);
                Assert.IsType<FaqChatImage>(session.History.Last());
                imageStages.Add(duration);
            }
            return Task.CompletedTask;
        });
        using (session)
        {
            session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(session.PendingHuman) && session.PendingHuman is { Length: > 0 } text && !session.CanSend)
                    pendingPrefixes.Add(text);
            };
            await session.StartAsync();
            Assert.True(session.CanSend);
            Assert.True(session.IsFirstPrompt);
            Assert.Contains(pendingPrefixes, text => text.Length < Content.Turns[0].Human.Length);
            await session.SendAsync();
            Assert.Equal(new[] { FaqChatTiming.ImageFade, FaqChatTiming.ImagePause }, imageStages);
            Assert.Equal(Content.Turns[1].Human, session.PendingHuman);
            Assert.False(session.IsFirstPrompt);
        }
    }

    [Fact]
    public async Task Closing_during_first_human_typing_cancels_without_enabling_send()
    {
        var gate = new TaskCompletionSource();
        var session = new FaqChatSession(Content, (_, token) => gate.Task.WaitAsync(token));
        var start = session.StartAsync();
        Assert.True(session.HasPendingHuman);
        Assert.False(session.CanSend);
        session.Dispose();
        await start;
        Assert.False(session.CanSend);
        Assert.Empty(session.History);
    }

    [Fact]
    public void Dialogue_text_and_paragraph_breaks_match_the_supplied_script()
    {
        // Fixed fingerprints protect the supplied wording and paragraph breaks.
        var expected = new[]
        {
            "CA1C5DE103E27AAB5535E6D41BE4C49708AD7E38EC72DF63D1EAB4CC77BC3C75",
            "234D71C4D66368EB2819CE286F48F2E8381E3A7CC399484EE64AD0CAEA9ECCE4",
            "8557EDDD19D781C6BE1481549D43EA0218B85CA0C5691FAF7E7277BB38E6FAA7",
            "80D3E278F807DE4891801F7C5BE73DE384E10DF5BEAC2CB0CA7E7B6C85FE7421",
            "1C1149F6040AC37A6E75E63EB7C4FA3F08579B5CD98BFF92F9B25D08E3FD97DB",
            "FDFCA988446088265AA09CFCCC676972FE533EB280816BDD6F3DD802E0CFFB30",
            "85859D63F6B5293CD4084DB368D0F23A157295FD8B7F9D9CFB79D06E9AC64FC6",
            "E2DB181F06DADFFCF5994EFDA67355E7DA7818381B8D590BBE60A40EEBB57E7C",
        };
        var actual = Content.Turns.SelectMany(turn => new[] { turn.Human, turn.Assistant })
            .Select(text => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))));
        Assert.Equal(expected, actual);
    }
}
