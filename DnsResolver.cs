using System;
using System.Runtime.InteropServices;
using System.Net;
using System.Text;
using System.Threading;
using System.Net.Sockets;

class DnsResolver
{
    private const string AresLib = "/usr/local/cares/lib/libcares.so";

    // Declare a delegate for the callback function signature used by ares_gethostbyname
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AresCallback(IntPtr arg, int status, int timeouts, IntPtr result);

    public enum AresStatus
    {
        ARES_SUCCESS = 0,

        /* Server error codes (ARES_ENODATA indicates no relevant answer) */
        ARES_ENODATA   = 1,
        ARES_EFORMERR  = 2,
        ARES_ESERVFAIL = 3,
        ARES_ENOTFOUND = 4,
        ARES_ENOTIMP   = 5,
        ARES_EREFUSED  = 6,

        /* Locally generated error codes */
        ARES_EBADQUERY    = 7,
        ARES_EBADNAME     = 8,
        ARES_EBADFAMILY   = 9,
        ARES_EBADRESP     = 10,
        ARES_ECONNREFUSED = 11,
        ARES_ETIMEOUT     = 12,
        ARES_EOF          = 13,
        ARES_EFILE        = 14,
        ARES_ENOMEM       = 15,
        ARES_EDESTRUCTION = 16,
        ARES_EBADSTR      = 17,

        /* ares_getnameinfo error codes */
        ARES_EBADFLAGS = 18,

        /* ares_getaddrinfo error codes */
        ARES_ENONAME   = 19,
        ARES_EBADHINTS = 20,

        /* Uninitialized library error code */
        ARES_ENOTINITIALIZED = 21, /* introduced in 1.7.0 */

        /* ares_library_init error codes */
        ARES_ELOADIPHLPAPI         = 22, /* introduced in 1.7.0 */
        ARES_EADDRGETNETWORKPARAMS = 23, /* introduced in 1.7.0 */

        /* More error codes */
        ARES_ECANCELLED = 24, /* introduced in 1.7.0 */

        /* More ares_getaddrinfo error codes */
        ARES_ESERVICE = 25, /* ares_getaddrinfo() was passed a text service name that
                            * is not recognized. introduced in 1.16.0 */

