using OpenCvSharp;

namespace TrafficCounter.Vision.Tests;

public class LatestFrameBufferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(2));

    private static Frame NewFrame(long sequence) => new(new Mat(2, 2, MatType.CV_8UC1), T0.AddMilliseconds(sequence * 40), sequence);

    [Fact]
    public async Task Taking_returns_the_published_frame()
    {
        using var buffer = new LatestFrameBuffer();
        buffer.Publish(NewFrame(1));

        using Frame frame = await buffer.TakeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, frame.Sequence);
        Assert.Equal(0, buffer.Dropped);
    }

    [Fact]
    public async Task Observer_sees_every_frame_even_those_dropped_for_a_slow_consumer()
    {
        using var buffer = new LatestFrameBuffer();
        List<long> seen = [];
        buffer.Observer = f => seen.Add(f.Sequence);
        buffer.Publish(NewFrame(1));
        buffer.Publish(NewFrame(2));
        buffer.Observer = _ => throw new InvalidOperationException("faulty observer");
        buffer.Publish(NewFrame(3));

        using Frame frame = await buffer.TakeAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], seen);
        Assert.Equal(3, frame.Sequence); // a throwing observer does not stop the hand-over
        Assert.Equal(2, buffer.Dropped);
    }

    [Fact]
    public async Task Newer_frame_replaces_and_disposes_an_untaken_one()
    {
        using var buffer = new LatestFrameBuffer();
        Frame first = NewFrame(1);
        buffer.Publish(first);
        buffer.Publish(NewFrame(2));
        buffer.Publish(NewFrame(3));

        using Frame frame = await buffer.TakeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, frame.Sequence);
        Assert.Equal(2, buffer.Dropped);
        Assert.Equal(3, buffer.Published);
        Assert.True(first.Image.IsDisposed);
    }

    [Fact]
    public void TryTake_times_out_when_no_frame_arrives()
    {
        using var buffer = new LatestFrameBuffer();
        Assert.False(buffer.TryTake(TimeSpan.FromMilliseconds(20), out Frame? frame));
        Assert.Null(frame);
    }

    [Fact]
    public async Task Waiting_consumer_receives_frame_published_later()
    {
        using var buffer = new LatestFrameBuffer();
        Task<Frame> waiting = buffer.TakeAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(waiting.IsCompleted);

        buffer.Publish(NewFrame(7));

        using Frame frame = await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(7, frame.Sequence);
    }

    [Fact]
    public async Task Taking_can_be_cancelled()
    {
        using var buffer = new LatestFrameBuffer();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await buffer.TakeAsync(cts.Token));
    }

    [Fact]
    public void Frames_published_after_dispose_are_disposed_immediately()
    {
        var buffer = new LatestFrameBuffer();
        buffer.Dispose();
        Frame frame = NewFrame(1);

        buffer.Publish(frame);

        Assert.True(frame.Image.IsDisposed);
    }

    [Fact]
    public async Task Concurrent_producer_and_consumer_never_lose_the_newest_frame()
    {
        using var buffer = new LatestFrameBuffer();
        const int count = 2000;
        Task producer = Task.Run(() =>
        {
            for (int i = 1; i <= count; i++)
            {
                buffer.Publish(NewFrame(i));
            }
        }, TestContext.Current.CancellationToken);

        long last = 0;
        while (last < count)
        {
            using Frame frame = await buffer.TakeAsync(TestContext.Current.CancellationToken);
            Assert.True(frame.Sequence > last, "frames must arrive in increasing order");
            last = frame.Sequence;
        }

        await producer;
        Assert.Equal(count, buffer.Published);
    }
}
