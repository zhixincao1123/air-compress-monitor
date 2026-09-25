using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Storage
{
    /// <summary>
    /// 采集数据的 CSV 编解码。CSV 落盘和归档导出共用同一套列定义，
    /// 保证「实时写出来的文件」和「归档导出的文件」能被同一个脚本读。
    /// </summary>
    public static class CsvFormat
    {
        public const string Header =
            "时间,从站号,从站名,压力,流量,运行时间,状态字,控制字,字节序,原始寄存器";

        /// <summary>时间列格式。带毫秒且无时区歧义，Excel 与脚本都好认。</summary>
        public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        public static string FormatTime(DateTime time)
        {
            return time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseTime(string text)
        {
            DateTime t;
            if (DateTime.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out t))
                return t;

            // 容忍手工编辑过的文件（Excel 常把毫秒抹掉）
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return t;

            return DateTime.MinValue;
        }

        public static string FormatRow(DeviceData d)
        {
            var sb = new StringBuilder(128);
            sb.Append(FormatTime(d.Timestamp)).Append(',');
            sb.Append(d.SlaveId).Append(',');
            sb.Append(Escape(d.SlaveName)).Append(',');
            sb.Append(d.PressureValue.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(d.Flow.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(d.Time).Append(',');
            sb.Append(d.Bit8 ? 1 : 0).Append(',');
            sb.Append(d.Reg4Value).Append(',');
            sb.Append((int)d.Endian).Append(',');
            sb.Append(Escape(FormatRegisters(d.RawRegisters)));
            return sb.ToString();
        }

        public static string FormatRegisters(ushort[] regs)
        {
            if (regs == null || regs.Length == 0) return string.Empty;

            var sb = new StringBuilder(regs.Length * 7);
            for (int i = 0; i < regs.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append("0x").Append(regs[i].ToString("X4", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>按 CSV 规则转义：含逗号、引号、换行时整体加引号，内部引号翻倍。</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            bool needQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!needQuote) return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>把一行拆成字段，处理引号包裹与转义。</summary>
        public static List<string> SplitLine(string line)
        {
            var fields = new List<string>();
            if (line == null) return fields;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"') inQuotes = true;
                    else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                    else sb.Append(c);
                }
            }

            fields.Add(sb.ToString());
            return fields;
        }
    }
}
