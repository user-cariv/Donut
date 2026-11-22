using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
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

                var gateway = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address))?.Address;

                bool isPhone = nic.Description.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
                               nic.Description.Contains("NDIS", StringComparison.OrdinalIgnoreCase) ||
                               nic.Description.Contains("RNDIS", StringComparison.OrdinalIgnoreCase) ||
                               nic.Description.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                               nic.Name.Contains("Cellular", StringComparison.OrdinalIgnoreCase) ||
                               nic.Name.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

                if (gateway == null && ipv4 != null && isPhone)
                {
                    var b = ipv4.Address.GetAddressBytes();
                    b[3] = 1; // Apple & Android hotspot gateway default is .1
                    gateway = new IPAddress(b);
                }

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
        _heartbeatTimer?.Dispose();
        foreach (var client in _clientPool.Values)
        {
            client.Dispose();
        }
        _clientPool.Clear();
    }
}
