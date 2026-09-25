using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Storage
{
    /// <summary>
    /// CSV 后端。一天一个文件（yyyy-MM-dd.csv），表头只在建文件时写一次。
    ///
    /// 保持文件句柄常开而不是「一条一开」：采集是持续的，反复开关文件既慢又容易
    /// 被杀软锁住。跨天时自动切到新文件，旧的关掉。
    /// </summary>
    public class CsvDataStore : IDataStore
    {
        private readonly string _directory;
        private readonly object _sync = new object();

        private StreamWriter _writer;
        private DateTime _currentDay = DateTime.MinValue;
        private bool _disposed;

        public CsvDataStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("CSV 目录不能为空", "directory");

            _directory = directory;
        }

        public string Name
        {
            get { return "CSV"; }
        }

        /// <summary>CSV 落盘目录。刻意不叫 Directory —— 那会遮住 System.IO.Directory。</summary>
        public string DirectoryPath
        {
            get { return _directory; }
        }

        public void Initialize()
        {
            lock (_sync)
            {
                if (!Directory.Exists(_directory))
                    Directory.CreateDirectory(_directory);
            }
        }

        private string FileFor(DateTime day)
        {
            return Path.Combine(_directory,
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
        }

        public void Append(DeviceData data)
        {
            if (data == null) return;

            lock (_sync)
            {
                EnsureWriter(data.Timestamp.Date);

                _writer.Write(CsvFormat.FormatRow(data));
                _writer.Write("\r\n");
            }
        }

        private void EnsureWriter(DateTime day)
        {
            if (_writer != null && _currentDay == day) return;

            CloseWriter();

            if (!Directory.Exists(_directory))
                Directory.CreateDirectory(_directory);

            var file = FileFor(day);
            bool isNew = !File.Exists(file) || new FileInfo(file).Length == 0;

            // UTF8 带 BOM：Excel 双击打开中文表头才不会乱码
            _writer = new StreamWriter(file, true, new UTF8Encoding(true));
            if (isNew)
            {
                _writer.WriteLine(CsvFormat.Header);
                _writer.Flush();
            }

            _currentDay = day;
        }

        private void CloseWriter()
        {
            if (_writer == null) return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch
            {
                // 关闭阶段出错无处理价值，且不能让它打断采集
            }
            finally
            {
                _writer = null;
            }
        }

        public void Flush()
        {
            lock (_sync)
            {
                if (_writer != null)
                {
                    try { _writer.Flush(); } catch { }
                }
            }
        }

        // =====================================================================

        public IEnumerable<DeviceData> Query(DateTime from, DateTime to, byte slaveId)
        {
            var result = new List<DeviceData>();

            lock (_sync)
            {
                if (!Directory.Exists(_directory)) return result;

                Flush();

                for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                {
                    var file = FileFor(day);
                    if (!File.Exists(file)) continue;

                    foreach (var data in ReadFile(file, from, to, slaveId))
                        result.Add(data);
                }
            }

            result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return result;
        }

        private static IEnumerable<DeviceData> ReadFile(string file, DateTime from, DateTime to, byte slaveId)
        {
            var list = new List<DeviceData>();

            // 文件可能正被 Excel 打开，只读共享方式打开，避免直接抛异常
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                bool first = true;

                while ((line = reader.ReadLine()) != null)
                {
                    if (first)
                    {
                        first = false;
                        if (line.StartsWith("时间", StringComparison.Ordinal)) continue;
                    }
                    if (line.Length == 0) continue;

                    var d = ParseRow(line);
                    if (d == null) continue;

                    if (d.Timestamp < from || d.Timestamp > to) continue;
                    if (slaveId != 0 && d.SlaveId != slaveId) continue;

                    list.Add(d);
                }
            }

            return list;
        }

        private static DeviceData ParseRow(string line)
        {
            var f = CsvFormat.SplitLine(line);
            if (f.Count < 10) return null;

            var d = new DeviceData
            {
                Timestamp = CsvFormat.ParseTime(f[0]),
                SlaveName = f[2]
            };

            byte slaveId;
            if (byte.TryParse(f[1], out slaveId)) d.SlaveId = slaveId;

            double pressure, flow;
            if (double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out pressure))
                d.PressureValue = pressure;
            if (double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out flow))
                d.Flow = flow;

            int runTime;
            if (int.TryParse(f[5], out runTime)) d.Time = runTime;

            d.Bit8 = f[6] == "1";

            ushort ctrl;
            if (ushort.TryParse(f[7], out ctrl)) d.Reg4Value = ctrl;

            int endian;
            if (int.TryParse(f[8], out endian)) d.Endian = (EndianMode)endian;

            d.RawRegisters = ParseRegisters(f[9]);
            return d;
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
                if (!Directory.Exists(_directory)) return 0;

                foreach (var file in Directory.GetFiles(_directory, "*.csv"))
                {
                    total += CountLines(file) - 1;   // 扣掉表头
                }
            }

            return total < 0 ? 0 : total;
        }

        private static long CountLines(string file)
        {
            long n = 0;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                while (reader.ReadLine() != null) n++;
            }
            return n;
        }

        // =====================================================================

        /// <summary>
        /// CSV 后端本身就是按天一个文件，没有「热表」概念，
        /// 所以归档动作退化成「把老文件挪进 archive 目录」——不删数据，只是让主目录保持整洁。
        /// </summary>
        public string Archive(DateTime before)
        {
            lock (_sync)
            {
                if (!Directory.Exists(_directory)) return null;

                Flush();

                var archiveDir = Path.Combine(
                    Path.GetDirectoryName(_directory.TrimEnd(Path.DirectorySeparatorChar)) ?? ".",
                    "archive");

                bool moved = false;

                foreach (var file in Directory.GetFiles(_directory, "*.csv"))
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file),
                            "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
                        continue;

                    if (day.Date >= before.Date) continue;

                    // 当天文件可能正被写入，不能挪走
                    if (day.Date == _currentDay) continue;

                    if (!Directory.Exists(archiveDir))
                        Directory.CreateDirectory(archiveDir);

                    var target = Path.Combine(archiveDir, Path.GetFileName(file));
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(file, target);
                        moved = true;
                    }
                    catch
                    {
                        // 文件被占用时跳过，下次维护再试
                    }
                }

                return moved ? archiveDir : null;
            }
        }

        public int Cleanup(TimeSpan retention)
        {
            var cutoff = DateTime.Now.Date - retention;
            int removed = 0;

            lock (_sync)
            {
                // 主目录和归档目录都要清，否则归档目录会无限膨胀
                foreach (var dir in new[]
                {
                    _directory,
                    Path.Combine(Path.GetDirectoryName(_directory.TrimEnd(Path.DirectorySeparatorChar)) ?? ".", "archive")
                })
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var file in Directory.GetFiles(dir, "*.csv"))
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
                            // 被占用则跳过
                        }
                    }
                }
            }

            return removed;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CloseWriter();
            }
        }
    }
}
