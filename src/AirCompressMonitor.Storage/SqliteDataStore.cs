using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Threading;
using AirCompressMonitor.Comm.Models;
using Microsoft.Data.Sqlite;

namespace AirCompressMonitor.Storage
{
    /// <summary>
    /// SQLite 后端。
    ///
    /// 三个关键设计：
    ///   1. 按天分表 —— 表名 data_yyyyMMdd。清理时整表 DROP，不用 DELETE 扫全表，
    ///      数据量涨到千万行也不会越跑越慢。
    ///   2. 批量插入 —— 一批写在一个事务里。SQLite 每条一个事务时磁盘 fsync 是瓶颈，
    ///      攒批后吞吐差一个数量级。
    ///   3. WAL 日志 —— 读写不互相阻塞，界面查询历史时采集不会被卡住。
    /// </summary>
    public class SqliteDataStore : IDataStore
    {
        private const string TablePrefix = "data_";

        private readonly string _dbPath;
        private readonly object _sync = new object();

        private SqliteConnection _conn;
        private SqliteTransaction _tx;
        private SqliteCommand _insertCmd;
        private string _txTable;
        private int _pendingInTx;
        private bool _disposed;

        /// <summary>一个事务里最多攒多少条。太小则 fsync 频繁，太大则掉电丢得多。</summary>
        public int RowsPerTransaction { get; set; }

        public SqliteDataStore(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("数据库路径不能为空", "dbPath");

            _dbPath = dbPath;
            RowsPerTransaction = 200;
        }

        public string Name
        {
            get { return "SQLite"; }
        }

        public string DatabasePath
        {
            get { return _dbPath; }
        }

        // =====================================================================

        public void Initialize()
        {
            lock (_sync)
            {
                if (_conn != null) return;

                var dir = Path.GetDirectoryName(_dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                EnsureNativeProvider();

                _conn = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Cache = SqliteCacheMode.Shared
                }.ToString());

                _conn.Open();

                // WAL 让「边写边查」不打架；synchronous=Normal 在 WAL 下已足够安全，
                // 又不会每条都强制落盘，采集吞吐能上来。
                Execute("PRAGMA journal_mode=WAL;");
                Execute("PRAGMA synchronous=Normal;");
                Execute("PRAGMA busy_timeout=5000;");

                EnsureTable(DateTime.Now);
            }
        }

        private static int _providerReady;

