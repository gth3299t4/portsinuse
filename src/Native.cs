using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace PortsInUse
{
    /// <summary>One row of the TCP/UDP tables (equivalent to one line of "netstat -ano").</summary>
    public class SocketEntry
    {
        public string Protocol;          // TCP, TCPv6, UDP, UDPv6
        public bool IsTcp;
        public IPAddress LocalAddress;
        public int LocalPort;
        public IPAddress RemoteAddress;  // null for UDP
        public int RemotePort;
        public int State;                // TCP only (MIB_TCP_STATE)
        public int Pid;
    }

    internal static class Native
    {
        public const int TCP_CLOSED = 1;
        public const int TCP_LISTEN = 2;

        static readonly string[] TcpStates =
        {
            "", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1",
            "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"
        };

        public static string StateName(int s)
        {
            return s > 0 && s < TcpStates.Length ? TcpStates[s] : s.ToString();
        }

        const int AF_INET = 2;
        const int AF_INET6 = 23;
        const int TCP_TABLE_OWNER_PID_ALL = 5;
        const int UDP_TABLE_OWNER_PID = 1;
        const uint ERROR_INSUFFICIENT_BUFFER = 122;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedTcpTable(IntPtr pTable, ref int size, bool sort, int af, int tableClass, uint reserved);

        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedUdpTable(IntPtr pTable, ref int size, bool sort, int af, int tableClass, uint reserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        public static List<SocketEntry> GetAllSockets()
        {
            var list = new List<SocketEntry>(512);
            ReadTcp4(list);
            ReadTcp6(list);
            ReadUdp4(list);
            ReadUdp6(list);
            return list;
        }

        static IntPtr GetTable(bool tcp, int af)
        {
            int size = 0;
            IntPtr buf = IntPtr.Zero;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                uint r = tcp
                    ? GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0)
                    : GetExtendedUdpTable(buf, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
                if (r == 0 && buf != IntPtr.Zero) return buf;
                if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                if (r != ERROR_INSUFFICIENT_BUFFER && r != 0) return IntPtr.Zero;
                size += 8192; // table can grow between calls
                buf = Marshal.AllocHGlobal(size);
            }
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            return IntPtr.Zero;
        }

        static int Port(int raw)
        {
            return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
        }

        static byte[] Bytes(IntPtr p, int offset, int len)
        {
            var b = new byte[len];
            Marshal.Copy(IntPtr.Add(p, offset), b, 0, len);
            return b;
        }

        static IPAddress V6(IntPtr p, int offset, int scopeOffset)
        {
            long scope = (uint)Marshal.ReadInt32(p, scopeOffset);
            return new IPAddress(Bytes(p, offset, 16), scope);
        }

        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (24 bytes)
        static void ReadTcp4(List<SocketEntry> list)
        {
            IntPtr buf = GetTable(true, AF_INET);
            if (buf == IntPtr.Zero) return;
            try
            {
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr r = IntPtr.Add(buf, 4 + i * 24);
                    list.Add(new SocketEntry
                    {
                        Protocol = "TCP",
                        IsTcp = true,
                        State = Marshal.ReadInt32(r, 0),
                        LocalAddress = new IPAddress(Bytes(r, 4, 4)),
                        LocalPort = Port(Marshal.ReadInt32(r, 8)),
                        RemoteAddress = new IPAddress(Bytes(r, 12, 4)),
                        RemotePort = Port(Marshal.ReadInt32(r, 16)),
                        Pid = Marshal.ReadInt32(r, 20)
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // MIB_TCP6ROW_OWNER_PID (56 bytes)
        static void ReadTcp6(List<SocketEntry> list)
        {
            IntPtr buf = GetTable(true, AF_INET6);
            if (buf == IntPtr.Zero) return;
            try
            {
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr r = IntPtr.Add(buf, 4 + i * 56);
                    list.Add(new SocketEntry
                    {
                        Protocol = "TCPv6",
                        IsTcp = true,
                        LocalAddress = V6(r, 0, 16),
                        LocalPort = Port(Marshal.ReadInt32(r, 20)),
                        RemoteAddress = V6(r, 24, 40),
                        RemotePort = Port(Marshal.ReadInt32(r, 44)),
                        State = Marshal.ReadInt32(r, 48),
                        Pid = Marshal.ReadInt32(r, 52)
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // MIB_UDPROW_OWNER_PID (12 bytes)
        static void ReadUdp4(List<SocketEntry> list)
        {
            IntPtr buf = GetTable(false, AF_INET);
            if (buf == IntPtr.Zero) return;
            try
            {
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr r = IntPtr.Add(buf, 4 + i * 12);
                    list.Add(new SocketEntry
                    {
                        Protocol = "UDP",
                        LocalAddress = new IPAddress(Bytes(r, 0, 4)),
                        LocalPort = Port(Marshal.ReadInt32(r, 4)),
                        Pid = Marshal.ReadInt32(r, 8)
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // MIB_UDP6ROW_OWNER_PID (28 bytes)
        static void ReadUdp6(List<SocketEntry> list)
        {
            IntPtr buf = GetTable(false, AF_INET6);
            if (buf == IntPtr.Zero) return;
            try
            {
                int n = Marshal.ReadInt32(buf);
                for (int i = 0; i < n; i++)
                {
                    IntPtr r = IntPtr.Add(buf, 4 + i * 28);
                    list.Add(new SocketEntry
                    {
                        Protocol = "UDPv6",
                        LocalAddress = V6(r, 0, 16),
                        LocalPort = Port(Marshal.ReadInt32(r, 20)),
                        Pid = Marshal.ReadInt32(r, 24)
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static string GetProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static bool IsElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}
