using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AirCompressMonitor.Wpf
{
    /// <summary>
    /// 轻量 MVVM 的通知基类：只做 INotifyPropertyChanged 一件事。
    /// 刻意不引 Prism / MVVMLight —— 现场机器上多一个 dll 就多一个部署变数。
    ///
    /// 必须是 public：<see cref="ViewModel.MainViewModel"/> 是 public 且继承它，
    /// 基类可访问性低于派生类会直接 CS0060 编译不过。
    /// </summary>
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        /// <summary>
        /// 值真的变了才通知。采集线程每秒推好几帧，
        /// 无条件 OnPropertyChanged 会让界面每秒重绘一堆没变的灯 —— 这正是
        /// 「高并发采集下界面稳定」要防的那类无谓开销。
        /// </summary>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
