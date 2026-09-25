using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AirCompressMonitor.Wpf.Control
{
    /// <summary>
    /// 仪表盘控件
    /// </summary>
    public partial class Meter : UserControl
    {
        #region 依赖属性定义
        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
        // 修改：Value 使用专用回调，仅更新指针，避免无谓的刻度重绘
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(Meter),
                new PropertyMetadata(50.0, new PropertyChangedCallback(OnValueChanged)));

        // 单位
        public string Unit
        {
            get { return (string)GetValue(UnitProperty); }
            set { SetValue(UnitProperty, value); }
        }
        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register("Unit", typeof(string), typeof(Meter),
                new PropertyMetadata(""));

        // 提示文本
        public string Tip
        {
            get { return (string)GetValue(TipProperty); }
            set { SetValue(TipProperty, value); }
        }
        public static readonly DependencyProperty TipProperty =
            DependencyProperty.Register("Tip", typeof(string), typeof(Meter),
                new PropertyMetadata(""));

        // 最小值
        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register("Minimum", typeof(double), typeof(Meter),
                new PropertyMetadata(0.0, new PropertyChangedCallback(OnPropertyChanged)));

        // 最大值
        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }
        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(Meter),
                new PropertyMetadata(100.0, new PropertyChangedCallback(OnPropertyChanged)));

        // 间隔
        public double Interval
        {
            get { return (double)GetValue(IntervalProperty); }
            set { SetValue(IntervalProperty, value); }
        }
        public static readonly DependencyProperty IntervalProperty =
            DependencyProperty.Register("Interval", typeof(double), typeof(Meter),
                new PropertyMetadata(5.0, new PropertyChangedCallback(OnPropertyChanged)));

        // 刻度数量
        public int ScaleCount
        {
            get { return (int)GetValue(ScaleCountProperty); }
            set { SetValue(ScaleCountProperty, value); }
        }
        public static readonly DependencyProperty ScaleCountProperty =
            DependencyProperty.Register("ScaleCount", typeof(int), typeof(Meter),
                new PropertyMetadata(9, new PropertyChangedCallback(OnPropertyChanged)));

        // 刻度线粗细
        public double ScaleThickness
        {
            get { return (double)GetValue(ScaleThicknessProperty); }
            set { SetValue(ScaleThicknessProperty, value); }
        }
        public static readonly DependencyProperty ScaleThicknessProperty =
            DependencyProperty.Register("ScaleThickness", typeof(double), typeof(Meter),
                new PropertyMetadata(1.0, new PropertyChangedCallback(OnPropertyChanged)));

        // 刻度线颜色
        public Brush ScaleBrush
        {
            get { return (Brush)GetValue(ScaleBrushProperty); }
            set { SetValue(ScaleBrushProperty, value); }
        }
        public static readonly DependencyProperty ScaleBrushProperty =
            DependencyProperty.Register("ScaleBrush", typeof(Brush), typeof(Meter),
                new PropertyMetadata(Brushes.White, new PropertyChangedCallback(OnPropertyChanged)));

        // 指针颜色
        public Brush PointerBrush
        {
            get { return (Brush)GetValue(PointerBrushProperty); }
            set { SetValue(PointerBrushProperty, value); }
        }
        public static readonly DependencyProperty PointerBrushProperty =
            DependencyProperty.Register("PointerBrush", typeof(Brush), typeof(Meter),
                new PropertyMetadata(Brushes.Red, new PropertyChangedCallback(OnPropertyChanged)));

        // 字体大小。刻意用 new 遮蔽 Control.FontSizeProperty：
        // 仪表内部刻度数字要按自己的默认值（9）走，不跟随外层控件的字体继承。
        public new double FontSize
        {
            get { return (double)GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }
        public static new readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register("FontSize", typeof(double), typeof(Meter),
                new PropertyMetadata(9.0, new PropertyChangedCallback(OnPropertyChanged)));

        #endregion

        #region 私有字段

        // 标记刻度是否已绘制（优化性能，避免重复绘制）
        private bool _isScaleDrawn = false;

        // 修改：缓存是否已构建指针几何（仅在尺寸变化时重建）
        private bool _isPointerBuilt = false;

        // 修改：使用单一 Path 缓存所有刻度线（替代大量 Line 元素）
        private Path _scalesPath;

        // 修改：缓存指针几何以减少频繁字符串解析或重建
        private StreamGeometry _pointerGeometryCache;

        // 用于解析几何图形的转换器（保留，用于小量现有用法）
        private static readonly TypeConverter _geometryConverter =
            TypeDescriptor.GetConverter(typeof(Geometry));

        #endregion

        #region 属性变更回调

        // 当影响刻度或整体布局的依赖属性变化时触发刷新（重绘刻度）
        static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var meter = d as Meter;
            if (meter == null) return;

            // 修改：改变与刻度/尺寸相关的属性时重绘（标记需要重建指针和刻度）
            meter._isScaleDrawn = false;
            meter._isPointerBuilt = false;
            meter.Refresh();
        }

        // 修改：Value 变更专用回调，仅更新指针，避免重建刻度/布局
        // 修改：替换类中的 OnValueChanged 与 DrawScales 小刻度计算处（只给出修改方法，直接替换源文件对应方法）
        static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var meter = d as Meter;
            if (meter == null) return;

            // 如果控件尚未准备好（尺寸为 0 或刻度尚未绘制），触发完整刷新以确保刻度与指针几何被构建。
            // 修改说明：原实现直接只调用 UpdatePointer，可能在首次绑定时控件未构建完造成指针/刻度不显示。
            if (meter.ActualWidth <= 0 || meter.ActualHeight <= 0 || !meter._isScaleDrawn)
            {
                meter.Refresh();
                return;
            }

            // 否则仅更新指针（轻量）
            double radius = meter.border.Width / 2;
            meter.UpdatePointer(radius);
        }

        #endregion

        #region 构造函数

        public Meter()
        {
            InitializeComponent();

            // 设置默认值
            SetCurrentValue(MinimumProperty, 0d);
            SetCurrentValue(MaximumProperty, 100d);
            SetCurrentValue(IntervalProperty, 10d);

            // 初始化合并刻度 Path（修改：提前创建并缓存）
            _scalesPath = new Path
            {
                Stroke = this.ScaleBrush,
                StrokeThickness = this.ScaleThickness,
                Fill = Brushes.Transparent, // 仅绘制线
                IsHitTestVisible = false
            };

            // 尺寸变化时需要重新绘制刻度和指针
            SizeChanged += (se, ev) =>
            {
                _isScaleDrawn = false; // 标记需要重新绘制刻度
                _isPointerBuilt = false; // 标记需要重新构建指针几何
                Refresh();
            };

            // 加载完成后首次刷新
            Loaded += (s, e) => Refresh();
        }

        #endregion

        #region 核心刷新方法

        /// <summary>
        /// 刷新仪表盘显示
        /// 优化：将刻度绘制和指针更新分离，提高性能
        /// </summary>
        private void Refresh()
        {
            // 如果控件尚未渲染，直接返回
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            // 1. 更新边框和圆形尺寸，保证为正方形
            this.border.Width = Math.Min(RenderSize.Width, RenderSize.Height);
            this.border.Height = Math.Min(RenderSize.Width, RenderSize.Height);
            this.border.CornerRadius = new CornerRadius(this.border.Width / 2);

            double radius = this.border.Width / 2;

            // 2. 绘制刻度（只在需要时绘制，避免每次数据更新都重绘）
            if (!_isScaleDrawn || (this.canvasPlate.Children.Count == 0))
            {
                DrawScales(radius);
                _isScaleDrawn = true;
            }

            // 3. 确保指针几何在尺寸变化时重建（修改：尺寸变化才重建几何）
            if (!_isPointerBuilt)
            {
                BuildPointerGeometry(radius);
                _isPointerBuilt = true;
            }

            // 4. 更新指针位置（每次数据更新都执行，但这是轻量操作）
            UpdatePointer(radius);
        }

        #endregion

        #region 刻度绘制方法

        /// <summary>
        /// 绘制刻度线和刻度标签
        /// 注意：此方法只在首次加载或尺寸变化时调用
        /// 修改：
        /// - 小刻度与主刻度线合并为单个 StreamGeometry 放入单一 Path，减少 Visual 数量
        /// - 保留文本标签使用 TextBlock（便于样式与绑定），但总元素数显著减少
        /// </summary>
        /// <summary>
        /// 绘制刻度线和刻度标签（修改后）
        /// 主要改动：
        /// - 将大量小刻度合并为单个 StreamGeometry 放入单一 Path，减少 Visual 节点数量与布局开销。
        /// - 对小刻度数量加上上限，防止大范围（如 999.9）产生成千上万条刻度导致性能问题。
        /// - 保留刻度标签为 TextBlock 以便样式与可访问性，标签数量等于 ScaleCount + 1。
        /// </summary>
        private void DrawScales(double radius)
        {
            // 清除之前的刻度（重新添加合并后的 Path 与标签）
            this.canvasPlate.Children.Clear();

            if (ScaleCount <= 0 || radius <= 0)
                return;

            // 保留原有外边框绘制方式
            string borderPathData = $"M4,{radius}A{radius - 4} {radius - 4} 0 1 1 {radius} {this.border.Height - 4}";
            this.plateBorder.Data = (Geometry)_geometryConverter.ConvertFrom(borderPathData);

            // 更新合并刻度 Path 的样式并准备数据
            _scalesPath.Stroke = this.ScaleBrush;
            _scalesPath.StrokeThickness = this.ScaleThickness;

            // 角度映射：270度覆盖 [Minimum, Maximum]
            double range = this.Maximum - this.Minimum;
            double stepPerUnit = 270.0 / Math.Max(0.1, range);

            // 构建小刻度与主刻度的 StreamGeometry（减少大量 Line 元素）
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                // 小刻度数：将范围取整并限制最大刻度数量以避免性能问题
                int maxSmallTicks = 360; // 上限，可根据需要调整
                int smallTickCount = Math.Max(0, (int)Math.Round(range));
                smallTickCount = Math.Min(smallTickCount, maxSmallTicks);

                for (int i = 0; i < smallTickCount; i++)
                {
                    double angleDeg = (i) * stepPerUnit; // 角度（度）
                    double rad = angleDeg * Math.PI / 180.0;
                    Point p1 = new Point(radius - (radius - 13) * Math.Cos(rad), radius - (radius - 13) * Math.Sin(rad));
                    Point p2 = new Point(radius - (radius - 8) * Math.Cos(rad), radius - (radius - 8) * Math.Sin(rad));
                    ctx.BeginFigure(p1, false, false);
                    ctx.LineTo(p2, true, false);
                }

                // 主刻度：使用 ScaleCount 分割 270 度，绘制更长的刻度线
                // 角度步长（度） = 270 / ScaleCount
                double angleStepMain = 270.0 / Math.Max(1, this.ScaleCount);
                for (int i = 0; i <= this.ScaleCount; i++)
                {
                    double angleDeg = i * angleStepMain;
                    double rad = angleDeg * Math.PI / 180.0;
                    Point p1 = new Point(radius - (radius - 20) * Math.Cos(rad), radius - (radius - 20) * Math.Sin(rad));
                    Point p2 = new Point(radius - (radius - 8) * Math.Cos(rad), radius - (radius - 8) * Math.Sin(rad));
                    ctx.BeginFigure(p1, false, false);
                    ctx.LineTo(p2, true, false);
                }
            }
            geo.Freeze();
            _scalesPath.Data = geo;

            // 将合并后的 Path 添加到画布（确保在最底层）
            this.canvasPlate.Children.Add(_scalesPath);

            // 绘制刻度标签（保留 TextBlock，数量为 ScaleCount + 1）
            double labelIntervalValue = 0;
            double valueStep = (range) / Math.Max(1, this.ScaleCount);
            for (int i = 0; i <= this.ScaleCount; i++)
            {
                double labelValue = this.Minimum + i * valueStep;
                double angleDeg = (labelValue - this.Minimum) * stepPerUnit;
                double rad = angleDeg * Math.PI / 180.0;

                TextBlock txtScale = new TextBlock
                {
                    Text = labelValue.ToString("0"),
                    FontFamily = new FontFamily("yahei"),
                    Width = 34,
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.White),
                    RenderTransform = new RotateTransform() { Angle = 45, CenterX = 17, CenterY = 8 },
                    FontSize = this.FontSize,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(txtScale, radius - (radius - 34) * Math.Cos(rad) - 17);
                Canvas.SetTop(txtScale, radius - (radius - 34) * Math.Sin(rad) - 8);
                this.canvasPlate.Children.Add(txtScale);

                labelIntervalValue += valueStep;
            }
        }

        #endregion

        #region 指针构建与更新方法

        /// <summary>
        /// 修改：仅在尺寸改变时构建指针几何体，之后通过 RotateTransform 改变角度
        /// </summary>
        private void BuildPointerGeometry(double radius)
        {
            // 防御性检查
            if (radius <= 0) return;

            // 使用 StreamGeometry 构建指针形状（替代字符串解析以减少分配）
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                // 原字符串模板："M{0} {1},{2} {0},{0} {3},{3} {0},{0} {1}"
                // 解释为一系列线段构成指针多边形，下面用相同点顺序构建
                Point pCenter = new Point(radius, radius);
                Point pBottom = new Point(radius, radius + 2);
                Point pFar = new Point(this.border.Width - radius / 10, radius);
                Point pTop = new Point(radius, radius - 4);

                // 从底部开始，绘制多边形（闭合）
                ctx.BeginFigure(pBottom, true, true);
                ctx.LineTo(pFar, true, false);
                ctx.LineTo(pTop, true, false);
                ctx.LineTo(pCenter, true, false);
            }
            geo.Freeze();
            _pointerGeometryCache = geo;

            // 应用到现有 pointer Path（XAML 中定义），并保持通过 RotateTransform 控制角度
            this.pointer.Data = _pointerGeometryCache;
            this.pointer.Fill = this.PointerBrush;
        }

        /// <summary>
        /// 更新指针位置和圆环进度（轻量）
        /// 修改要点：
        /// - Value 值先做范围限制
        /// - 重用已缓存的指针几何（仅更新 RotateTransform 与 Brush）
        /// - 使用 StreamGeometry 构建圆弧路径（替代字符串解析）
        /// </summary>
        private void UpdatePointer(double radius)
        {
            // 防御性检查
            if (radius <= 0) return;

            // 限制 Value 在 [Minimum, Maximum]
            double value = Math.Max(Minimum, Math.Min(Maximum, this.Value));

            // 计算每个单位值对应的角度
            double step = 270.0 / Math.Max(0.1, (this.Maximum - this.Minimum));
            double targetAngle = (value - Minimum) * step + 135;

            // 指针动画（使用缓动函数使动画更平滑）
            DoubleAnimation pointerAnimation = new DoubleAnimation(
                targetAngle,
                new Duration(TimeSpan.FromMilliseconds(150)));
            pointerAnimation.EasingFunction = new QuadraticEase();
            this.rtPointer.BeginAnimation(RotateTransform.AngleProperty, pointerAnimation);

            // 确保指针几何已构建（仅在尺寸变更时重建）
            if (!_isPointerBuilt || _pointerGeometryCache == null)
            {
                BuildPointerGeometry(radius);
                _isPointerBuilt = true;
            }
            // 更新指针颜色（Brush 通过依赖属性变更也会触发 OnPropertyChanged）
            this.pointer.Fill = this.PointerBrush;

            // 更新圆环（进度指示器）——改为使用 StreamGeometry ArcTo，减少字符串解析
            double thickness = radius / 2;
            this.circle.StrokeThickness = thickness;

            // 计算圆环的终点位置
            double angleRad = (value - Minimum) * step * Math.PI / 180;
            Point endPoint = new Point(radius - (radius - thickness / 2) * Math.Cos(angleRad),
                                       radius - (radius - thickness / 2) * Math.Sin(angleRad));

            // 判断是否为大圆弧
            bool isLarge = ((value - Minimum) * step) >= 180.0;

            // 构建圆环弧形几何
            var arcGeo = new StreamGeometry();
            using (var ctx = arcGeo.Open())
            {
                Point startPoint = new Point(thickness / 2, radius);
                ctx.BeginFigure(startPoint, false, false);
                // ArcTo(target, size, rotationAngle, isLargeArc, sweepDirection, isStroked, isSmoothJoin)
                ctx.ArcTo(endPoint,
                          new Size(radius - thickness / 2, radius - thickness / 2),
                          0,
                          isLarge,
                          SweepDirection.Clockwise,
                          true,
                          false);
            }
            arcGeo.Freeze();
            this.circle.Data = arcGeo;

            // 小尺寸仪表盘不显示圆环
            this.circle.Visibility = (this.border.Width < 200) ? Visibility.Collapsed : Visibility.Visible;
        }
        #endregion
    }
}