using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DonutBoost.Models;

namespace DonutBoost.Engine;

public class NetworkBondingManager : IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clientPool = new();
    private readonly List<NetworkAdapterInfo> _adapters = new();
    private readonly object _lock = new();
    private readonly Timer? _pollTimer;
    private readonly Timer? _speedTimer;
    private readonly object _trafficLock = new();
    private readonly Dictionary<string, (long Bytes, long Timestamp)> _trafficCounters = new();
    private readonly NetworkAddressChangedEventHandler _addressChangedHandler;
    private readonly NetworkAvailabilityChangedEventHandler _availabilityChangedHandler;

    // Debounce rapid-fire NetworkChange events during USB plug/unplug
    private long _lastRefreshRequestTick;
    private int _refreshInFlight;

    public BondingMode CurrentMode { get; set; } = BondingMode.CombineAll;

    public IReadOnlyList<NetworkAdapterInfo> Adapters
    {
        get
        {
            lock (_lock)
            {
                return _adapters.ToList();
            }
        }
    }

    public event Action? AdaptersChanged;
    public event Action<double>? AdapterRatesUpdated;

    public NetworkBondingManager()
    {
        _addressChangedHandler = (_, _) => DebouncedRefreshAdapters();
        _availabilityChangedHandler = (_, _) => DebouncedRefreshAdapters();
        NetworkChange.NetworkAddressChanged += _addressChangedHandler;
        NetworkChange.NetworkAvailabilityChanged += _availabilityChangedHandler;
        RefreshAdapters();

        // Poll every 3 seconds (was 2s — reduced frequency to avoid WMI contention during USB changes)
        _pollTimer = new Timer(_ => DebouncedRefreshAdapters(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        _speedTimer = new Timer(_ => RefreshAdapterRates(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// Coalesces multiple rapid refresh requests (e.g. during USB plug/unplug)
    /// into a single background refresh, preventing UI thread stalls.
    /// </summary>
    private void DebouncedRefreshAdapters()
    {
        long now = Environment.TickCount64;
        long last = Interlocked.Read(ref _lastRefreshRequestTick);
        // Skip if another refresh happened less than 800ms ago
        if (now - last < 800) return;
        Interlocked.Exchange(ref _lastRefreshRequestTick, now);

        // Only allow one refresh in-flight at a time
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { RefreshAdapters(); }
            catch { /* Network metadata may disappear while USB tethering changes. */ }
            finally { Interlocked.Exchange(ref _refreshInFlight, 0); }
        });
    }

    /// <summary>
    /// Reads per-adapter NIC receive counters using a snapshot cached from
    /// the last RefreshAdapters() call, avoiding a second heavy
    /// GetAllNetworkInterfaces() call every 500ms.
    /// </summary>
    private NetworkInterface[]? _cachedNics;
    private readonly object _nicCacheLock = new();

    private void RefreshAdapterRates()
    {
        try
        {
            // Use the NIC snapshot cached by RefreshAdapters() so this 500ms
            // timer never calls the expensive GetAllNetworkInterfaces().
            // If no cache exists yet, build one (first call before any refresh).
            NetworkInterface[] nics;
            lock (_nicCacheLock)
            {
                nics = _cachedNics ?? NetworkInterface.GetAllNetworkInterfaces();
            }

            var nicDict = nics
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                            !IsVirtualOrOverlayAdapter(n))
                .ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

            double combinedBytesPerSecond = 0;
            long now = Stopwatch.GetTimestamp();
            lock (_trafficLock)
            {
                foreach (var adapter in Adapters)
                {
                    double rate = 0;
                    if (adapter.IsOnline && nicDict.TryGetValue(adapter.Id, out var nic))
                    {
                        try
                        {
                            // Windows exposes a per-adapter receive counter via
                            // GetIPStatistics. One unsupported adapter must not
                            // stop measurements for the others.
                            long current = nic.GetIPStatistics().BytesReceived;
                            if (_trafficCounters.TryGetValue(adapter.Id, out var previous))
                            {
                                double elapsed = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
                                if (elapsed > 0 && current >= previous.Bytes)
                                    rate = (current - previous.Bytes) / elapsed;
                            }
                            _trafficCounters[adapter.Id] = (current, now);
                        }
                        catch (NetworkInformationException) { _trafficCounters.Remove(adapter.Id); }
                    }

                    adapter.CurrentSpeedBps = rate;
                    adapter.NotifyMetricsChanged();
                    combinedBytesPerSecond += rate;
                }
            }

            AdapterRatesUpdated?.Invoke(combinedBytesPerSecond);
        }
        catch { }
    }

    public void SetMode(BondingMode mode)
    {
        lock (_lock)
        {
            CurrentMode = mode;
            foreach (var adp in _adapters)
            {
                switch (mode)
                {
                    case BondingMode.CombineAll:
                        adp.IsEnabled = true;
                        break;
                    case BondingMode.WifiOnly:
                        adp.IsEnabled = !adp.IsMobilePhone;
                        break;
                    case BondingMode.MobileDataOnly:
                        adp.IsEnabled = adp.IsMobilePhone;
                        break;
                }
            }
            // Modes only control which adapters Donut's own downloader uses.
            // Leave Windows routes and interface metrics under Windows/user control.
        }
        AdaptersChanged?.Invoke();
    }

    public void RefreshAdapters()
    {
        // Run the expensive WMI call OUTSIDE the lock so the UI thread
        // is never blocked waiting for _lock while GetAllNetworkInterfaces()
        // stalls during USB plug/unplug events.
        NetworkInterface[] allInterfaces;
        try
        {
            allInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return; // transient — adapter is mid-plug; next poll will catch it
        }

        // Cache the NIC array so RefreshAdapterRates (500ms timer) never
        // has to call GetAllNetworkInterfaces() independently.
        lock (_nicCacheLock)
        {
            _cachedNics = allInterfaces;
        }

        lock (_lock)
        {
            var detected = new List<NetworkAdapterInfo>();

            foreach (var nic in allInterfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (IsVirtualOrOverlayAdapter(nic)) continue;

                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                         && !IPAddress.IsLoopback(a.Address));

                if (ipv4 == null) continue;

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address))?.Address;

                int ifIndex = 0;
                try
                {
                    var ipv4Props = ipProps.GetIPv4Properties();
                    if (ipv4Props != null)
                    {
                        ifIndex = ipv4Props.Index;
                    }
                }
                catch
                {
                    // If IPv4 properties query fails, ifIndex remains 0
                }

                var existing = _adapters.FirstOrDefault(a => a.Id == nic.Id || a.Name.Equals(nic.Name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // CRITICAL: Preserve existing instance so live download metrics and UI bindings never get severed!
                    existing.Name = nic.Name;
                    existing.Description = nic.Description;
                    existing.InterfaceIndex = ifIndex;
                    existing.InterfaceType = nic.NetworkInterfaceType;
                    existing.LocalIp = ipv4?.Address;
                    existing.GatewayIp = gateway;
                    existing.SpeedBitsPerSecond = nic.Speed;
                    existing.IsOnline = true;
                    existing.NotifyAdapterMetadataChanged();
                    detected.Add(existing);
                }
                else
                {
                    var adapter = new NetworkAdapterInfo
                    {
                        Id = nic.Id,
                        Name = nic.Name,
                        Description = nic.Description,
                        InterfaceIndex = ifIndex,
                        InterfaceType = nic.NetworkInterfaceType,
                        LocalIp = ipv4?.Address,
                        GatewayIp = gateway,
                        SpeedBitsPerSecond = nic.Speed,
                        IsEnabled = true,
                        IsOnline = true
                    };

                    switch (CurrentMode)
                    {
                        case BondingMode.CombineAll:
                            adapter.IsEnabled = true;
                            break;
                        case BondingMode.WifiOnly:
                            adapter.IsEnabled = !adapter.IsMobilePhone;
                            break;
                        case BondingMode.MobileDataOnly:
                            adapter.IsEnabled = adapter.IsMobilePhone;
                            break;
                    }

                    detected.Add(adapter);
                    GetOrCreateClient(adapter);
                }
            }

            foreach (var old in _adapters)
            {
                if (!detected.Any(d => d.Id == old.Id))
                {
                    old.IsOnline = false;
                    old.CurrentSpeedBps = 0;
                }
            }

            bool hasStructuralChange = detected.Count != _adapters.Count ||
                detected.Any(d => !_adapters.Any(a => a.Id == d.Id && a.IsOnline == d.IsOnline && Equals(a.LocalIp, d.LocalIp)));

            _adapters.Clear();
            _adapters.AddRange(detected);

            if (hasStructuralChange)
            {
                AdaptersChanged?.Invoke();
            }
        }
    }

    private static bool IsVirtualOrOverlayAdapter(NetworkInterface nic)
    {
        string identity = $"{nic.Name} {nic.Description}";
        string[] virtualMarkers =
        {
            "vEthernet", "Hyper-V", "VMware", "VirtualBox", "Host-Only",
            "WSL", "Docker", "Container", "TAP-Windows", "Wintun",
            "WireGuard", "ZeroTier", "OpenVPN", "VPN", "Wi-Fi Direct Virtual"
        };
        return virtualMarkers.Any(marker => identity.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public HttpClient GetOrCreateClient(NetworkAdapterInfo adapter)
    {
        if (adapter.LocalIp == null)
        {
            var fallbackHandler = new SocketsHttpHandler
            {
                UseProxy = false, // NEVER route through localhost proxy
                Proxy = null,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true,
                MaxConnectionsPerServer = 24
            };
            var fallbackClient = new HttpClient(fallbackHandler) { Timeout = TimeSpan.FromMinutes(10) };
            fallbackClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return fallbackClient;
        }

        return _clientPool.GetOrAdd(adapter.Id, _ =>
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false, // CRITICAL: NEVER route direct downloads through local Game Boost proxy!
                Proxy = null,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        var currentIp = adapter.LocalIp;
                        if (currentIp == null)
                        {
                            throw new InvalidOperationException($"Adapter '{adapter.Name}' no longer has an active IPv4 address.");
                        }

                        int ifIndex = adapter.InterfaceIndex;

                        // CRITICAL FOR SPEED BONDING:
                        // Sockets bound to non-default adapters (like 5G USB tethering) will route
                        // packets through Wi-Fi unless IP_UNICAST_IF (Winsock option 31) overrides it.
                        if (ifIndex > 0)
                        {
                            try
                            {
                                int netIndex = IPAddress.HostToNetworkOrder(ifIndex);
                                socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, netIndex);
                            }
                            catch { }
                        }

                        // Explicitly bind the socket to this adapter's IP address
                        socket.Bind(new IPEndPoint(currentIp, 0));

                        // Explicitly resolve DNS to IPv4 address to match the IPv4 socket
                        var ipAddresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                        var targetIp = ipAddresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                                       ?? ipAddresses.First();

                        await socket.ConnectAsync(targetIp, context.DnsEndPoint.Port, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true,
                MaxConnectionsPerServer = 24
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            return client;
        });
    }

    public List<NetworkAdapterInfo> GetActiveEnabledAdapters()
    {
        lock (_lock)
        {
            return _adapters.Where(a => a.IsEnabled && a.IsOnline && a.LocalIp != null).ToList();
        }
    }

    public void Dispose()
    {
        _pollTimer?.Dispose();
        _speedTimer?.Dispose();
        NetworkChange.NetworkAddressChanged -= _addressChangedHandler;
        NetworkChange.NetworkAvailabilityChanged -= _availabilityChangedHandler;
        foreach (var client in _clientPool.Values)
        {
            client.Dispose();
        }
        _clientPool.Clear();
    }
}
