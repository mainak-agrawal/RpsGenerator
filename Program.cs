using System;
using System.Diagnostics;
using System.Net.Http;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private static long totalRequestCount = 0;
    private static long failedRequestCount = 0;
    private static long count2xx = 0;
    private static long count3xx = 0;
    private static long count4xx = 0;
    private static long count5xx = 0;
    private static Stopwatch timeTracker = new Stopwatch();

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
        // string urls = "http://52.140.106.224:80";
        // string hosts = "india-backend.azurewebsites.net";
        // int requestsPerSecond = 300;
        // int maxConnections = 100;
        // int timeoutInSeconds = 10;
        // int durationInSeconds = 30;
        // int readResponseBody = 0;
        // int sliceFactor = 1;

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
        GenerateTimers(client, urllist[0], hostlist[0], requestsPerSecond, readResponseBody, sliceFactor, cancellationTokenSource.Token);

        // for (int i = 0; i < urllist.Length; i++)
        // {
        //     rpsTask = GenerateRequestsAsync(client, urllist[i], hostlist[i], requestsPerSecond, readResponseBody, sliceFactor, cancellationTokenSource.Token);
        // }

        try
        {
            // Await till cancellation token is cancelled
            await Task.Delay(Timeout.Infinite, cancellationTokenSource.Token);
            Console.WriteLine("Cancellation requested. Stopping...");
        }
        catch (Exception)
        {
            Console.WriteLine("Stopped on exception!");
        }
        finally
        {
            timeTracker.Stop();
            var failedreq = Interlocked.Read(ref failedRequestCount);
            var totalreq = Interlocked.Read(ref totalRequestCount);
            var status2xx = Interlocked.Read(ref count2xx);
            var status3xx = Interlocked.Read(ref count3xx);
            var status4xx = Interlocked.Read(ref count4xx);
            var status5xx = Interlocked.Read(ref count5xx);
            var seconds = timeTracker.Elapsed.TotalSeconds;
            var effectiveRps = totalreq/seconds;
            Console.WriteLine($"Total requests sent: {totalreq}");
            Console.WriteLine($"Total time in seconds: {seconds}");
            Console.WriteLine($"Measured RPS: {effectiveRps}");
            Console.WriteLine($"Failed requests: {failedreq}");
            Console.WriteLine($"2xx responses: {status2xx}");
            Console.WriteLine($"3xx responses: {status3xx}");
            Console.WriteLine($"4xx responses: {status4xx}");
            Console.WriteLine($"5xx responses: {status5xx}");
        }
    }

    private static void GenerateTimers(HttpClient client, string url, string host, int requestsPerSecond, int readResponseBody, int sliceFactor, CancellationToken cancellationToken)
    {
        for (int i = 0; i < requestsPerSecond; i++)
        {
            // Create a random number between 1 and 500
            int delay = new Random().Next(1, 500);

            CreateTimer(delay, client, url, host, readResponseBody, cancellationToken);
        }
    }

    private static async Task SendRequestAsync(System.Timers.Timer timer, HttpClient client, string url, string host, int readResponseBody, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Host = host;
            var responseStatusCode = 0;
            if (readResponseBody == 1)
            {
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                responseStatusCode = (int)response.StatusCode;
            }
            else
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                responseStatusCode = (int)response.StatusCode;
            }

            // Switch case to increment the correct counter based on the response status code
            switch (responseStatusCode / 100)
            {
                case 2:
                    Interlocked.Increment(ref count2xx);
                    break;
                case 3:
                    Interlocked.Increment(ref count3xx);
                    break;
                case 4:
                    Interlocked.Increment(ref count4xx);
                    break;
                case 5:
                    Interlocked.Increment(ref count5xx);
                    break;
            }
        }
        catch (TaskCanceledException)
        {
            Interlocked.Increment(ref failedRequestCount);
            timer.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}: {ex.InnerException}");
            Interlocked.Increment(ref failedRequestCount);
        }
        finally
        {
            Interlocked.Increment(ref totalRequestCount);
            if (timer != null)
            {
                timer.Interval = 1000;
                timer.Start();
            }
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

    private static void CreateTimer(int delay, HttpClient client, string url, string host, int readResponseBody, CancellationToken cancellationToken)
    {
        // Create a timer with the delay
        var timer = new System.Timers.Timer(delay);

        // Set the timer to trigger only once
        timer.AutoReset = false;

        // Attach an event handler to the Elapsed event
        timer.Elapsed += (sender, e) =>
        {
            // Send the request
            _ = SendRequestAsync(timer, client, url, host, readResponseBody, cancellationToken);
        };

        // Start the timer
        timer.Start();
    }
}