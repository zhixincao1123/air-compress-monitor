using System;

namespace AirCompressMonitor.Comm.Models
{
    /// <summary>
    /// 通信统计快照。UI 上「高并发采集下界面稳定」这句话要能被看见，
    /// 就得有真实指标可展示：成功率、当前延迟、重连次数。
    /// </summary>
    public class CommStatistics
    {
        /// <summary>累计发出的请求数。</summary>
        public long TotalRequests { get; set; }

        /// <summary>累计失败的请求数。</summary>
        public long FailedRequests { get; set; }

        /// <summary>最近一次请求耗时（毫秒）。</summary>
        public double LastLatencyMs { get; set; }

        /// <summary>链路重建次数（断线自动重连的计数）。</summary>
        public int ReconnectCount { get; set; }

        /// <summary>心跳成功/失败次数，用来判断链路是不是「连而不用」的假活。</summary>
        public long HeartbeatOk { get; set; }
        public long HeartbeatFail { get; set; }

        /// <summary>采样时刻。</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>成功率，无请求时返回 1。</summary>
        public double SuccessRate
        {
            get
            {
                if (TotalRequests <= 0) return 1.0;
                return (double)(TotalRequests - FailedRequests) / TotalRequests;
            }
        }
    }
}
