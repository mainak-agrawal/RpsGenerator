using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static long totalRequestCount = 0;
    private static long failedRequestCount = 0;
    private static Stopwatch timeTracker = new Stopwatch();

    static async Task Main(string[] args)
    {
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
                MaxConnectionsPerServer = maxConnections,
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            };
        }
        else
        {
            handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(30),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            };
        }

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutInSeconds) };

        Console.WriteLine($"Starting RPS generator with {requestsPerSecond} requests per second for {durationInSeconds} seconds...");

        var tasks = new List<Task>();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(durationInSeconds));

        timeTracker.Start();

        for (int i = 0; i < urllist.Length; i++)
        {
            tasks.Add(GenerateRequestsAsync(client, urllist[i], hostlist[i], requestsPerSecond, readResponseBody, sliceFactor, cancellationTokenSource.Token));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            Console.WriteLine("Stopped!");
        }
        finally
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

    private static async Task GenerateRequestsAsync(HttpClient client, string url, string host, int requestsPerSecond, int readResponseBody, int sliceFactor, CancellationToken cancellationToken)
    {
        var stopwatch = new Stopwatch();

        while (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Start();
            var requestsInIteration = requestsPerSecond/sliceFactor;
            var tasks = new Task[requestsInIteration];
            int count = 0;

            for (int i = 0; i < requestsInIteration; i++)
            {
                tasks[i] = SendRequestAsync(client, url, host, readResponseBody, cancellationToken);
                count += 1;
            }

            stopwatch.Stop();
            Console.WriteLine($"Fired {count} requests to host {host} at {url} in {stopwatch.ElapsedMilliseconds} ms");
            await Task.Delay((int)1000/sliceFactor - (int)stopwatch.ElapsedMilliseconds);
            stopwatch.Reset();
        }

        throw new OperationCanceledException();
    }

    private static async Task SendRequestAsync(HttpClient client, string url, string host, int readResponseBody, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Host = host;
            if (readResponseBody == 1)
            {
                using var response = await client.SendAsync(request, cancellationToken);
            }
            else
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    private static void PrintInstructions()
    {
        Console.WriteLine("Usage: RpsGenerator <urls> <hosts> <requestsPerSecond> <maxConnections> <timeoutInSeconds> <durationInSeconds> <readResponseBody> <secondSliceFactor>");
        Console.WriteLine("Urls and hosts should be comma separated without space.");
        Console.WriteLine("For putting no cap on maxConnections, set it to 0.");
        Console.WriteLine("Set readResponseBody as 1 or 0.");
        Console.WriteLine("Keep secondSliceFactor as one, unless you want to divide a second into set number of slices and divide RPS spurts among them.");
    }
}