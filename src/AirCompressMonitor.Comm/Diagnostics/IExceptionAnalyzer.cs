using System;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Comm.Diagnostics
{
    /// <summary>
    /// 故障诊断契约。把「一个异常 / 一个报警位」翻译成「原因 + 处置建议」。
    ///
    /// 做成接口而不是直接写死，是因为诊断策略本身会演进：
    /// 现在内置的是规则库版本（离线、可解释、无外部依赖），
    /// 将来要接专家系统或大模型，换一个实现注入即可，采集层不用动。
    /// </summary>
    public interface IExceptionAnalyzer
    {
        /// <summary>诊断一个异常。</summary>
        /// <param name="ex">捕获到的异常。</param>
        /// <param name="context">发生位置描述，如 "从站 1 采集"。</param>
        FaultDiagnosis Analyze(Exception ex, string context);

        /// <summary>诊断一个状态位报警。</summary>
        FaultDiagnosis AnalyzeAlarm(AlarmInfo alarm);
    }
}
