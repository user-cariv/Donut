using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace DonutBoost.Engine;

internal static class UsbPhoneDisplayName
{
    private const string NetworkClass = "{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string SystemKey = @"SYSTEM\CurrentControlSet";

    internal static string? Find(string adapterId)
    {
        if (!Guid.TryParse(adapterId, out var id)) return null;
        try
        {
            using var connection = Registry.LocalMachine.OpenSubKey($@"{SystemKey}\Control\Network\{NetworkClass}\{id:B}\Connection");
            if (connection?.GetValue("PnpInstanceID") is not string instance ||
                CM_Locate_DevNode(out var node, instance, 0) != 0) return null;

            string? genericName = null;
            for (int depth = 0; depth < 4; depth++)
            {
                if (!instance.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)) break;
                if (instance.StartsWith(@"USB\VID_05AC", StringComparison.OrdinalIgnoreCase)) genericName ??= "Apple phone";
                if (instance.StartsWith(@"USB\VID_04E8", StringComparison.OrdinalIgnoreCase)) genericName ??= "Samsung phone";
                string? reportedName = ReadBusName(node);
                if (reportedName is not null && IsPhoneName(reportedName)) return reportedName;

                using var device = Registry.LocalMachine.OpenSubKey($@"{SystemKey}\Enum\{instance}");
                foreach (var field in new[] { "FriendlyName", "DeviceDesc" })
                {
                    if (device?.GetValue(field) is not string name) continue;
                    name = name[(name.LastIndexOf(';') + 1)..].Trim();
                    if (IsPhoneName(name)) return name;
                }

                if (CM_Get_Parent(out var parent, node, 0) != 0) break;
                node = parent;
                var parentId = new StringBuilder(512);
                if (CM_Get_Device_ID(node, parentId, parentId.Capacity, 0) != 0) break;
                instance = parentId.ToString();
            }
            return genericName;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
        {
            return null;
        }
    }

    private static string? ReadBusName(uint node)
    {
        var key = new DevicePropertyKey { Format = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), Id = 4 };
        var buffer = new byte[512];
        uint length = (uint)buffer.Length;
        return CM_Get_DevNode_Property(node, ref key, out uint type, buffer, ref length, 0) == 0 &&
            type == 0x12 && length >= 2 && length <= buffer.Length
            ? Encoding.Unicode.GetString(buffer, 0, (int)length).TrimEnd('\0').Trim()
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static extern uint CM_Get_DevNode_Property(uint node, ref DevicePropertyKey key,
        out uint type, [Out] byte[] buffer, ref uint length, uint flags);

    private static bool IsPhoneName(string name)
    {
        if (name.Length == 0 || new[] { "ethernet", "tether", "RNDIS", "adapter", "composite", "interface" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)))
            return false;

        return new[] { "iPhone", "iPad", "Galaxy", "Samsung", "Pixel", "OnePlus", "Xiaomi", "Redmi", "Motorola", "Huawei", "OPPO", "vivo", "SM-" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Locate_DevNodeW")]
    private static extern uint CM_Locate_DevNode(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    private static extern uint CM_Get_Device_ID(uint node, StringBuilder deviceId, int length, uint flags);
}
