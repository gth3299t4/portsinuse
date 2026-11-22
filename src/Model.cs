using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PortsInUse
{
    public class ProcInfo
    {
        public int Pid;
        public string Name;
        public string Path;
        public string Services;

        /// <summary>Name used to group history. svchost etc. get their hosted services appended.</summary>
        public string AppName
        {
            get { return string.IsNullOrEmpty(Services) ? Name : Name + " [" + Services + "]"; }
        }
    }

    public class Snapshot
    {
        public DateTime Time;
        public List<SocketEntry> Entries;
        public Dictionary<int, ProcInfo> Procs;
        public long ElapsedMs;
    }

    /// <summary>Maps PIDs to process names / paths / hosted Windows services. Used from the collector thread only.</summary>
    public class ProcessResolver
    {
        readonly Dictionary<int, ProcInfo> cache = new Dictionary<int, ProcInfo>();
        Dictionary<int, string> services = new Dictionary<int, string>();
        DateTime servicesLoaded = DateTime.MinValue;

        void RefreshServices()
        {
            var map = new Dictionary<int, List<string>>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId <> 0"))
                using (var results = s.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            int pid = Convert.ToInt32(mo["ProcessId"]);
                            List<string> l;
                            if (!map.TryGetValue(pid, out l)) { l = new List<string>(); map[pid] = l; }
                            l.Add((string)mo["Name"]);
                        }
                    }
                }
            }
            catch { }
            services = map.ToDictionary(kv => kv.Key, kv => string.Join(", ", kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
            servicesLoaded = DateTime.Now;
            cache.Clear();
        }

        public Dictionary<int, ProcInfo> Resolve(IEnumerable<int> pids)
        {
            if ((DateTime.Now - servicesLoaded).TotalSeconds > 60) RefreshServices();
            var result = new Dictionary<int, ProcInfo>();
            foreach (int pid in pids)
            {
                if (result.ContainsKey(pid)) continue;
                ProcInfo pi;
                if (!cache.TryGetValue(pid, out pi)) { pi = Lookup(pid); cache[pid] = pi; }
                result[pid] = pi;
            }
            foreach (var k in cache.Keys.Where(k => !result.ContainsKey(k)).ToList()) cache.Remove(k);
            return result;
        }

        ProcInfo Lookup(int pid)
        {
            var pi = new ProcInfo { Pid = pid };
            if (pid == 0) pi.Name = "[No owner]";
            else if (pid == 4) { pi.Name = "System"; pi.Path = "Windows kernel (includes HTTP.sys, SMB)"; }
            else
            {
                pi.Path = Native.GetProcessPath(pid);
                if (!string.IsNullOrEmpty(pi.Path)) pi.Name = System.IO.Path.GetFileName(pi.Path);
                else
                {
                    try { using (var p = Process.GetProcessById(pid)) pi.Name = p.ProcessName + ".exe"; }
                    catch { pi.Name = "PID " + pid; }
                }
            }
            string svc;
            if (services.TryGetValue(pid, out svc)) pi.Services = svc;
            return pi;
        }
    }

    public static class Collector
    {
        public static Snapshot Take(ProcessResolver resolver)
        {
            var sw = Stopwatch.StartNew();
            var entries = Native.GetAllSockets();
            var procs = resolver.Resolve(entries.Select(e => e.Pid));
            return new Snapshot { Time = DateTime.Now, Entries = entries, Procs = procs, ElapsedMs = sw.ElapsedMilliseconds };
        }
    }

    /// <summary>Background reverse-DNS cache.</summary>
    public static class DnsCache
    {
        static readonly ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();
        static readonly BlockingCollection<string> queue = new BlockingCollection<string>();
        public static volatile bool Enabled = true;

        static DnsCache()
        {
            for (int i = 0; i < 4; i++)
            {
                var t = new Thread(Worker) { IsBackground = true, Name = "DNS worker" };
                t.Start();
            }
        }

        public static string Get(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "";
            string v;
            if (cache.TryGetValue(ip, out v)) return v ?? "";
            if (!Enabled) return "";
            if (cache.TryAdd(ip, null)) queue.Add(ip);
            return "";
        }

        static void Worker()
        {
            foreach (var ip in queue.GetConsumingEnumerable())
            {
                string name = "";
                try
                {
                    IPAddress a;
                    if (IPAddress.TryParse(ip, out a))
                    {
                        if (IPAddress.IsLoopback(a)) name = "localhost";
                        else if (!a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any))
                        {
                            var h = Dns.GetHostEntry(a);
                            if (h != null && !string.IsNullOrEmpty(h.HostName) && h.HostName != ip) name = h.HostName;
                        }
                    }
                }
                catch { }
                cache[ip] = name;
            }
        }
    }

    public static class Fmt
    {
        public static string Addr(IPAddress a)
        {
            if (a == null) return "";
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            return a.ToString();
        }

        public static bool IsAny(IPAddress a)
        {
            return a == null || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any);
        }

        public static bool IsLoopback(IPAddress a)
        {
            if (a == null) return false;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            return IPAddress.IsLoopback(a);
        }

        public static string Scope(IPAddress a)
        {
            if (a == null) return "";
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            if (IPAddress.IsLoopback(a)) return "Loopback";
            byte[] b = a.GetAddressBytes();
            if (a.AddressFamily == AddressFamily.InterNetwork)
            {
                if (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)) return "LAN";
                if (b[0] == 169 && b[1] == 254) return "Link-local";
                if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return "CGNAT";
                if (b[0] >= 224 && b[0] <= 239) return "Multicast";
                return "Internet";
            }
            if (a.IsIPv6LinkLocal) return "Link-local";
            if (a.IsIPv6Multicast) return "Multicast";
            if ((b[0] & 0xFE) == 0xFC || a.IsIPv6SiteLocal) return "LAN";
            return "Internet";
        }

        public static string Exposure(IPAddress a)
        {
            if (IsAny(a)) return "All interfaces";
            if (IsLoopback(a)) return "Loopback only";
            return "This address only";
        }

        public static string Time(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            return t.Date == DateTime.Today ? t.ToString("HH:mm:ss") : t.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>Sort key so IPs sort numerically.</summary>
        public static string IpKey(string ip)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a)) return "9" + ip;
            var sb = new StringBuilder(a.AddressFamily == AddressFamily.InterNetwork ? "4" : "6");
            foreach (byte x in a.GetAddressBytes()) sb.Append(x.ToString("X2"));
            return sb.ToString();
        }

        public static string JoinLimited<T>(IEnumerable<T> items, int max)
        {
            var list = items.ToList();
            string s = string.Join(", ", list.Take(max));
            if (list.Count > max) s += ", … (+" + (list.Count - max) + ")";
            return s;
        }
    }

    /// <summary>An aggregated, over-time dependency: app ⇄ remote endpoint.</summary>
    public class DepRecord
    {
        public string Key, App, ProcessPath, Direction, Protocol, RemoteAddress, Scope;
        public int LocalPort;   // service port for inbound, 0 for outbound
        public int RemotePort;  // service port for outbound, 0 for inbound
        public HashSet<int> Pids = new HashSet<int>();
        public DateTime FirstSeen, LastSeen;
        public long TimesSeen, TotalConnections;
        public int MaxConcurrent, CurrentCount;
        public bool Active;
        internal HashSet<string> PrevIds = new HashSet<string>();
        public bool Inbound { get { return Direction == "Inbound"; } }
        public int ServicePort { get { return Inbound ? LocalPort : RemotePort; } }
    }

    public class ListenRecord
    {
        public string Key, App, ProcessPath, Protocol, LocalAddress, Exposure;
        public int Port;
        public HashSet<int> Pids = new HashSet<int>();
        public DateTime FirstSeen, LastSeen;
        public long TimesSeen;
        public bool Active;
        public bool IsTcp { get { return Protocol.StartsWith("TCP"); } }
    }

    public class TrafficModel
    {
        public readonly Dictionary<string, DepRecord> Deps = new Dictionary<string, DepRecord>();
        public readonly Dictionary<string, ListenRecord> Listeners = new Dictionary<string, ListenRecord>();
        public HashSet<int> CurrentListenPorts = new HashSet<int>();
        public long Samples;
        public DateTime FirstSample, LastSample;
        public Snapshot Last;

        public static string DepKey(string app, string dir, string proto, int lport, string rip, int rport)
        {
            return app + "|" + dir + "|" + proto + "|" + lport + "|" + rip + "|" + rport;
        }

        public static string ListenKey(string app, string proto, string addr, int port)
        {
            return app + "|" + proto + "|" + addr + "|" + port;
        }

        public static bool IsListener(SocketEntry e)
        {
            return !e.IsTcp || e.State == Native.TCP_LISTEN;
        }

        public static bool IsConnection(SocketEntry e)
        {
            return e.IsTcp && e.State != Native.TCP_LISTEN && e.State != Native.TCP_CLOSED
                && e.Pid != 0 && !Fmt.IsAny(e.RemoteAddress) && e.RemotePort != 0;
        }

        public string DirectionOf(SocketEntry e)
        {
            if (!e.IsTcp) return "UDP endpoint";
            if (e.State == Native.TCP_LISTEN) return "Listening";
            if (Fmt.IsAny(e.RemoteAddress)) return "";
            return CurrentListenPorts.Contains(e.LocalPort) ? "Inbound" : "Outbound";
        }

        public static ProcInfo Proc(Snapshot s, int pid)
        {
            ProcInfo pi;
            if (s.Procs.TryGetValue(pid, out pi)) return pi;
            return new ProcInfo { Pid = pid, Name = "PID " + pid };
        }

        public void Clear()
        {
            Deps.Clear(); Listeners.Clear();
            Samples = 0; FirstSample = DateTime.MinValue; LastSample = DateTime.MinValue;
        }

        public void Ingest(Snapshot s)
        {
            Samples++;
            if (FirstSample == DateTime.MinValue) FirstSample = s.Time;
            LastSample = s.Time;
            Last = s;

            var listen = new HashSet<int>();
            foreach (var e in s.Entries)
                if (e.IsTcp && e.State == Native.TCP_LISTEN) listen.Add(e.LocalPort);
            CurrentListenPorts = listen;

            var depConns = new Dictionary<DepRecord, HashSet<string>>();
            var seenLis = new HashSet<ListenRecord>();

            foreach (var e in s.Entries)
            {
                var pi = Proc(s, e.Pid);
                if (IsListener(e))
                {
                    string la = Fmt.Addr(e.LocalAddress);
                    string key = ListenKey(pi.AppName, e.Protocol, la, e.LocalPort);
                    ListenRecord l;
                    if (!Listeners.TryGetValue(key, out l))
                    {
                        l = new ListenRecord
                        {
                            Key = key, App = pi.AppName, Protocol = e.Protocol, LocalAddress = la,
                            Port = e.LocalPort, Exposure = Fmt.Exposure(e.LocalAddress), FirstSeen = s.Time
                        };
                        Listeners[key] = l;
                    }
                    l.LastSeen = s.Time;
                    l.Pids.Add(e.Pid);
                    if (l.ProcessPath == null) l.ProcessPath = pi.Path;
                    seenLis.Add(l);
                }
                else if (IsConnection(e))
                {
                    bool inbound = listen.Contains(e.LocalPort);
                    string dir = inbound ? "Inbound" : "Outbound";
                    string ra = Fmt.Addr(e.RemoteAddress);
                    int lport = inbound ? e.LocalPort : 0;
                    int rport = inbound ? 0 : e.RemotePort;
                    string key = DepKey(pi.AppName, dir, "TCP", lport, ra, rport);
                    DepRecord d;
                    if (!Deps.TryGetValue(key, out d))
                    {
                        d = new DepRecord
                        {
                            Key = key, App = pi.AppName, Direction = dir, Protocol = "TCP", LocalPort = lport,
                            RemoteAddress = ra, RemotePort = rport, Scope = Fmt.Scope(e.RemoteAddress), FirstSeen = s.Time
                        };
                        Deps[key] = d;
                    }
                    d.LastSeen = s.Time;
                    d.Pids.Add(e.Pid);
                    if (d.ProcessPath == null) d.ProcessPath = pi.Path;
                    HashSet<string> ids;
                    if (!depConns.TryGetValue(d, out ids)) { ids = new HashSet<string>(); depConns[d] = ids; }
                    ids.Add(Fmt.Addr(e.LocalAddress) + ":" + e.LocalPort + ">" + ra + ":" + e.RemotePort);
                }
            }

            foreach (var d in Deps.Values)
            {
                HashSet<string> ids;
                if (depConns.TryGetValue(d, out ids))
                {
                    d.Active = true;
                    d.TimesSeen++;
                    d.CurrentCount = ids.Count;
                    if (ids.Count > d.MaxConcurrent) d.MaxConcurrent = ids.Count;
                    foreach (var id in ids) if (!d.PrevIds.Contains(id)) d.TotalConnections++;
                    d.PrevIds = ids;
                }
                else
                {
                    d.Active = false;
                    d.CurrentCount = 0;
                    if (d.PrevIds.Count > 0) d.PrevIds = new HashSet<string>();
                }
            }
            foreach (var l in Listeners.Values)
            {
                l.Active = seenLis.Contains(l);
                if (l.Active) l.TimesSeen++;
            }
        }

        // ---------- session persistence (tab separated) ----------

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        static string T(DateTime t) { return t.ToString("o", CultureInfo.InvariantCulture); }
        static DateTime PT(string s) { return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }

        static void AddPids(HashSet<int> set, string s)
        {
            foreach (var p in s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(p.Trim(), out v)) set.Add(v);
            }
        }

        public void Save(string path)
        {
            string tmp = path + ".tmp";
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                w.WriteLine("#PortsInUse session v1\t" + T(DateTime.Now));
                foreach (var d in Deps.Values)
                    w.WriteLine(string.Join("\t", new[]
                    {
                        "D", Clean(d.App), Clean(d.ProcessPath), string.Join(";", d.Pids), d.Direction, d.Protocol,
                        d.LocalPort.ToString(), d.RemoteAddress, d.RemotePort.ToString(), d.Scope,
                        T(d.FirstSeen), T(d.LastSeen), d.TimesSeen.ToString(), d.MaxConcurrent.ToString(), d.TotalConnections.ToString()
                    }));
                foreach (var l in Listeners.Values)
                    w.WriteLine(string.Join("\t", new[]
                    {
                        "L", Clean(l.App), Clean(l.ProcessPath), string.Join(";", l.Pids), l.Protocol, l.LocalAddress,
                        l.Port.ToString(), l.Exposure, T(l.FirstSeen), T(l.LastSeen), l.TimesSeen.ToString()
                    }));
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        /// <summary>Merges a saved session into the current history. Returns number of records read.</summary>
        public int Load(string path)
        {
            int n = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                try
                {
                    if (f[0] == "D" && f.Length >= 15)
                    {
                        int lp = int.Parse(f[6]), rp = int.Parse(f[8]);
                        DateTime first = PT(f[10]), last = PT(f[11]);
                        string key = DepKey(f[1], f[4], f[5], lp, f[7], rp);
                        DepRecord d;
                        if (!Deps.TryGetValue(key, out d))
                        {
                            d = new DepRecord
                            {
                                Key = key, App = f[1], ProcessPath = f[2].Length == 0 ? null : f[2], Direction = f[4],
                                Protocol = f[5], LocalPort = lp, RemoteAddress = f[7], RemotePort = rp, Scope = f[9],
                                FirstSeen = first, LastSeen = last
                            };
                            Deps[key] = d;
                        }
                        else
                        {
                            if (first < d.FirstSeen) d.FirstSeen = first;
                            if (last > d.LastSeen) d.LastSeen = last;
                        }
                        d.TimesSeen += long.Parse(f[12]);
                        d.MaxConcurrent = Math.Max(d.MaxConcurrent, int.Parse(f[13]));
                        d.TotalConnections += long.Parse(f[14]);
                        AddPids(d.Pids, f[3]);
                        n++;
                    }
                    else if (f[0] == "L" && f.Length >= 11)
                    {
                        int port = int.Parse(f[6]);
                        DateTime first = PT(f[8]), last = PT(f[9]);
                        string key = ListenKey(f[1], f[4], f[5], port);
                        ListenRecord l;
                        if (!Listeners.TryGetValue(key, out l))
                        {
                            l = new ListenRecord
                            {
                                Key = key, App = f[1], ProcessPath = f[2].Length == 0 ? null : f[2], Protocol = f[4],
                                LocalAddress = f[5], Port = port, Exposure = f[7], FirstSeen = first, LastSeen = last
                            };
                            Listeners[key] = l;
                        }
                        else
                        {
                            if (first < l.FirstSeen) l.FirstSeen = first;
                            if (last > l.LastSeen) l.LastSeen = last;
                        }
                        l.TimesSeen += long.Parse(f[10]);
                        AddPids(l.Pids, f[3]);
                        n++;
                    }
                }
                catch { /* skip malformed line */ }
            }
            if (n > 0 && FirstSample == DateTime.MinValue)
            {
                var all = Deps.Values.Select(d => d.FirstSeen).Concat(Listeners.Values.Select(l => l.FirstSeen)).ToList();
                if (all.Count > 0) FirstSample = all.Min();
            }
            return n;
        }
    }
}
