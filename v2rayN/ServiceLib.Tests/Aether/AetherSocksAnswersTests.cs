namespace ServiceLib.Tests.Aether;

// From PattNG SocksAnswersTest: the readiness probe asks a quiet listener over one connection until it answers, and
// questions asked together share it. What the probe makes of an answer is in AetherCoreManagerTests.
public class AetherSocksAnswersTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    /// <summary>A listener bound and not served, as a core's own is until its tunnel is up: the system queues what connects.</summary>
    private static TcpListener QuietListener(int port = 0)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(50);
        return listener;
    }

    private static int PortOf(TcpListener listener)
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The connections queued on <paramref name="listener" /> so far, taken from it once no more come within <paramref name="waitMs" />.</summary>
    private static async Task<List<TcpClient>> TakeQueuedAsync(TcpListener listener, int waitMs = 300)
    {
        var taken = new List<TcpClient>();
        while (true)
        {
            var waited = Stopwatch.StartNew();
            while (!listener.Pending() && waited.ElapsedMilliseconds < waitMs)
            {
                await Task.Delay(10);
            }
            if (!listener.Pending())
            {
                return taken;
            }
            taken.Add(await listener.AcceptTcpClientAsync());
        }
    }

    /// <summary>Answers the greeting <paramref name="client" /> sent, as a SOCKS5 server does.</summary>
    private static async Task AnswerAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var head = new byte[2];
        await stream.ReadExactlyAsync(head);
        await stream.ReadExactlyAsync(new byte[head[1]]);
        await stream.WriteAsync(new byte[] { 5, 0 });
    }

    /// <summary>Takes the next connection to <paramref name="listener" /> and answers its greeting, in the background.</summary>
    private static Task ServeOnceAsync(TcpListener listener)
    {
        return Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync().WaitAsync(Soon);
            await AnswerAsync(client);
        });
    }

    [Test]
    public async Task AQuietListenerIsAskedOverOneConnectionUntilItAnswers()
    {
        // Long enough for the server below to answer the question's new connection on a busy machine.
        var answers = new AetherSocksAnswers(TimeSpan.FromSeconds(1));
        var listener = QuietListener();
        try
        {
            await (await answers.AnswersAsync(PortOf(listener))).Should().BeFalse();
            await (await answers.AnswersAsync(PortOf(listener))).Should().BeFalse();
            // One connection waits in the listener's queue, not one for each question.
            var queued = await TakeQueuedAsync(listener);
            await queued.Count.Should().BeEqualTo(1);
            // The tunnel comes up: it answers the waiting greeting, and serves the new connection the next question makes.
            using (var client = queued[0])
            {
                await AnswerAsync(client);
                var server = ServeOnceAsync(listener);
                await (await answers.AnswersAsync(PortOf(listener))).Should().BeTrue();
                await server.WaitAsync(Soon);
            }
            await (await TakeQueuedAsync(listener)).Count.Should().BeEqualTo(0);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task AnAnswerThatWaitedDoesNotOutliveItsServer()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromMilliseconds(100));
        var listener = QuietListener();
        var port = PortOf(listener);
        try
        {
            await (await answers.AnswersAsync(port)).Should().BeFalse();
            // The core answers the waiting greeting, then ends, and its listener with it.
            using var client = (await TakeQueuedAsync(listener)).Single();
            await AnswerAsync(client);
        }
        finally
        {
            listener.Stop();
        }
        await (await answers.AnswersAsync(port)).Should().BeFalse();
    }

    [Test]
    public async Task AnAnswerThatWaitedDoesNotSpeakForTheNextCoreOnThePort()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromMilliseconds(100));
        var listener = QuietListener();
        var port = PortOf(listener);
        try
        {
            await (await answers.AnswersAsync(port)).Should().BeFalse();
            using var client = (await TakeQueuedAsync(listener)).Single();
            await AnswerAsync(client);
        }
        finally
        {
            listener.Stop();
        }
        // A new core binds the port and says nothing yet, as a test's core does while its tunnel comes up.
        var next = new TcpListener(IPAddress.Loopback, port);
        next.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        next.Start(50);
        try
        {
            await (await answers.AnswersAsync(port)).Should().BeFalse();
        }
        finally
        {
            next.Stop();
        }
    }

    [Test]
    public async Task AConnectionTheServerEndedIsAskedAnew()
    {
        // Long enough for the server below to answer the question's new connection on a busy machine.
        var answers = new AetherSocksAnswers(TimeSpan.FromSeconds(1));
        var listener = QuietListener();
        try
        {
            await (await answers.AnswersAsync(PortOf(listener))).Should().BeFalse();
            // The core that held it ends it, and the listener is served from then on.
            (await TakeQueuedAsync(listener)).Single().Dispose();
            var server = ServeOnceAsync(listener);
            await (await answers.AnswersAsync(PortOf(listener))).Should().BeTrue();
            await server.WaitAsync(Soon);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task AServedListenerAnswersAtOnce()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromSeconds(1));
        var listener = QuietListener();
        try
        {
            var server = ServeOnceAsync(listener);
            await (await answers.AnswersAsync(PortOf(listener))).Should().BeTrue();
            await server.WaitAsync(Soon);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task ACancelledQuestionGivesUpAtOnceAndTheQuestionGoesOn()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromSeconds(1));
        var listener = QuietListener();
        var port = PortOf(listener);
        try
        {
            // A first question waits for its answer...
            var first = answers.AnswersAsync(port);
            await Task.Delay(200);
            // ... and one asked meanwhile, then cancelled, gives up at once, rather than wait out the first.
            using var cancelled = new CancellationTokenSource();
            var second = answers.AnswersAsync(port, cancelled.Token);
            var waited = Stopwatch.StartNew();
            cancelled.Cancel();
            var threw = false;
            try
            {
                await second;
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
            await threw.Should().BeTrue();
            await (waited.Elapsed < TimeSpan.FromMilliseconds(500)).Should().BeTrue();
            await (await first.WaitAsync(Soon)).Should().BeFalse();
            // Both asked over one connection.
            await (await TakeQueuedAsync(listener)).Count.Should().BeEqualTo(1);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task APortNothingListensOnAnswersNothing()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromMilliseconds(100));
        var listener = QuietListener();
        var closedPort = PortOf(listener);
        listener.Stop();
        await (await answers.AnswersAsync(closedPort)).Should().BeFalse();
        await (await answers.AnswersAsync(closedPort)).Should().BeFalse();
    }

    [Test]
    public async Task QuestionsAskedTogetherShareOneConnection()
    {
        var answers = new AetherSocksAnswers(TimeSpan.FromMilliseconds(300));
        var listener = QuietListener();
        try
        {
            var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => answers.AnswersAsync(PortOf(listener)))))
                .WaitAsync(TimeSpan.FromSeconds(10));
            await replies.Any(reply => reply).Should().BeFalse();
            await (await TakeQueuedAsync(listener)).Count.Should().BeEqualTo(1);
        }
        finally
        {
            listener.Stop();
        }
    }
}
