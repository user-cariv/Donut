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
    private readonly Timer? _heartbeatTimer;

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

    public NetworkBondingManager()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => RefreshAdapters();
        NetworkChange.NetworkAvailabilityChanged += (_, _) => RefreshAdapters();
        RefreshAdapters();

        // Automatically poll every 2 seconds to immediately detect phones when plugged in or tethered
        _pollTimer = new Timer(_ => RefreshAdapters(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        // Heartbeat probe every 10 seconds to keep iPhone/Android USB Personal Hotspot active 24/7
        _heartbeatTimer = new Timer(_ => KeepMobileConnectionAlive(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10));
    }

    private void KeepMobileConnectionAlive()
    {
        try
        {
            List<NetworkAdapterInfo> phones;
            lock (_lock)
            {
                phones = _adapters.Where(a => a.IsMobilePhone && a.IsEnabled && a.IsOnline && a.LocalIp != null).ToList();
            }

            foreach (var phone in phones)
            {
                try
                {
                    var client = GetOrCreateClient(phone);
                    // Apple's official captive portal test URL - keeps iPhone tethering alive with ~100 bytes of traffic
                    _ = client.GetStringAsync("http://www.apple.com/library/test/success.html");
                }
                catch { }
            }
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
        }
        AdaptersChanged?.Invoke();
    }

    public void RefreshAdapters()
    {
        lock (_lock)
        {
            var detected = new List<NetworkAdapterInfo>();
            var allInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var nic in allInterfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                         && !IPAddress.IsLoopback(a.Address));

                if (ipv4 == null) continue;

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
                    existing.LocalIp = ipv4.Address;
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
                        LocalIp = ipv4.Address,
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

            _adapters.Clear();
            _adapters.AddRange(detected);

            // Enforce Wi-Fi as lowest metric (5) and secondary adapters as higher (60).
            // This ensures:
            // 1. Windows taskbar network icon stays permanently on Wi-Fi (never switches to Ethernet).
            // 2. All regular laptop tasks (Chrome, YouTube, Discord) stay 100% on Home Wi-Fi.
            // 3. MultiChannelDownloader can still bind directly to phone IP (option 31) for maximum 5G boost!
            if (_adapters.Any(a => a.IsEnabled && a.IsOnline))
            {
                EnforcePrimaryWifiPriority(_adapters.Where(a => a.IsEnabled && a.IsOnline));
            }
        }

        AdaptersChanged?.Invoke();
    }

    public static void EnforcePrimaryWifiPriority(IEnumerable<NetworkAdapterInfo> adapters)
    {
        Task.Run(() =>
        {
            try
            {
                foreach (var adp in adapters)
                {
                    if (adp.InterfaceIndex > 0)
                    {
                        // Wi-Fi metric 5 (lowest = primary gateway for Windows & taskbar icon)
                        // Phone tethering metric 60 (secondary = dedicated boost pipeline)
                        int targetMetric = adp.IsWifi ? 5 : 60;
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ip set interface {adp.InterfaceIndex} metric={targetMetric}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        proc?.WaitForExit(500);
                    }
                }
            }
            catch { }
        });
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
                MaxConnectionsPerServer = 16
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
                MaxConnectionsPerServer = 16
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
        _heartbeatTimer?.Dispose();
        foreach (var client in _clientPool.Values)
        {
            client.Dispose();
        }
        _clientPool.Clear();
    }
}
