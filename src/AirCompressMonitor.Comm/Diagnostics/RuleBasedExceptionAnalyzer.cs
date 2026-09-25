using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Modbus;
using AirCompressMonitor.Comm.Models;

namespace AirCompressMonitor.Comm.Diagnostics
{
    /// <summary>
    /// 规则库版故障诊断。
    ///
    /// 设计取舍：不调任何外部服务。诊断知识以「规则表」形式内置，
    /// 好处是离线可用、结论可解释、可评审 —— 现场断网时照样能给出建议，
    /// 而且每一条结论都能追到是哪条规则判出来的。
    /// </summary>
    public class RuleBasedExceptionAnalyzer : IExceptionAnalyzer
    {
        /// <summary>一条异常诊断规则。</summary>
        private class Rule
        {
            public Func<Exception, bool> Match;
            public FaultCategory Category;
            public string Symptom;
            public string Cause;
            public string Suggestion;
            public AlarmLevel Level;
        }

        private readonly List<Rule> _rules = new List<Rule>();

        /// <summary>Modbus 异常码 → 诊断。key 为从站返回的异常码。</summary>
        private static readonly Dictionary<byte, string[]> ModbusCodes = new Dictionary<byte, string[]>
        {
            { 0x01, new[] { "非法功能码", "从站不支持该功能码", "核对设备手册确认支持的寄存器区（保持/输入寄存器），改配置里的寄存器类型" } },
            { 0x02, new[] { "非法数据地址", "请求的寄存器地址超出从站实际范围", "核对设备点表，修正从站的起始地址；注意部分品牌地址从 0 还是从 1 起算" } },
            { 0x03, new[] { "非法数据值", "写入的数值超出从站允许范围", "检查下发的控制字取值；先读回当前值再按位改写，不要整体覆盖" } },
            { 0x04, new[] { "从站设备故障", "从站自身检测到硬件或内部故障", "查看该设备本机故障码与指示灯；必要时断电重启，仍报错则联系厂家检修" } },
            { 0x05, new[] { "从站已确认", "从站已受理但需要较长时间完成", "降低该从站的轮询频率，等待执行完成后再读状态" } },
            { 0x06, new[] { "从站设备忙", "从站正在处理上一条请求", "适当增大轮询间隔；多从站共用一条串口时避免高频并发" } },
            { 0x08, new[] { "存储奇偶性差错", "从站存储区校验失败", "属设备侧问题，需厂家处理；可先尝试重新下发一次参数" } },
            { 0x0A, new[] { "网关路径不可用", "网关未找到目标设备", "检查网关到目标从站的下一段链路（串口接线 / 站号是否重复）" } },
            { 0x0B, new[] { "网关目标设备响应失败", "网关转发后目标从站无响应", "目标从站掉线或站号冲突；逐一排查同链路上的设备站号" } },
        };

        public RuleBasedExceptionAnalyzer()
        {
            BuildRules();
        }

        public FaultDiagnosis Analyze(Exception ex, string context)
        {
            var diag = new FaultDiagnosis
            {
                Category = FaultCategory.Unknown,
                Symptom = string.IsNullOrEmpty(context) ? "未知故障" : context,
                Cause = "未能匹配到已知规则",
                Suggestion = "记录完整异常信息后人工排查",
                RawError = Describe(ex),
                Level = AlarmLevel.Warning
            };

            if (ex == null) return diag;

            var rule = Match(ex);
            if (rule != null)
            {
                diag.Category = rule.Category;
                diag.Symptom = string.IsNullOrEmpty(context) ? rule.Symptom : rule.Symptom;
                diag.Cause = rule.Cause;
                diag.Suggestion = rule.Suggestion;
                diag.Level = rule.Level;
            }

            // Modbus 从站异常码能给出比「规则命中」更精确的结论，优先覆盖
            var slaveEx = FindInner<SlaveException>(ex);
            if (slaveEx != null)
            {
                string[] info;
                if (ModbusCodes.TryGetValue((byte)slaveEx.SlaveExceptionCode, out info))
                {
                    diag.Category = FaultCategory.Device;
                    diag.Symptom = string.Format("从站返回异常码 0x{0:X2}（{1}）",
                        (byte)slaveEx.SlaveExceptionCode, info[0]);
                    diag.Cause = info[1];
                    diag.Suggestion = info[2];
                    diag.Level = AlarmLevel.Fault;
                }
            }

            return diag;
        }

