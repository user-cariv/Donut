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
            // Apply metrics to disabled adapters too. If Wi-Fi keeps its old
            // low route metric when Mobile Data Only is selected, Windows can
            // keep sending launcher traffic over Wi-Fi.
            ApplyBondingModeMetrics(_adapters.Where(a => a.IsOnline).ToList(), CurrentMode);
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

            // Apply bonding metrics based on current mode:
            // CombineAll: Metric 15 on ALL adapters (Equal Cost Multi-Path) + IPv4 Precedence
            // WifiOnly: Wi-Fi metric 5, Secondary 60
            // MobileDataOnly: Mobile metric 5, Secondary 60
            if (_adapters.Any(a => a.IsEnabled && a.IsOnline))
            {
                ApplyBondingModeMetrics(_adapters.Where(a => a.IsOnline).ToList(), CurrentMode);
            }

            if (hasStructuralChange)
            {
                AdaptersChanged?.Invoke();
            }
        }
    }

    public static void SetIpv4Precedence(bool preferIpv4)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", true);
            if (key != null)
            {
                if (preferIpv4)
                {
                    // 0x20 = RFC 3484/6724: Prefer IPv4 over IPv6. This forces game launchers like Steam to resolve IPv4 CDN edges
                    // so connections bond across both Wi-Fi and Mobile Data (since USB tethering only provides IPv4).
                    key.SetValue("DisabledComponents", 0x20, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue("DisabledComponents", 0, RegistryValueKind.DWord);
                }
            }
        }
        catch { }
    }

    public static void ApplyBondingModeMetrics(IEnumerable<NetworkAdapterInfo> adapters, BondingMode mode)
    {
        Task.Run(() =>
        {
            try
            {
                bool isCombining = mode == BondingMode.CombineAll;
                SetIpv4Precedence(isCombining);

                foreach (var adp in adapters)
                {
                    if (adp.InterfaceIndex > 0)
                    {
                        // In CombineAll:
                        // Metric 12 for Mobile 5G and Metric 15 for Wi-Fi ensures high-speed 300+ Mbps 5G pipelines
                        // take primary routing priority, while Wi-Fi bonds in parallel for combined max speed!
                        // In WifiOnly: Wi-Fi is 5, Mobile is 60.
                        // In MobileOnly: Mobile is 5, Wi-Fi is 60.
                        int targetMetric = 15;
                        if (mode == BondingMode.WifiOnly)
                        {
                            targetMetric = adp.IsWifi ? 5 : 60;
                        }
                        else if (mode == BondingMode.MobileDataOnly)
                        {
                            targetMetric = adp.IsMobilePhone ? 5 : 60;
                        }
                        else if (mode == BondingMode.CombineAll)
                        {
                            // In Combine Both: Metric 10 for Mobile 5G/4G ensures high-speed 300+ Mbps 5G pipelines
                            // take primary route priority without being starved by slower 60 Mbps Wi-Fi,
                            // while Wi-Fi (Metric 15) bonds in parallel via multi-path WeakHost routing!
                            targetMetric = adp.IsMobilePhone ? 10 : 15;
                        }

                        // 1. Instant native netsh interface metric & weakhostsend/receive enforcement (takes ~5ms)
                        try
                        {
                            using var proc4 = Process.Start(new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ipv4 set interface {adp.InterfaceIndex} metric={targetMetric}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            proc4?.WaitForExit(300);
                        }
                        catch { }

                        try
                        {
                            using var procWeak = Process.Start(new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ipv4 set interface {adp.InterfaceIndex} weakhostsend=enabled weakhostreceive=enabled",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            procWeak?.WaitForExit(300);
                        }
                        catch { }

                        // 2. Route table default gateway update (route.exe)
                        if (adp.GatewayIp != null)
                        {
                            try
                            {
                                using var procRoute = Process.Start(new ProcessStartInfo
                                {
                                    FileName = "route",
                                    Arguments = $"CHANGE 0.0.0.0 MASK 0.0.0.0 {adp.GatewayIp} METRIC {targetMetric} IF {adp.InterfaceIndex}",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                });
                                procRoute?.WaitForExit(300);
                                if (procRoute?.ExitCode != 0)
                                {
                                    using var procRouteAdd = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "route",
                                        Arguments = $"ADD 0.0.0.0 MASK 0.0.0.0 {adp.GatewayIp} METRIC {targetMetric} IF {adp.InterfaceIndex}",
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    });
                                    procRouteAdd?.WaitForExit(300);
                                }
                            }
                            catch { }
                        }

                        // 3. Direct Registry InterfaceMetric update for this adapter GUID (authoritative for Windows TCP stack)
                        try
                        {
                            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adp.Id}", true);
                            key?.SetValue("InterfaceMetric", targetMetric, RegistryValueKind.DWord);
                        }
                        catch { }

                        // 4. PowerShell NetTCPIP cmdlet enforcement
                        try
                        {
                            string psCmd = $"Set-NetIPInterface -InterfaceIndex {adp.InterfaceIndex} -InterfaceMetric {targetMetric} -WeakHostSend Enabled -WeakHostReceive Enabled -ErrorAction SilentlyContinue; " +
                                            $"Get-NetRoute -DestinationPrefix '0.0.0.0/0' -InterfaceIndex {adp.InterfaceIndex} -ErrorAction SilentlyContinue | Set-NetRoute -RouteMetric {targetMetric} -ErrorAction SilentlyContinue";
                            using var procPs = Process.Start(new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psCmd}\"",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            procPs?.WaitForExit(2500);
                        }
                        catch { }

                        // 5. Enforce IPv6 Metric
                        try
                        {
                            using var proc6 = Process.Start(new ProcessStartInfo
                            {
                                FileName = "netsh",
                                Arguments = $"interface ipv6 set interface {adp.InterfaceIndex} metric={targetMetric}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            proc6?.WaitForExit(300);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        });
    }

    public static void EnforcePrimaryWifiPriority(IEnumerable<NetworkAdapterInfo> adapters)
    {
        ApplyBondingModeMetrics(adapters, BondingMode.CombineAll);
    }

    public static void RestoreDefaultMetrics(IEnumerable<NetworkAdapterInfo> adapters)
    {
        try
        {
            SetIpv4Precedence(false);
            foreach (var adp in adapters)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adp.Id}", true);
                    key?.DeleteValue("InterfaceMetric", false);
                }
                catch { }

                if (adp.InterfaceIndex > 0)
                {
                    try
                    {
                        using var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"interface ipv4 set interface {adp.InterfaceIndex} metric=automatic",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        proc?.WaitForExit(200);
                    }
                    catch { }
                }
            }
        }
        catch { }
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
        RestoreDefaultMetrics(_adapters);
        foreach (var client in _clientPool.Values)
        {
            client.Dispose();
        }
        _clientPool.Clear();
    }
}
