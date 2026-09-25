using System;

namespace AirCompressMonitor.Comm.Models
{
    /// <summary>报警/恢复事件。</summary>
    public class AlarmInfo
    {
        public DateTime Time { get; set; } = DateTime.Now;

        /// <summary>来源从站地址；0 表示链路级报警（如断线），不对应具体设备。</summary>
        public byte SlaveId { get; set; }

        public string SlaveName { get; set; }

        /// <summary>"报警" / "恢复"。</summary>
        public string State { get; set; }

        public string Message { get; set; }

        /// <summary>严重级别，供 UI 着色和后续分级推送。</summary>
        public AlarmLevel Level { get; set; } = AlarmLevel.Warning;
    }

    public enum AlarmLevel
    {
        Info = 0,
        Warning = 1,
        Fault = 2
    }
}