        public FaultDiagnosis AnalyzeAlarm(AlarmInfo alarm)
        {
            if (alarm == null) return null;

            var diag = new FaultDiagnosis
            {
                Time = alarm.Time,
                SlaveId = alarm.SlaveId,
                SlaveName = alarm.SlaveName,
                Category = FaultCategory.Device,
                Symptom = alarm.Message,
                Level = alarm.Level
            };

            if (alarm.State == "恢复")
            {
                diag.Category = FaultCategory.Device;
                diag.Cause = "该状态位已复位";
                diag.Suggestion = "确认现场处置生效，本条目可归档";
                diag.Level = AlarmLevel.Info;
                return diag;
            }

            foreach (var pair in BitRules)
            {
                if (alarm.Message != null && alarm.Message.Contains(pair.Key))
                {
                    diag.Cause = pair.Value[0];
                    diag.Suggestion = pair.Value[1];
                    return diag;
                }
            }

            diag.Cause = "设备状态位置位，具体原因需结合现场判断";
            diag.Suggestion = "查看设备本机显示与历史趋势，必要时停机检查";
            return diag;
        }

        // =====================================================================
        // 规则表
        // =====================================================================

        private void BuildRules()
        {
            // ---- 通信层 ----
            Add<TimeoutException>(FaultCategory.Communication,
                "通信超时",
                "从站在规定时间内没有回应。常见于波特率/校验位不匹配、站号填错、RS485 接线极性反接、或线路过长干扰大",
                "先用同参数扫描站号确认设备在线；核对串口参数与设备菜单一致；检查 A/B 线是否接反、终端电阻是否到位",
                AlarmLevel.Fault);

            Add<SocketException>(FaultCategory.Communication,
                "网络连接异常",
                "TCP 链路无法建立或已断开：对端未开机、IP/端口不对、防火墙拦截、或交换机链路中断",
                "在命令行 ping 设备地址确认可达；确认设备 Modbus TCP 端口（默认 502）已开放；检查防火墙策略",
                AlarmLevel.Fault);

            Add<IOException>(FaultCategory.Communication,
                "串口 I/O 异常",
                "串口被拔出、被其他程序占用，或 USB 转串口驱动异常",
                "确认串口线未被拔出；关闭其他占用该串口的软件（调试助手、组态软件）；必要时重新插拔并检查设备管理器端口号是否变化",
                AlarmLevel.Fault);

            Add<UnauthorizedAccessException>(FaultCategory.Communication,
                "串口被拒绝访问",
                "该串口已被另一个进程独占打开",
                "关闭占用该串口的程序；同一串口不要同时开两个采集实例",
                AlarmLevel.Fault);

            Add<ObjectDisposedException>(FaultCategory.Communication,
                "链路已被释放",
                "重连过程中旧主站被释放后仍被使用",
                "属内部时序问题，重启采集即可；若频繁出现请记录复现步骤",
                AlarmLevel.Warning);

            // ---- 配置层 ----
            Add<InvalidOperationException>(FaultCategory.Configuration,
                "操作前置条件不满足",
                "链路未打开就发起请求，或返回的寄存器数量少于解析所需",
                "核对从站配置的寄存器数量（压力/流量/时间/状态/控制共 5 个）；确认链路已连接后再启动采集",
                AlarmLevel.Warning);

            Add<ArgumentOutOfRangeException>(FaultCategory.Configuration,
                "参数越界",
                "配置项取值超出允许范围，如寄存器数量为 0、站号大于 247",
                "检查从站配置：站号 1~247、寄存器数量 ≥ 4、轮询周期 ≥ 100ms",
                AlarmLevel.Warning);

            Add<FormatException>(FaultCategory.Configuration,
                "数据格式错误",
                "收到无法解析的报文，通常是波特率不匹配导致字节错位",
                "核对串口波特率、数据位、校验位与停止位四项参数",
                AlarmLevel.Warning);

            // ---- 存储层 ----
            Add<System.Data.Common.DbException>(FaultCategory.Storage,
                "数据库操作失败",
                "数据库文件被占用、损坏或磁盘空间不足",
                "确认没有其他程序打开该 db 文件；检查磁盘剩余空间；必要时备份后重建库文件",
                AlarmLevel.Warning);
        }

