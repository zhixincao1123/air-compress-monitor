using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using AirCompressMonitor.Comm.Diagnostics;
using AirCompressMonitor.Comm.Models;
using AirCompressMonitor.Comm.Modbus;
using AirCompressMonitor.Storage;

namespace AirCompressMonitor.SmokeTest
{
    /// <summary>
    /// 冒烟测试。不连真实设备，只验证「纯软件部分」是否成立：
    /// 字节序换算与判定、规则库诊断、以及最关键的 —— SQLite 原生库能否真的落盘并读回。
    ///
    /// 之所以要有这个：SQLite 在 .NET Framework 上跑，靠的是随程序部署的 e_sqlite3.dll，
    /// 位数不对或没拷到输出目录时，编译期一切正常、运行期第一次 Open 才炸。
    /// 这类问题必须在交付前用运行验证兜住，不能靠"编译通过"下结论。
    /// </summary>
    internal class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== AirCompressMonitor smoke test ===");

            string root = Path.Combine(Path.GetTempPath(),
                "acm-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            try
            {
                TestEndianness();
                TestEndianDetector();
                TestDiagnostics();
                TestStorage(root);
                TestSqliteOnlyRetention(root);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FATAL] unhandled: " + ex);
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine(string.Format("=== passed {0}, failed {1} ===", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        // =====================================================================

        private static void TestEndianness()
        {
            Section("Endianness");

            Check("SwapBytes(0x0064) == 0x6400",
                Endianness.SwapBytes(0x0064) == 0x6400);

            Check("SwapBytes is its own inverse",
                Endianness.SwapBytes(Endianness.SwapBytes(0x1234)) == 0x1234);

            // 0x0064 原样 = 100；换字节后 = 25600。这正是"用错字节序解出离谱值"的典型。
            Check("ApplyWord plain keeps value",
                Endianness.ApplyWord(0x0064, EndianMode.BigEndian) == 0x0064);
            Check("ApplyWord byte-swap swaps",
                Endianness.ApplyWord(0x0064, EndianMode.BigEndianByteSwap) == 0x6400);

            // 32 位字序：大端 0x0001,0x0002 -> 0x00010002
            var regs = new ushort[] { 0x0001, 0x0002 };
            Check("ToUInt32 big-endian word order",
                Endianness.ToUInt32(regs, 0, EndianMode.BigEndian) == 0x00010002u);
            Check("ToUInt32 little-endian word order",
                Endianness.ToUInt32(regs, 0, EndianMode.LittleEndian) == 0x00020001u);
        }

        private static void TestEndianDetector()
        {
            Section("EndianDetector");

            // 量程 0~400，喂真实压力 100..123 的"原样"编码 —— 应当锁到大端
            var a = new EndianDetector(1.0, 0, 400);
            for (int i = 0; i < 24; i++) a.Feed((ushort)(100 + i));
            Check("locks to BigEndian on plausible plain values", a.IsLocked);
            Check("resolves BigEndian", a.Resolve(EndianMode.Auto) == EndianMode.BigEndian);

            // 同一批数但寄存器里是换过字节的 —— 应当锁到换字节模式
            var b = new EndianDetector(1.0, 0, 400);
            for (int i = 0; i < 24; i++) b.Feed(Endianness.SwapBytes((ushort)(100 + i)));
            Check("locks to BigEndianByteSwap on swapped wire values", b.IsLocked);
            Check("resolves BigEndianByteSwap",
                b.Resolve(EndianMode.Auto) == EndianMode.BigEndianByteSwap);

            // 设备没刷新（全窗口同一个值）时不能下结论
            var c = new EndianDetector(1.0, 0, 400);
            for (int i = 0; i < 24; i++) c.Feed(0x0064);
            Check("refuses to lock when signal is frozen", !c.IsLocked);

            // 配置写了具体值就必须听配置，不能被判定器覆盖
            Check("configured mode overrides detector",
                b.Resolve(EndianMode.BigEndian) == EndianMode.BigEndian);
        }

        private static void TestDiagnostics()
        {
            Section("Diagnostics");

            var analyzer = new RuleBasedExceptionAnalyzer();

            var d1 = analyzer.Analyze(new TimeoutException("no response"), "slave 1 poll");
            Check("timeout -> Communication", d1.Category == FaultCategory.Communication);
            Check("timeout -> Fault level", d1.Level == AlarmLevel.Fault);
            Check("timeout -> has suggestion", !string.IsNullOrEmpty(d1.Suggestion));

            var d2 = analyzer.Analyze(
                new IOException("port closed", new SocketException(10061)), "link open");
            Check("socket-in-io -> Communication", d2.Category == FaultCategory.Communication);

            var d3 = analyzer.Analyze(new ArgumentOutOfRangeException("startAddress"), "slave 3");
            Check("bad arg -> Configuration", d3.Category == FaultCategory.Configuration);

            var d4 = analyzer.Analyze(new Exception("something odd"), "slave 2");
            Check("unknown -> Unknown but still advises",
                d4.Category == FaultCategory.Unknown && !string.IsNullOrEmpty(d4.Suggestion));

            var d5 = analyzer.AnalyzeAlarm(new AlarmInfo
            {
                SlaveId = 1, SlaveName = "1#", State = "报警",
                Message = "排气温度过高触发", Level = AlarmLevel.Fault
            });
            Check("alarm bit -> device rule hit",
                d5.Category == FaultCategory.Device &&
                d5.Suggestion.Contains("冷却器"));

            var d6 = analyzer.AnalyzeAlarm(new AlarmInfo
            {
                SlaveId = 1, SlaveName = "1#", State = "恢复",
                Message = "排气温度过高恢复", Level = AlarmLevel.Info
            });
            Check("recovered alarm -> Info", d6.Level == AlarmLevel.Info);
        }

        // =====================================================================

        private static void TestStorage(string root)
        {
            Section("Storage (SQLite + CSV dual mode)");

            var options = new DataStoreOptions
            {
                RootDirectory = root,
                Mode = StoreMode.Both,
                RetentionDays = 30,
                BatchSize = 4,          // 故意调小，逼出"攒批落盘"这条路径
                FlushIntervalMs = 60000
            };

            var hub = new DataStoreHub(options);
            var errors = new List<string>();
            hub.Error += m => errors.Add(m);

            hub.Initialize();
            Check("hub initialized", File.Exists(options.DatabasePath));
            Check("native e_sqlite3.dll deployed",
                File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "e_sqlite3.dll")));

            // 昨天 3 条 + 今天 5 条：昨天那批用来验证归档
            var yesterday = DateTime.Now.Date.AddDays(-1).AddHours(10);
            var today = DateTime.Now.Date.AddHours(9);

            for (int i = 0; i < 3; i++)
                hub.Append(MakeData(1, yesterday.AddMinutes(i * 10), 100 + i));
            for (int i = 0; i < 5; i++)
                hub.Append(MakeData(2, today.AddMinutes(i), 200 + i));

            hub.Flush();
            Check("no storage errors", errors.Count == 0);
            if (errors.Count > 0) Console.WriteLine("      errors: " + string.Join(" | ", errors.ToArray()));

            long total = hub.Count();
            Check("count == 8 after flush", total == 8);

            var todayRows = new List<DeviceData>(hub.Query(today, today.AddHours(1), 0));
            Check("query today returns 5", todayRows.Count == 5);
            Check("query preserves slave id",
                todayRows.Count > 0 && todayRows[0].SlaveId == 2);
            Check("query preserves pressure",
                todayRows.Count > 0 && Math.Abs(todayRows[0].PressureValue - 200) < 0.001);

            var slave1Rows = new List<DeviceData>(hub.Query(yesterday, today.AddHours(1), 1));
            Check("query filters by slave id", slave1Rows.Count == 3);

            // 归档 + 清理：保留 30 天，所以只有归档会动作，清理不该删今天/昨天的数据
            var report = hub.RunMaintenance();
            Check("maintenance archived something", report.DidAnything);

            string archived = Path.Combine(options.ArchiveDirectory,
                yesterday.ToString("yyyy-MM-dd") + ".csv");
            Check("archive file for yesterday exists", File.Exists(archived));

            if (File.Exists(archived))
            {
                var lines = File.ReadAllLines(archived);
                Check("archive has header + 3 rows", lines.Length == 4);
                Check("archive header intact", lines.Length > 0 && lines[0].StartsWith("时间,"));
            }

            Check("today data survived maintenance", hub.Count() == 5);

            // 保留期收窄到 1 天 -> 昨天及更早都进入清理范围。
            // 但昨天的表在第一次维护时已经归档并 DROP，归档文件本身是昨天的（= 截止当天），
            // 不算超期，所以这一次应该什么都不删。断言「没删」而不是「没报错」：
            // CleanedUnits 是 int，写 != null 恒真，等于没测。
            hub.Options.RetentionDays = 1;
            var cleaned = hub.RunMaintenance();
            Check("retention keeps same-day archive", cleaned.CleanedUnits == 0);
            Check("retention keeps yesterday archive", File.Exists(archived));

            hub.Dispose();

            // CSV 侧独立验证：文件确实是按天分开的
            string csvToday = Path.Combine(options.CsvDirectory,
                DateTime.Now.Date.ToString("yyyy-MM-dd") + ".csv");
            Check("csv file per day exists", File.Exists(csvToday));
            if (File.Exists(csvToday))
            {
                var lines = File.ReadAllLines(csvToday);
                Check("csv has header + 5 rows", lines.Length == 6);
            }
        }

        /// <summary>
        /// 仅 SQLite 模式下的「超期清理」。
        ///
        /// 单独立一节的原因：双模式下归档目录会被 CSV 后端顺手扫掉，SQLite 自己不清也看不出来。
        /// 只有仅 SQLite 模式下，「归档文件永远不删」才会暴露 —— 而工具集界面上就摆着这个模式。
        /// </summary>
        private static void TestSqliteOnlyRetention(string root)
        {
            Section("Storage (SQLite-only retention)");

            var options = new DataStoreOptions
            {
                RootDirectory = Path.Combine(root, "sqlite-only"),
                Mode = StoreMode.Sqlite,
                RetentionDays = 60,     // 先放宽，让归档文件活过一次维护
                BatchSize = 2,
                FlushIntervalMs = 60000
            };

            var hub = new DataStoreHub(options);
            var errors = new List<string>();
            hub.Error += m => errors.Add(m);
            hub.Initialize();

            var stale = DateTime.Now.Date.AddDays(-40).AddHours(10);   // 40 天前
            var today = DateTime.Now.Date.AddHours(9);

            for (int i = 0; i < 2; i++) hub.Append(MakeData(1, stale.AddMinutes(i), 300 + i));
            for (int i = 0; i < 2; i++) hub.Append(MakeData(1, today.AddMinutes(i), 400 + i));
            hub.Flush();

            // 保留 60 天：40 天前的那天该归档，但不该被清掉
            hub.RunMaintenance();
            Check("sqlite-only: no errors", errors.Count == 0);

            string archived = Path.Combine(options.ArchiveDirectory,
                stale.ToString("yyyy-MM-dd") + ".csv");
            Check("sqlite-only: stale day archived", File.Exists(archived));

            if (File.Exists(archived))
            {
                var head = File.ReadAllBytes(archived);
                Check("sqlite-only: archived csv has utf-8 BOM",
                    head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF);
            }

            // 保留期收窄到 30 天：那份归档文件超期，必须被删；今天的数据必须留下
            hub.Options.RetentionDays = 30;
            var report = hub.RunMaintenance();
            Check("sqlite-only: expired archive purged", !File.Exists(archived));
            Check("sqlite-only: purge was counted", report.CleanedUnits > 0);
            Check("sqlite-only: today data kept", hub.Count() == 2);

            hub.Dispose();
        }

        private static DeviceData MakeData(byte slaveId, DateTime time, double pressure)
        {
            return new DeviceData
            {
                SlaveId = slaveId,
                SlaveName = slaveId + "#空压机",
                Timestamp = time,
                PressureValue = pressure,
                Flow = pressure / 10.0,
                Time = 1234,
                Reg4Value = 0x0003,
                Endian = EndianMode.BigEndian,
                Bit8 = false,
                RawRegisters = new ushort[]
                {
                    (ushort)pressure, 0x0010, 0x04D2, 0x0000, 0x0003
                }
            };
        }

        // =====================================================================

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + name);
        }

        private static void Check(string label, bool ok)
        {
            if (ok) { _passed++; Console.WriteLine("  [PASS] " + label); }
            else { _failed++; Console.WriteLine("  [FAIL] " + label); }
        }
    }
}