        ARES_ENOSERVER = 26 /* No DNS servers were configured */
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct AresAddrinfoHints
    {
        public int ai_family;
        public int ai_flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AresAddrinfoNode
    {
        public IntPtr ai_next;
        public int ai_family;
        public IntPtr ai_addr;  // struct sockaddr*
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AresAddrinfo
    {
        public IntPtr nodes; // Pointer to the first AresAddrinfoNode
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AresOptions
    {
        public int evsys; // Event system (set to default)
        public int timeout; // Query timeout
        public int tries; // Number of query attempts
    }

    [DllImport(AresLib)]
    public static extern int ares_library_init(int flags);

    [DllImport(AresLib)]
    public static extern void ares_library_cleanup();

    [DllImport(AresLib)]
    public static extern int ares_threadsafety();

    [DllImport(AresLib)]
    public static extern int ares_init_options(ref IntPtr channel, ref AresOptions options, int optmask);

    [DllImport(AresLib)]
    public static extern void ares_destroy(IntPtr channel);

    [DllImport(AresLib)]
    public static extern void ares_getaddrinfo(
        IntPtr channel, 
        string name, 
        IntPtr serv, 
        ref AresAddrinfoHints hints, 
        AresCallback callback, 
        IntPtr arg);

    [DllImport(AresLib)]
    public static extern void ares_queue_wait_empty(IntPtr channel, int timeout);

    [DllImport(AresLib)]
    public static extern IntPtr ares_strerror(int status);

    [DllImport(AresLib)]
    public static extern void ares_freeaddrinfo(IntPtr result);

    private const int ARES_OPT_EVENT_THREAD = 1 << 22;

    // Import ares_init from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern int ares_init(out IntPtr channel);

    // Import ares_gethostbyname from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern void ares_gethostbyname(IntPtr channel, string name, int family, AresCallback callback, IntPtr arg);

    // // Import ares_process_fd from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern void ares_process_fd(IntPtr channel, int read_fd, int write_fd);

    // // Import ares_fds from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern int ares_fds(IntPtr channel, out IntPtr read_fds, out IntPtr write_fds);

    // Import ares_destroy from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern void ares_destroy(IntPtr channel);

    // // Import ares_library_cleanup from the c-ares library
    // [DllImport("libcares", CallingConvention = CallingConvention.Cdecl)]
    // public static extern void ares_library_cleanup();

    // // timeval struct for timeouts (must match the C version)
    // [StructLayout(LayoutKind.Sequential)]
    // public struct timeval
    // {
    //     public int tv_sec;  // seconds
    //     public int tv_usec; // microseconds
    // }

    // // hostent struct for DNS response (simplified)
    // [StructLayout(LayoutKind.Sequential)]
    // public struct hostent
    // {
    //     public IntPtr h_name;
    //     public IntPtr h_aliases;
    //     public short h_addrtype;
    //     public short h_length;
    //     public IntPtr h_addr_list;
    // }

    // --------------- epoll Related Methods --------------- //

    // // Import epoll_create1
    // [DllImport("libc", SetLastError = true)]
    // public static extern int epoll_create1(int flags);

    // // Import epoll_ctl
    // [DllImport("libc", SetLastError = true)]
    // public static extern int epoll_ctl(int epfd, int op, IntPtr fd, ref epoll_event ev);

    // // Import epoll_wait
    // [DllImport("libc", SetLastError = true)]
    // public static extern int epoll_wait(int epfd, [In, Out] epoll_event[] events, int maxevents, int timeout);

    // // Import close function
    // [DllImport("libc", SetLastError = true)]
    // public static extern int close(int fd);

    // epoll_event struct
    // [StructLayout(LayoutKind.Sequential)]
    // public struct epoll_event
    // {
    //     public uint events; // Epoll events (EPOLLIN, EPOLLOUT)
    //     public int fd;      // File descriptor
    // }
    // Constants for epoll
    // const int EPOLL_CTL_ADD = 1;
    // const int EPOLL_CTL_DEL = 2;
    // const int EPOLL_CTL_MOD = 3;
    // const uint EPOLLIN = 0x001;
    // const uint EPOLLOUT = 0x004;

    // --------------- DNS Query and epoll Wait Implementation --------------- //

    // Callback method to handle DNS responses
    // public static void AresHostCallback(IntPtr arg, int status, int timeouts, IntPtr hostentPtr)
    // {
    //     if (status == 0) // ARES_SUCCESS
    //     {
    //         hostent host = Marshal.PtrToStructure<hostent>(hostentPtr);
    //         string hostName = Marshal.PtrToStringAnsi(host.h_name);
    //         Console.WriteLine($"Resolved Host: {hostName}");

    //         // Access the IP addresses in the h_addr_list (simplified)
    //         IntPtr addrPtr = Marshal.ReadIntPtr(host.h_addr_list);
    //         IPAddress ip = new IPAddress((long)Marshal.ReadInt32(addrPtr));
    //         Console.WriteLine($"IP Address: {ip}");
    //     }
    //     else
    //     {
    //         Console.WriteLine($"DNS Query failed with status: {status}");
    //         Interlocked.Increment(ref Program.failedRequestCount);
    //     }

    //     Interlocked.Increment(ref Program.totalRequestCount);

    //     // Start a new query
    //     if (!cancellationToken.IsCancellationRequested)
    //     {
    //         string fqdn = DnsResolver.GetRandomDomain();
    //         ares_gethostbyname(channel, fqdn, 2 /*AF_INET*/, AresHostCallback, IntPtr.Zero);
    //     }
    // }

    // // Event loop to process DNS queries using epoll
    // public static void WaitAresWithEpoll(int queryCount, CancellationToken cancellationToken)
    // {
    //     int epfd = epoll_create1(0);
    //     if (epfd == -1)
    //     {
    //         Console.WriteLine("Failed to create epoll instance.");
    //         return;
    //     }

    //     // Set up the initial file descriptors for c-ares
    //     ares_fds(channel, out IntPtr read_fds, out IntPtr write_fds);
    //     AddFdsToEpoll(epfd, read_fds, EPOLLIN);
    //     AddFdsToEpoll(epfd, write_fds, EPOLLOUT);

    //     epoll_event[] events = new epoll_event[queryCount];

    //     while (!cancellationToken.IsCancellationRequested)
    //     {
    //         int numEvents = epoll_wait(epfd, events, events.Length, -1);
    //         if (numEvents == -1)
    //         {
    //             Console.WriteLine("epoll_wait failed.");
    //             break;
    //         }

    //         for (int i = 0; i < numEvents; i++)
    //         {
    //             epoll_event ev = events[i];
    //             if (ev.events == EPOLLIN || ev.events == EPOLLOUT)
    //             {
    //                 ares_process_fd(channel, ev.fd, ev.fd);
    //             }
    //         }

    //         // Check if more FDs need to be added
    //         ares_fds(channel, out read_fds, out write_fds);

    //         if (read_fds == IntPtr.Zero && write_fds == IntPtr.Zero)
    //         {
    //             break;
    //         }

    //         AddFdsToEpoll(epfd, read_fds, EPOLLIN);
    //         AddFdsToEpoll(epfd, write_fds, EPOLLOUT);
    //     }

    //     // Cleanup
    //     close(epfd);
    // }

    // // Adds file descriptors to the epoll instance
    // public static void AddFdsToEpoll(int epfd, IntPtr fds, uint events)
    // {
    //     if (fds == IntPtr.Zero) return;

    //     // Iterate of fds which is a pointer to an array of file descriptors
    //     int offset = 0;
    //     while (true)
    //     {
    //         IntPtr currentFdPtr = Marshal.ReadIntPtr(fds, offset);
    //         if (currentFdPtr == IntPtr.Zero)
    //         {
    //             break;
    //         }

    //         int fd = currentFdPtr.ToInt32();
    //         epoll_event ev = new epoll_event
    //         {
    //             events = events,
    //             fd = fd
    //         };

    //         if (epoll_ctl(epfd, EPOLL_CTL_ADD, fd, ref ev) == -1)
    //         {
    //         Console.WriteLine($"Failed to add fd {fd} to epoll.");
    //         }

    //         offset += IntPtr.Size;
    //     }
    // }

    [StructLayout(LayoutKind.Sequential)]
    public struct sockaddr_in
    {
        public short sin_family;
        public ushort sin_port;
        public uint sin_addr;
    }

    public static IntPtr channel = IntPtr.Zero;
    public static CancellationToken cancellationToken;

    public static AresAddrinfoHints hints;

    static void AddrinfoCallback(IntPtr arg, int status, int timeouts, IntPtr result)
    {
        // Print the result status and timeouts
        Console.WriteLine($"Result: {Marshal.PtrToStringAnsi(ares_strerror(status))}, timeouts: {timeouts}");

        // if (status == (int)AresStatus.ARES_ENOTFOUND)
        // {
        //     Console.WriteLine("Error: The requested domain does not exist (NXDOMAIN).");
        //     return; // Exit the callback for this error
        // }

        // If the result is valid, process it
        if (result != IntPtr.Zero)
        {
            // AresAddrinfo addrinfo = Marshal.PtrToStructure<AresAddrinfo>(result);
            // IntPtr nodePtr = addrinfo.nodes;

            // // Loop through the result nodes (address information)
            // while (nodePtr != IntPtr.Zero)
            // {
            //     AresAddrinfoNode node = Marshal.PtrToStructure<AresAddrinfoNode>(nodePtr);

            //     // Process only IPv4 (AF_INET) results
            //     if (node.ai_family == (int)AddressFamily.InterNetwork) // AF_INET
            //     {
            //         IntPtr sockaddrInPtr = node.ai_addr;
            //         sockaddr_in addr_in = Marshal.PtrToStructure<sockaddr_in>(sockaddrInPtr);
            //         string addr = new IPAddress(addr_in.sin_addr).ToString();

            //         Console.WriteLine($"IPv4 Addr: {addr}");
            //     }

            //     // Move to the next node
            //     nodePtr = node.ai_next;
            // }

            // Free the address info result when done
            ares_freeaddrinfo(result);
        }
        else
        {
            Interlocked.Increment(ref Program.failedRequestCount);
        }

        Interlocked.Increment(ref Program.totalRequestCount);
    }

    public static void Initialize(CancellationToken cancellationToken)
    {
        // Initialize c-ares
        if (ares_threadsafety() == 0)
        {
            Console.WriteLine("c-ares not compiled with thread support");
            return;
        }

        AresOptions options = new AresOptions(); // Default event system
        options.evsys = 0; // Default event system
        options.timeout = 2000; // 5 seconds
        options.tries = 4; // 2 attempts
        int optmask = 0;
        optmask |= ARES_OPT_EVENT_THREAD; // Enable event loop
        optmask |= 1 << 1; // ARES_OPT_TIMEOUT
        optmask |= 1 << 2; // ARES_OPT_TRIES

        // Initialize the c-ares channel
        if (ares_init_options(ref channel, ref options, optmask) != (int)AresStatus.ARES_SUCCESS)
        {
            Console.WriteLine("c-ares initialization issue");
            return;
        }
        else
        {
            Console.WriteLine("c-ares initialized successfully");
        }

        // Prepare the hints for IPv4 only (AF_INET)
        hints = new AresAddrinfoHints
        {
            ai_family = (int)AddressFamily.InterNetwork, // AF_INET (IPv4 only)
            ai_flags = 2  // ARES_AI_CANONNAME
        };

        DnsResolver.cancellationToken = cancellationToken;

        // Set the c-ares options
        // ares_options opts = new ares_options();
        // opts.tries = 2;
        // opts.timeout = 5000;
        // ares_set_options(channel, ref opts);
    }

    public static void Run(int queryCount, int cur)
    {
        for (int i = 0; i < queryCount; i++)
        {
            string host = GetRandomDomain(cur+i+1);
            ares_getaddrinfo(channel, host, IntPtr.Zero, ref hints, AddrinfoCallback, IntPtr.Zero);
        }

        // Start the event loop to process the queries
        // WaitAresWithEpoll(queryCount, cancellationToken);

        // // Cleanup
        // ares_destroy(channel);
        // ares_library_cleanup();        
    }

    public static string GetRandomDomain(int total)
    {
        // Random random = new Random();
        // const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        // StringBuilder builder = new StringBuilder();
        // for (int i = 0; i < 10; i++)
        // {
        //     builder.Append(chars[random.Next(chars.Length)]);
        // }
        // builder.Append(".com");
        // Generate a guid
        // StringBuilder builder = new StringBuilder();
        // builder.Append(Guid.NewGuid().ToString().Substring(0, 8));
        // builder.Append(".com");
        // return builder.ToString();
        return Program.randomPrefix.ToString() + total.ToString() + ".com";
    }

    public static void Cleanup()
    {
        // Cleanup
        ares_destroy(channel);
        ares_library_cleanup();
    }

    public static void LibraryInit()
    {
        ares_library_init(1);
    }
}