        private void Add<T>(FaultCategory category, string symptom, string cause,
                            string suggestion, AlarmLevel level) where T : Exception
        {
            _rules.Add(new Rule
            {
                Match = e => FindInner<T>(e) != null,
                Category = category,
                Symptom = symptom,
                Cause = cause,
                Suggestion = suggestion,
                Level = level
            });
        }

        private Rule Match(Exception ex)
        {
            foreach (var rule in _rules)
            {
                try
                {
                    if (rule.Match(ex)) return rule;
                }
                catch
                {
                    // 规则本身出错不能反过来打断诊断
                }
            }
            return null;
        }

        /// <summary>状态位 → [原因, 处置建议]。key 与 BitAlarmNames 的文案保持一致。</summary>
        private static readonly Dictionary<string, string[]> BitRules = new Dictionary<string, string[]>
        {
            { "加载/卸载", new[] { "压缩机在加载与卸载之间切换", "属正常运行状态；若切换过于频繁，检查储气罐容量与用气量是否匹配" } },
            { "运行/停止", new[] { "主机运行状态变化", "属正常运行状态；若意外停机，查看是否有其他故障位同时置位" } },
            { "排气温度过高", new[] { "冷却器散热不良、冷却水/风量不足、油位偏低或油滤堵塞、环境温度过高", "检查冷却器翅片是否积灰并清理；确认油位在刻度内；改善机房通风；核对温度传感器是否漂移" } },
            { "相序错误", new[] { "进线三相相序接反，或缺相", "断电后对调任意两相进线；用相序表确认；同时检查是否伴随缺相" } },
            { "主电机电流故障", new[] { "主电机过载、接触器触点烧蚀、电流互感器异常或电源电压偏低", "测量三相电流是否平衡；检查接触器触点；确认电源电压在额定范围内" } },
            { "空滤器堵塞", new[] { "空气滤芯积尘到报警压差", "更换空气滤芯；粉尘大的场合缩短更换周期并加装前置过滤" } },
            { "风机电流故障", new[] { "风扇电机过载或卡滞、轴承损坏", "手动盘车确认风扇是否卡滞；检查轴承并加注润滑脂；测量风扇电机电流" } },
            { "供气压力过高", new[] { "卸载阀未动作、压力开关或压力传感器失效", "检查卸载阀与电磁阀动作是否正常；校验压力传感器读数与机械压力表是否一致" } },
            { "油分气堵塞", new[] { "油分离器滤芯堵塞导致压差过大", "更换油分离器滤芯；同时检查回油管路是否通畅" } },
        };

        // =====================================================================

        /// <summary>在异常链里找指定类型。NModbus 常把底层异常包一层再抛。</summary>
        private static T FindInner<T>(Exception ex) where T : Exception
        {
            var cur = ex;
            int guard = 0;
            while (cur != null && guard++ < 16)
            {
                var t = cur as T;
                if (t != null) return t;

                var agg = cur as AggregateException;
                if (agg != null)
                {
                    foreach (var inner in agg.InnerExceptions)
                    {
                        var hit = FindInner<T>(inner);
                        if (hit != null) return hit;
                    }
                }

                cur = cur.InnerException;
            }
            return null;
        }

        /// <summary>把异常链拍平成一行，便于写日志。</summary>
        private static string Describe(Exception ex)
        {
            if (ex == null) return string.Empty;

            var parts = new List<string>();
            var cur = ex;
            int guard = 0;
            while (cur != null && guard++ < 8)
            {
                parts.Add(string.Format("{0}: {1}", cur.GetType().Name, cur.Message));
                cur = cur.InnerException;
            }
            return string.Join(" <- ", parts.ToArray());
        }
    }
}