        /// <summary>
        /// 初始化 SQLite 原生提供程序。用的是 Microsoft.Data.Sqlite.Core（不含原生库），
        /// 必须显式挂上 e_sqlite3 的实现，否则第一次 Open() 会报「找不到本机库」。
        /// 放在这里而不是让每个宿主自己记得调 —— 少一个必踩的坑。
        ///
        /// 刻意不走 SQLitePCLRaw 的 Batteries_V2.Init()：它在 .NET Framework 上会选
        /// dynamic_cdecl 分支，而那个 provider 包并不在我们引用的集合里，运行期才炸。
        /// 直接指定 e_sqlite3 这一个 provider，依赖最少、行为最确定。
        /// </summary>
        private static void EnsureNativeProvider()
        {
            if (Interlocked.CompareExchange(ref _providerReady, 1, 0) != 0) return;

            try
            {
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
                SQLitePCL.raw.FreezeProvider(true);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _providerReady, 0);
                throw new InvalidOperationException(
                    "SQLite 原生库初始化失败，请确认 e_sqlite3.dll 已随程序一起部署：" + ex.Message, ex);
            }
        }

        private static string TableNameFor(DateTime day)
        {
            return TablePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        private static DateTime? DayFromTable(string table)
        {
            if (string.IsNullOrEmpty(table) || !table.StartsWith(TablePrefix, StringComparison.Ordinal))
                return null;

            var part = table.Substring(TablePrefix.Length);
            DateTime day;
            if (DateTime.TryParseExact(part, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day))
                return day;

            return null;
        }

        private void EnsureTable(DateTime day)
        {
            var table = TableNameFor(day);

            // 表名由日期拼出、不接受外部输入，不存在注入面；列定义固定。
            Execute(string.Format(CultureInfo.InvariantCulture,
                @"CREATE TABLE IF NOT EXISTS [{0}] (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    Ts         TEXT    NOT NULL,
                    SlaveId    INTEGER NOT NULL,
                    SlaveName  TEXT,
                    Pressure   REAL,
                    Flow       REAL,
                    RunTime    INTEGER,
                    StatusWord INTEGER,
                    CtrlWord   INTEGER,
                    Endian     INTEGER,
                    RawRegs    TEXT
                );", table));

            Execute(string.Format(CultureInfo.InvariantCulture,
                "CREATE INDEX IF NOT EXISTS [{0}_ts] ON [{0}](Ts);", table));
        }

        private void Execute(string sql)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        // =====================================================================

        public void Append(DeviceData data)
        {
            if (data == null) return;

            lock (_sync)
            {
                EnsureOpen();

                var table = TableNameFor(data.Timestamp.Date);

                // 跨天时先把上一天的事务结掉、把新表建出来，再继续写
                if (_tx == null || _txTable != table)
                {
                    CommitTransaction();
                    EnsureTable(data.Timestamp.Date);
                    BeginTransaction(table);
                }

                _insertCmd.Parameters["$ts"].Value = CsvFormat.FormatTime(data.Timestamp);
                _insertCmd.Parameters["$slaveId"].Value = (long)data.SlaveId;
                _insertCmd.Parameters["$slaveName"].Value = (object)data.SlaveName ?? DBNull.Value;
                _insertCmd.Parameters["$pressure"].Value = data.PressureValue;
                _insertCmd.Parameters["$flow"].Value = data.Flow;
                _insertCmd.Parameters["$runTime"].Value = (long)data.Time;
                _insertCmd.Parameters["$statusWord"].Value = (long)(data.Bit8 ? 1 : 0);
                _insertCmd.Parameters["$ctrlWord"].Value = (long)data.Reg4Value;
                _insertCmd.Parameters["$endian"].Value = (long)(int)data.Endian;
                _insertCmd.Parameters["$rawRegs"].Value =
                    (object)CsvFormat.FormatRegisters(data.RawRegisters) ?? DBNull.Value;

                _insertCmd.ExecuteNonQuery();
                _pendingInTx++;

                if (_pendingInTx >= RowsPerTransaction)
                    CommitTransaction();
            }
        }

        private void BeginTransaction(string table)
        {
            _tx = _conn.BeginTransaction();
            _txTable = table;
            _pendingInTx = 0;

            _insertCmd = _conn.CreateCommand();
            _insertCmd.Transaction = _tx;
            _insertCmd.CommandText = string.Format(CultureInfo.InvariantCulture,
                @"INSERT INTO [{0}]
                    (Ts, SlaveId, SlaveName, Pressure, Flow, RunTime, StatusWord, CtrlWord, Endian, RawRegs)
                  VALUES ($ts, $slaveId, $slaveName, $pressure, $flow, $runTime, $statusWord, $ctrlWord, $endian, $rawRegs);",
                table);

            foreach (var name in new[]
            {
                "$ts", "$slaveId", "$slaveName", "$pressure", "$flow",
                "$runTime", "$statusWord", "$ctrlWord", "$endian", "$rawRegs"
            })
            {
                _insertCmd.Parameters.Add(new SqliteParameter(name, DBNull.Value));
            }
        }

        private void CommitTransaction()
        {
            if (_tx == null) return;

            try
            {
                _tx.Commit();
            }
            catch
            {
                try { _tx.Rollback(); } catch { }
                throw;
            }
            finally
            {
                if (_insertCmd != null)
                {
                    _insertCmd.Dispose();
                    _insertCmd = null;
                }
                _tx.Dispose();
                _tx = null;
                _txTable = null;
                _pendingInTx = 0;
            }
        }

        public void Flush()
        {
            lock (_sync)
            {
                CommitTransaction();
            }
        }

        // =====================================================================

        public IEnumerable<DeviceData> Query(DateTime from, DateTime to, byte slaveId)
        {
            var result = new List<DeviceData>();

            lock (_sync)
            {
                EnsureOpen();

                foreach (var day in EnumerateDays(from, to))
                {
                    var table = TableNameFor(day);
                    if (!TableExists(table)) continue;

                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = string.Format(CultureInfo.InvariantCulture,
                            @"SELECT Ts, SlaveId, SlaveName, Pressure, Flow, RunTime, StatusWord, CtrlWord, Endian, RawRegs
                              FROM [{0}]
                              WHERE Ts >= $from AND Ts <= $to {1}
                              ORDER BY Ts;",
                            table,
                            slaveId == 0 ? string.Empty : "AND SlaveId = $slaveId");

                        cmd.Parameters.AddWithValue("$from", CsvFormat.FormatTime(from));
                        cmd.Parameters.AddWithValue("$to", CsvFormat.FormatTime(to));
                        if (slaveId != 0)
                            cmd.Parameters.AddWithValue("$slaveId", (long)slaveId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                result.Add(ReadRow(reader));
                        }
                    }
                }
            }

            return result;
        }

        private static DeviceData ReadRow(SqliteDataReader reader)
        {
            var data = new DeviceData
            {
                Timestamp = CsvFormat.ParseTime(reader.GetString(0)),
                SlaveId = (byte)reader.GetInt64(1),
                SlaveName = reader.IsDBNull(2) ? null : reader.GetString(2),
                PressureValue = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
                Flow = reader.IsDBNull(4) ? 0 : reader.GetDouble(4),
                Time = reader.IsDBNull(5) ? 0 : (int)reader.GetInt64(5),
                Bit8 = !reader.IsDBNull(6) && reader.GetInt64(6) != 0,
                Reg4Value = reader.IsDBNull(7) ? (ushort)0 : (ushort)reader.GetInt64(7),
                Endian = reader.IsDBNull(8) ? EndianMode.Auto : (EndianMode)(int)reader.GetInt64(8),
                RawRegisters = ParseRegisters(reader.IsDBNull(9) ? null : reader.GetString(9))
            };
            return data;
        }

        private static ushort[] ParseRegisters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new ushort[0];

            var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var regs = new ushort[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                ushort v;
                ushort.TryParse(parts[i].Replace("0x", ""),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
                regs[i] = v;
            }
            return regs;
        }

        public long Count()
        {
            long total = 0;

            lock (_sync)
            {
                EnsureOpen();

                foreach (var table in ListTables())
                {
                    using (var cmd = _conn.CreateCommand())
                    {
                        cmd.CommandText = string.Format(CultureInfo.InvariantCulture,
                            "SELECT COUNT(*) FROM [{0}];", table);
                        total += Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                    }
                }
            }

            return total;
        }

        // =====================================================================

        /// <summary>归档目录。与 CSV 后端指向同一个位置（都在数据根目录下的 archive）。</summary>
        private string ArchiveDir
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(_dbPath) ?? ".", "archive");
            }
        }

        /// <summary>
        /// 把 <paramref name="before"/> 之前的按天表导出成 CSV 后整表删除。
        /// 「先导出再删」的顺序不能反：归档的意义就是删了还能查。
        /// </summary>
        public string Archive(DateTime before)
        {
            string archiveDir = ArchiveDir;

            lock (_sync)
            {
                EnsureOpen();

                var tables = new List<string>();
                foreach (var table in ListTables())
                {
                    var day = DayFromTable(table);
                    if (day.HasValue && day.Value.Date < before.Date)
                        tables.Add(table);
                }

                if (tables.Count == 0) return null;

                if (!Directory.Exists(archiveDir))
                    Directory.CreateDirectory(archiveDir);

                foreach (var table in tables)
                {
                    var day = DayFromTable(table).Value;
                    var file = Path.Combine(archiveDir,
                        day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");

                    ExportTableToCsv(table, file);
                    Execute(string.Format(CultureInfo.InvariantCulture, "DROP TABLE [{0}];", table));
                }
            }

            return archiveDir;
        }

        private void ExportTableToCsv(string table, string file)
        {
            // 带 BOM：归档出来的 CSV 是给人看的，多半直接用 Excel 打开，
            // 不带 BOM 中文会显示成乱码。与 CsvDataStore 的写法保持一致。
            using (var writer = new StreamWriter(file, false, new System.Text.UTF8Encoding(true)))
            {
                writer.WriteLine(CsvFormat.Header);

                using (var cmd = _conn.CreateCommand())
                {
                    cmd.CommandText = string.Format(CultureInfo.InvariantCulture,
                        @"SELECT Ts, SlaveId, SlaveName, Pressure, Flow, RunTime, StatusWord, CtrlWord, Endian, RawRegs
                          FROM [{0}] ORDER BY Ts;", table);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var d = ReadRow(reader);
                            writer.WriteLine(CsvFormat.FormatRow(d));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 删除超过保留期的按天表。整表 DROP，代价与数据量无关。
        ///
        /// 归档目录也要清：Archive 会把老表导出成 archive/yyyy-MM-dd.csv 再 DROP 表，
        /// 那些文件不在任何表里，只清表的话归档目录会无限膨胀 ——
        /// 而且「仅 SQLite」模式下没有 CSV 后端帮忙扫这个目录，
        /// 保留期就形同虚设。双模式时 CSV 后端也会扫同一目录，重复删不存在的文件无害。
        /// </summary>
        public int Cleanup(TimeSpan retention)
        {
            var cutoff = DateTime.Now.Date - retention;

            lock (_sync)
            {
                EnsureOpen();

                int dropped = 0;
                foreach (var table in ListTables())
                {
                    var day = DayFromTable(table);
                    if (!day.HasValue || day.Value.Date >= cutoff) continue;

                    Execute(string.Format(CultureInfo.InvariantCulture, "DROP TABLE [{0}];", table));
                    dropped++;
                }

                dropped += PurgeArchive(cutoff);
                return dropped;
            }
        }

        /// <summary>删掉归档目录里超过保留期的导出文件。返回删除个数。</summary>
        private int PurgeArchive(DateTime cutoff)
        {
            string archiveDir = ArchiveDir;
            if (!Directory.Exists(archiveDir)) return 0;

            int removed = 0;

            foreach (var file in Directory.GetFiles(archiveDir, "*.csv"))
            {
                DateTime day;
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file),
                        "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                    continue;

                if (day.Date >= cutoff) continue;

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    // 文件被占用（比如正被人用 Excel 打开）则跳过，下次维护再试
                }
            }

            return removed;
        }

        private List<string> ListTables()
        {
            var tables = new List<string>();

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'data_%' ORDER BY name;";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        tables.Add(reader.GetString(0));
                }
            }

            return tables;
        }

        private bool TableExists(string table)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = $name;";
                cmd.Parameters.AddWithValue("$name", table);
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        /// <summary>枚举起止之间涉及的每一天，用于跨天查询。</summary>
        private static IEnumerable<DateTime> EnumerateDays(DateTime from, DateTime to)
        {
            for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                yield return day;
        }

        private void EnsureOpen()
        {
            if (_conn == null)
                throw new InvalidOperationException("SqliteDataStore 尚未 Initialize()");
            if (_disposed)
                throw new ObjectDisposedException("SqliteDataStore");
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;

                try { CommitTransaction(); } catch { }
                try { if (_conn != null) _conn.Dispose(); } catch { }
                _conn = null;
            }
        }
    }
}
