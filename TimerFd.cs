using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using static TimerFd;

class TimerFd
{
    // Constants
    private const int CLOCK_REALTIME = 0;
    private const int TFD_NONBLOCK = 0x800;
    private const int TFD_CLOEXEC = 0x080000;
    private const int EPOLLIN = 0x001; // Event for "readable"
    private const int EPOLL_CTL_ADD = 1;
    private const int MAX_TIMERS = 300;
    private static int[] TimerFds;
    private static int EpollFd;

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    public struct Timespec
    {
        public long tv_sec;  // seconds
        public long tv_nsec; // nanoseconds
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Itimerspec
    {
        public Timespec it_interval;  // Interval for periodic timer
        public Timespec it_value;     // Initial expiration
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EpollEvent
    {
        public uint events;   // Epoll events (readable, writable, etc.)
        public int fd;        // File descriptor associated with the event
    }


    // P/Invoke for timerfd_create
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int timerfd_create(int clockid, int flags);

    // P/Invoke for timerfd_settime
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int timerfd_settime(int fd, int flags, ref Itimerspec new_value, IntPtr old_value);

    // P/Invoke for read
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern long read(int fd, byte[] buffer, ulong count);

    [DllImport("libc", SetLastError = true)]
    public static extern int dup(int oldfd);

    // Close
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int close(int fd);

        // P/Invoke for epoll_create1
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int epoll_create1(int flags);

    // P/Invoke for epoll_ctl
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int epoll_ctl(int epfd, int op, int fd, ref EpollEvent ev);

    // P/Invoke for epoll_wait
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int epoll_wait(int epfd, [In, Out] EpollEvent[] events, int maxevents, int timeout);

    public static void Init(HttpClient client, string url, string host, int rps, int readResponseBody, CancellationToken cancellationToken)
    {
        Random rand = new Random();

        // Step 1: Create an epoll instance
        EpollFd = epoll_create1(0);
        if (EpollFd == -1)
        {
            Console.WriteLine("Failed to create epoll instance");
            return;
        }
        else
        {
            Console.WriteLine($"Created epoll instance {EpollFd}");
        }

        // Step 2: Create 100 timerfds and add them to epoll
        TimerFds = new int[rps];
        int j = 0;
        while (j < rps)
        {
            // Create a timerfd for each timer
            int fd = timerfd_create(CLOCK_REALTIME, TFD_NONBLOCK | TFD_CLOEXEC);
            if (fd == 0)
            {
                Console.WriteLine("0 fd returned");

                int newFd = dup(fd);
                if (newFd == -1)
                {
                    Console.WriteLine("Failed to duplicate file descriptor");
                    return;
                }
                
                close(fd);
                fd = newFd;
            }

            if (fd == -1)
            {
                Console.WriteLine($"Failed to create timerfd {j}");
                return;
            }
            else
            {
                Console.WriteLine($"Created timerfd {fd}");
            }

            TimerFds[j] = fd;

            // Set the timer to trigger at a random time within 10 seconds
            int randomTime = rand.Next(1, 500); // Random time between 1 and 500 ms
            Itimerspec newTimer = new Itimerspec
            {
                it_value = new Timespec
                {
                    tv_sec = 0,
                    tv_nsec = randomTime * 1000000  // Convert ms to ns
                },
                it_interval = new Timespec
                {
                    tv_sec = 0,  // One-shot timer, no interval
                    tv_nsec = 0
                }
            };

            int res = timerfd_settime(TimerFds[j], 0, ref newTimer, IntPtr.Zero);
            if (res == -1)
            {
                Console.WriteLine($"Failed to set time for timerfd {j}");
                return;
            }

            // Register the timerfd with epoll
            EpollEvent ev = new EpollEvent
            {
                events = EPOLLIN,  // Monitor for readable events
                fd = TimerFds[j]
            };

            res = epoll_ctl(EpollFd, EPOLL_CTL_ADD, TimerFds[j], ref ev);
            if (res == -1)
            {
                Console.WriteLine($"Failed to add timerfd {j} to epoll");
                return;
            }

            j += 1;
        }

        Console.WriteLine("Waiting for timers to expire...");

        // Step 3: Wait for the timers to expire using epoll_wait
        EpollEvent[] events = new EpollEvent[rps];  // Array to hold events
        byte[] buffer = new byte[8];  // Buffer to read from timerfd

        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait for any timer event (maximum timeout -1 for indefinite wait)
            int n = epoll_wait(EpollFd, events, rps, -1);
            if (n == -1)
            {
                Console.WriteLine("epoll_wait failed");
                return;
            }

            for (int i = 0; i < n; i++)
            {
                int timerFd = events[i].fd;
                if (timerFd == -1 || timerFd == 0 || timerFd == 1)
                {
                    Console.WriteLine($"Invalid file descriptor {timerFd} skipping");
                    continue;
                }

                Console.WriteLine($"Reading Timer Fd {timerFd}...");

                // Step 4: Read from the timerfd to acknowledge the event
                long result = read(timerFd, buffer, 8);

                if (result == -1)
                {
                    Console.WriteLine($"Failed to read timer {events[i].fd}");
                }
                else
                {
                    // The read buffer will hold the number of expirations
                    ulong expirations = BitConverter.ToUInt64(buffer, 0);
                    Console.WriteLine($"Timer {events[i].fd} expired {expirations} time(s)");
                    _ = Program.SendRequestAsync(timerFd, client, url, host, readResponseBody, cancellationToken);
                    Console.WriteLine("Sent request");
                }
            }
        }
    }

    public static void SetTimer(int timerFd, int sec)
    {
        Itimerspec newTimer = new Itimerspec
        {
            it_value = new Timespec
            {
                tv_sec = sec,
                tv_nsec = 0
            },
            it_interval = new Timespec
            {
                tv_sec = 0,  // One-shot timer, no interval
                tv_nsec = 0
            }
        };

        int res = timerfd_settime(timerFd, 0, ref newTimer, IntPtr.Zero);
        if (res == -1)
        {
            Console.WriteLine($"Failed to set time for timerfd {timerFd}");
            return;
        }
    }

    public static void CloseAll()
    {
        foreach (int fd in TimerFds)
        {
            FileDescriptorClose(fd);
        }
        FileDescriptorClose(EpollFd);
    }

    // Close the file descriptor
    private static void FileDescriptorClose(int fd)
    {
        close(fd);
    }
}
