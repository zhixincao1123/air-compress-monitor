using System;
using System.Collections.Generic;
using System.Threading;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Storage
{
    /// <summary>一次维护动作的结果，用于界面提示与日志。</summary>
    public class MaintenanceReport
    {
        public DateTime Time { get; set; }
        public string ArchivePath { get; set; }
        public int CleanedUnits { get; set; }
        public bool DidAnything { get; set; }
        public string Message { get; set; }

        public MaintenanceReport()
        {
            Time = DateTime.Now;
        }

        public override string ToString()
        {
            return Message ?? (DidAnything ? "维护完成" : "无需维护");
        }
    }

    /// <summary>
    /// 存储门面。上层只跟它打交道，由它决定这一条落到 SQLite、CSV，还是两边都落。
    ///
    /// 两件事在这里做，都不适合放到后端里：
    ///   1. 攒批 —— 采集是高频小包，逐条落盘会被 I/O 拖死。按条数或时间两个条件触发落盘，
    ///      低频时不会让数据一直挂在内存里，高频时不会把磁盘打满。
    ///   2. 维护 —— 跨天归档 + 超期清理，按固定节拍在后台跑，不占用采集线程。
    /// </summary>
    public class DataStoreHub : IDisposable
    {
        private readonly DataStoreOptions _options;
        private readonly List<IDataStore> _stores = new List<IDataStore>();
        private readonly List<DeviceData> _buffer = new List<DeviceData>();
        private readonly object _sync = new object();

        private Timer _flushTimer;
        private Timer _maintenanceTimer;
        private bool _disposed;

        private IDataStore _sqlite;
        private IDataStore _csv;

        /// <summary>维护间隔，默认 1 小时。归档与清理都不需要更勤。</summary>
        public int MaintenanceIntervalMs { get; set; }

        public event Action<string> Error;

        public event Action<MaintenanceReport> MaintenanceCompleted;

        /// <summary>落盘计数，界面用来看「到底写进去没有」。</summary>
        public long TotalWritten { get; private set; }

        public DataStoreOptions Options
        {
            get { return _options; }
        }

        public StoreMode Mode
        {
            get { return _options.Mode; }
        }

        /// <summary>当前缓冲里还没落盘的条数。</summary>
        public int BufferedCount
        {
            get { lock (_sync) { return _buffer.Count; } }
        }

        public DataStoreHub(DataStoreOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");

            _options = options;
            _options.Normalize();
            MaintenanceIntervalMs = 60 * 60 * 1000;
        }

        // =====================================================================

        public void Initialize()
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException("DataStoreHub");
                if (_stores.Count > 0) return;

                if (_options.Mode == StoreMode.Sqlite || _options.Mode == StoreMode.Both)
                {
                    _sqlite = new SqliteDataStore(_options.DatabasePath);
                    _sqlite.Initialize();
                    _stores.Add(_sqlite);
                }

                if (_options.Mode == StoreMode.Csv || _options.Mode == StoreMode.Both)
                {
                    _csv = new CsvDataStore(_options.CsvDirectory);
                    _csv.Initialize();
                    _stores.Add(_csv);
                }
            }

            _flushTimer = new Timer(OnFlushTimer, null, _options.FlushIntervalMs, _options.FlushIntervalMs);
            _maintenanceTimer = new Timer(OnMaintenanceTimer, null, MaintenanceIntervalMs, MaintenanceIntervalMs);
        }

        // =====================================================================

        public void Append(DeviceData data)
        {
            if (data == null || _disposed) return;

            bool needFlush = false;

            lock (_sync)
            {
                _buffer.Add(data);
                if (_buffer.Count >= _options.BatchSize)
                    needFlush = true;
            }

            if (needFlush) Flush();
        }

        /// <summary>把缓冲写进所有启用的后端。</summary>
        public void Flush()
        {
            List<DeviceData> batch;
            List<IDataStore> stores;

            lock (_sync)
            {
                if (_buffer.Count == 0) return;

                batch = new List<DeviceData>(_buffer);
                _buffer.Clear();
                stores = new List<IDataStore>(_stores);
            }

            foreach (var store in stores)
            {
                try
                {
                    foreach (var d in batch)
                        store.Append(d);

                    store.Flush();
                }
                catch (Exception ex)
                {
                    RaiseError(string.Format("{0} 落盘失败：{1}", store.Name, ex.Message));
                    // 单个后端失败不影响另一个：CSV 挂了不该把 SQLite 也带停
                }
            }

            lock (_sync)
            {
                TotalWritten += batch.Count;
            }
        }

        private void OnFlushTimer(object state)
        {
            try
            {
                Flush();
            }
            catch (Exception ex)
            {
                RaiseError(string.Format("定时落盘异常：{0}", ex.Message));
            }
        }

        // =====================================================================
        // 维护：归档 + 超期清理
        // =====================================================================

        private void OnMaintenanceTimer(object state)
        {
            try
            {
                RunMaintenance();
            }
            catch (Exception ex)
            {
                RaiseError(string.Format("存储维护异常：{0}", ex.Message));
            }
        }

        /// <summary>
        /// 归档 + 清理。清理阈值来自 <see cref="DataStoreOptions.RetentionDays"/>（默认 30 天）。
        /// 顺序固定：先归档再清理，保证被清掉的数据一定已经在归档里。
        /// </summary>
        public MaintenanceReport RunMaintenance()
        {
            var report = new MaintenanceReport();
            List<IDataStore> stores;

            lock (_sync)
            {
                stores = new List<IDataStore>(_stores);
            }

            if (stores.Count == 0) return report;

            // 先把内存里的写下去，否则归档时最新数据还不在文件里
            Flush();

            var archiveBefore = DateTime.Now.Date;    // 归档「今天之前」的完整天
            var retention = TimeSpan.FromDays(_options.RetentionDays);

            foreach (var store in stores)
            {
                try
                {
                    var path = store.Archive(archiveBefore);
                    if (!string.IsNullOrEmpty(path))
                    {
                        report.ArchivePath = path;
                        report.DidAnything = true;
                    }

                    int cleaned = store.Cleanup(retention);
                    if (cleaned > 0)
                    {
                        report.CleanedUnits += cleaned;
                        report.DidAnything = true;
                    }
                }
                catch (Exception ex)
                {
                    RaiseError(string.Format("{0} 维护失败：{1}", store.Name, ex.Message));
                }
            }

            report.Message = report.DidAnything
                ? string.Format("已归档至 {0}，清理 {1} 个过期单元，保留 {2} 天",
                    report.ArchivePath ?? "(无)", report.CleanedUnits, _options.RetentionDays)
                : string.Format("无需维护，保留 {0} 天", _options.RetentionDays);

            var h = MaintenanceCompleted;
            if (h != null)
            {
                try { h(report); } catch { }
            }

            return report;
        }

        // =====================================================================

        /// <summary>
        /// 查询历史。双模式时只从 SQLite 读 —— 两边数据相同，合并会出重复行。
        /// SQLite 不可用时自动回退到 CSV。
        /// </summary>
        public IEnumerable<DeviceData> Query(DateTime from, DateTime to, byte slaveId)
        {
            lock (_sync)
            {
                if (_sqlite != null) return _sqlite.Query(from, to, slaveId);
                if (_csv != null) return _csv.Query(from, to, slaveId);
            }

            return new List<DeviceData>();
        }

        /// <summary>当前已落盘的记录数（不含缓冲）。</summary>
        public long Count()
        {
            long total = 0;

            lock (_sync)
            {
                // 双模式时两边条数相同，取其一即可
                var store = _sqlite ?? _csv;
                if (store != null) total = store.Count();
            }

            return total;
        }

        /// <summary>给界面用的一句话描述。</summary>
        public string Describe()
        {
            lock (_sync)
            {
                var names = new List<string>();
                foreach (var s in _stores) names.Add(s.Name);

                return string.Format("{0}｜{1}｜缓冲 {2} 条｜已落盘 {3} 条",
                    _options.Mode,
                    names.Count == 0 ? "未初始化" : string.Join("+", names.ToArray()),
                    _buffer.Count,
                    TotalWritten);
            }
        }

        private void RaiseError(string message)
        {
            var h = Error;
            if (h == null) return;
            try { h(message); } catch { }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (_flushTimer != null)
            {
                try { _flushTimer.Dispose(); } catch { }
                _flushTimer = null;
            }

            if (_maintenanceTimer != null)
            {
                try { _maintenanceTimer.Dispose(); } catch { }
                _maintenanceTimer = null;
            }

            try { Flush(); } catch { }

            lock (_sync)
            {
                foreach (var store in _stores)
                {
                    try { store.Dispose(); } catch { }
                }
                _stores.Clear();
            }
        }
    }
}
