using System;
using System.Windows.Forms;

namespace AirCompressMonitor.Tools
{
    internal static class Program
    {
        /// <summary>
        /// 工业设备监控工具集的入口。
        ///
        /// 这个工具集和正式监控软件是两回事：正式软件是「装在现场、长期跑」的，
        /// 工具集是「揣在 U 盘里、到现场打开就用」的 —— 只依赖 .NET Framework 4.7.2
        /// 和一份 exe，不装服务、不写注册表。
        /// </summary>
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 界面线程上的未处理异常一律弹出来。
            // 工具集的使用场景是现场排障，把异常吞掉等于让人对着一个没反应的窗口猜。
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "未处理的界面异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.Run(new MainForm());
        }
    }
}
