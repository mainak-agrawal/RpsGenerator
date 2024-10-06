using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static long totalRequestCount = 0;
    private static long failedRequestCount = 0;
    private static Stopwatch timeTracker = new Stopwatch();
    private static PriorityQueue<string, long> taskQueue = new PriorityQueue<string, long>();
    private const long timerGranularityTicks = 1000000; // 100ms

    static async Task Main(string[] args)
    {
        // Set an environment variable for the current process
        Environment.SetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS", "1");

        // Retrieve and print the environment variable to confirm it was set
        string? value = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS");
        Console.WriteLine($"DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS: {value}");

        var cancellationTokenSource = new CancellationTokenSource();

        // Register for the Ctrl + C (SIGINT) event
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately
            cancellationTokenSource.Cancel();    // Signal cancellation
            Console.WriteLine("Cancellation requested. Stopping...");
        };

        if (args.Length != 8)
        {
            PrintInstructions();
            return;
        }

        string urls = args[0];
        string hosts = args[1];
        int requestsPerSecond = int.Parse(args[2]);
        int maxConnections = int.Parse(args[3]);
        int timeoutInSeconds = int.Parse(args[4]);
        int durationInSeconds = int.Parse(args[5]);
        int readResponseBody = int.Parse(args[6]);
        int sliceFactor = int.Parse(args[7]);

        var urllist = urls.Split(',');
        var hostlist = hosts.Split(',');

        if (hostlist.Length != urllist.Length)
        {
            Console.WriteLine("Number of URLs and Host headers should be equal. Wrong input!");
            return;
        }

        SocketsHttpHandler handler;
        if (maxConnections != 0)
        {
            handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(2),
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(2),
                UseCookies = false,
            };
        }
        else
        {
            handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                UseCookies = false,
            };
        }

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutInSeconds) };

        Console.WriteLine($"Starting RPS generator with {requestsPerSecond} requests per second for {durationInSeconds} seconds...");

        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(durationInSeconds));
        timeTracker.Start();
        await GenerateRequestsAsync(client, urllist[0], hostlist[0], requestsPerSecond, readResponseBody, cancellationTokenSource.Token);
    }

    private static async Task GenerateRequestsAsync(HttpClient client, string url, string host, int requestsPerSecond, int readResponseBody, CancellationToken cancellationToken)
    {
        for (int i = 0; i < requestsPerSecond; i++)
        {
            EnqueueNext(host, DateTime.UtcNow.Ticks);
        }

        var tasks = new List<Task>();
        var delayTask = Task.Delay(1, cancellationToken);
        var expectedNext = DateTime.UtcNow.Ticks + 10000;
        tasks.Add(delayTask);

        while (!cancellationToken.IsCancellationRequested && tasks.Count > 0)
        {
            Task completedTask = await Task.WhenAny(tasks).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Check if the returned task is the delay task
            if (completedTask == delayTask)
            {
                tasks.Remove(delayTask);
                long priority = 0;
                string curHost = string.Empty;
                while (taskQueue.TryPeek(out curHost, out priority))
                {
                    if (priority <= DateTime.UtcNow.Ticks)
                    {
                        taskQueue.Dequeue();
                        tasks.Add(SendRequestAsync(client, url, curHost, readResponseBody, cancellationToken));
                    }
                    else
                    {
                        break;
                    }
                }

                priority = 0;
                long nextDelay;
                if (taskQueue.TryPeek(out curHost, out priority))
                {
                    nextDelay = priority - DateTime.UtcNow.Ticks;
                    if (nextDelay < timerGranularityTicks)
                    {
                        nextDelay = timerGranularityTicks - 10000; // -1ms
                    }
                    nextDelay = nextDelay + 10000; // nextDelay + 1ms
                    delayTask = Task.Delay(TimeSpan.FromTicks(nextDelay), cancellationToken);
                    tasks.Add(delayTask);
                    expectedNext = DateTime.UtcNow.Ticks + nextDelay;
                }
                else
                {
                    expectedNext = DateTime.MaxValue.Ticks;
                }
            }
            else
            {
                tasks.Remove(completedTask);
                var intervalTicks = 100000000; // 10 second
                EnqueueNext(host, DateTime.UtcNow.Ticks + intervalTicks);
                var remainingDelay = expectedNext - DateTime.UtcNow.Ticks;
                if (intervalTicks < remainingDelay)
                {
                    tasks.Remove(delayTask);
                    delayTask = Task.Delay(TimeSpan.FromTicks(intervalTicks), cancellationToken);
                    tasks.Add(delayTask);
                    expectedNext = DateTime.UtcNow.Ticks + intervalTicks;
                }
            }
        }

        PrintStats();
    }

    private static async Task SendRequestAsync(HttpClient client, string url, string host, int readResponseBody, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Host = host;
            if (readResponseBody == 1)
            {
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException)
        {
            Interlocked.Increment(ref failedRequestCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}: {ex.InnerException}");
            Interlocked.Increment(ref failedRequestCount);
        }
        finally
        {
            Interlocked.Increment(ref totalRequestCount);
        }
    }

    private static async Task ResolveAsync(string host, CancellationToken cancellationToken)
    {
        try {
            var ipAddresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (ipAddresses.Length == 0)
            {
                Console.WriteLine($"Failed to resolve host {host}");
                Interlocked.Increment(ref failedRequestCount);
            }
        }
        catch (TaskCanceledException)
        {
            Interlocked.Increment(ref failedRequestCount);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Query failed: {ex.Message}: {ex.InnerException}");
            Interlocked.Increment(ref failedRequestCount);
        }
        finally
        {
            Interlocked.Increment(ref totalRequestCount);
        }
    }

    private static void EnqueueNext(string host, long time)
    {
        taskQueue.Enqueue(host, time);
    }

    private static void PrintInstructions()
    {
        Console.WriteLine("Usage: RpsGenerator <urls> <hosts> <requestsPerSecond> <maxConnections> <timeoutInSeconds> <durationInSeconds> <readResponseBody> <secondSliceFactor>");
        Console.WriteLine("Urls and hosts should be comma separated without space.");
        Console.WriteLine("For putting no cap on maxConnections, set it to 0.");
        Console.WriteLine("Set readResponseBody as 1 or 0.");
        Console.WriteLine("Keep secondSliceFactor as one, unless you want to divide a second into set number of slices and divide RPS spurts among them.");
    }

    private static void PrintStats()
    {
        timeTracker.Stop();
        var failedreq = Interlocked.Read(ref failedRequestCount);
        var totalreq = Interlocked.Read(ref totalRequestCount);
        var seconds = timeTracker.Elapsed.TotalSeconds;
        var effectiveRps = totalreq/seconds;
        Console.WriteLine($"Total requests sent: {totalreq}");
        Console.WriteLine($"Total time in seconds: {seconds}");
        Console.WriteLine($"Measured RPS: {effectiveRps}");
        Console.WriteLine($"Failed requests: {failedreq}");
    }
}