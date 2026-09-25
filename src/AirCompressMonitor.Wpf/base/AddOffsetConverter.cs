using System;
using System.Globalization;
using System.Windows.Data;

namespace AirCompressMonitor.Wpf
{
    /// <summary>
    /// 把绑定值加上 ConverterParameter 指定的偏移量，用于仪表刻度的微调显示。
    /// 必须是 public：XAML 里以 `local:AddOffsetConverter` 实例化，internal 会解析不到。
    /// </summary>
    public class AddOffsetConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double offset;
            if (value is double d && double.TryParse(parameter == null ? null : parameter.ToString(),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out offset))
            {
                return d + offset;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 仪表只用于显示，回写没有语义。返回 DoNothing 而不是抛异常：
            // 抛异常会让 WPF 把它当成绑定错误记进 trace，掩盖真正的问题。
            return Binding.DoNothing;
        }
    }
}
