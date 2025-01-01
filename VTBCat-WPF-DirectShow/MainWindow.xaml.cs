using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;
using DirectShowLib;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Media.Media3D;
using System.Windows.Threading;


namespace TransparentVideoWindow
{
    public partial class MainWindow : Window
    {
        private VideoCapture _capture;
        private List<string> _cameraDevicesName = new List<string>();
        private DsDevice[] _cameraDevices = null;
        private bool _isDragging = false;
        private Point _lastMousePosition;
        double _aspectRatio;

        private double _frameRate = 60.0;
        
        private IFilterGraph2 _filterGraph = null;
        private IMediaControl _mediaControl = null;
        private ISampleGrabber _sampleGrabber = null;
        private IBaseFilter _cameraFilter = null;
        private DispatcherTimer _frameTimer;
        private int _currentDeviceIndex = 0;
        private const int _initCameraIndex = 2;
        
        bool hasInited = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeCamera();
            GetCameraDevices();
            PopulateMenuItems();
            StartCamera(_initCameraIndex);
            hasInited = true;
        }

        private void InitializeCamera()
        {
            // 获取摄像头设备列表
            GetCameraDevices();

            if (_cameraDevices.Length == 0)
            {
                MessageBox.Show("未检测到摄像头设备！");
                return;
            }
            InitializeTimer();
        }
        private void InitializeTimer()
        {
            // 设置定时器，用于更新帧
            _frameTimer?.Stop();
            _frameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / _frameRate)
            };
            _frameTimer.Tick += UpdateFrame;
            _frameTimer.Start();
        }
        
        private void GetCameraDevices()
        {
            _cameraDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            
            _cameraDevicesName.Clear();
            foreach (var camera in _cameraDevices)
            {
                _cameraDevicesName.Add(camera.Name);
            }
            if (_cameraDevices.Length == 0)
            {
                MessageBox.Show("未检测到摄像头设备！");
                return;
            }
        }
        
        private void PopulateMenuItems()
        {
            //Cameras
            for (int i = 0; i < _cameraDevicesName.Count; i++)
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = _cameraDevicesName[i];
                menuItem.Tag = i;
                menuItem.IsCheckable = true;
                menuItem.IsChecked = false;
                if (i == _initCameraIndex) menuItem.IsChecked = true;
                menuItem.Click += (sender, e) =>
                {
                    int index = (int)(sender as MenuItem).Tag;
                    foreach (var item in CameraListMenu.Items)
                    {
                        (item as MenuItem).IsChecked = item.Equals(menuItem) ;
                    }
                    StartCamera(index);
                };
                this.CameraListMenu.Items.Add(menuItem);
            }
            //Frames
            double[] FrameChoices = new[] {1.0, 30.0, 60.0, 120.0 };//If you want more choices, add numbers here.
            foreach (double frameRate in FrameChoices)
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = frameRate.ToString();
                menuItem.Tag = frameRate;
                menuItem.IsCheckable = true;
                menuItem.IsChecked = false;
                if (frameRate == _frameRate) menuItem.IsChecked = true;
                menuItem.Click += (sender, e) =>
                {
                    _frameRate = (double)(sender as MenuItem).Tag;
                    foreach (var item in FrameListMenu.Items)
                    {
                        (item as MenuItem).IsChecked = item.Equals(menuItem) ;
                    }
                    InitializeTimer();
                };
                this.FrameListMenu.Items.Add(menuItem);
            }
        }

        private void StartCamera(int deviceIndex)
        {
            if (hasInited)
            {
                for (int i = 0; i < _cameraDevices.Length; i++)
                {
                    var menuItem = CameraListMenu.Items[i] as MenuItem;
                    if (menuItem != null)
                    {
                        // 只选中与当前设备索引匹配的菜单项
                        menuItem.IsChecked = (i == deviceIndex);
                    }
                }
            }
            try
            {
                // 释放旧资源
                StopCamera();

                _currentDeviceIndex = deviceIndex;

                // 创建过滤图
                _filterGraph = new FilterGraph() as IFilterGraph2;

                // 添加视频捕获设备
                _cameraFilter = AddCameraFilterToGraph(_cameraDevices[deviceIndex]);

                // 创建并配置 SampleGrabber
                _sampleGrabber = new SampleGrabber() as ISampleGrabber;
                ConfigureSampleGrabber(_sampleGrabber);

                // 添加 SampleGrabber 到过滤图
                IBaseFilter grabberFilter = (IBaseFilter)_sampleGrabber;
                _filterGraph.AddFilter(grabberFilter, "SampleGrabber");

                // 连接设备到 SampleGrabber
                ConnectPins(_cameraFilter, grabberFilter);

                // 连接 SampleGrabber 的输出 Pin，不连接到渲染器，禁用 ActiveMovie
                // SampleGrabber 的输出流无需渲染到默认窗口
                // _filterGraph.Render(GetPin(grabberFilter, "Output")); --> 注释此行，避免连接默认渲染器

                // 禁用 ActiveMovie Window
                IVideoWindow videoWindow = _filterGraph as IVideoWindow;
                if (videoWindow != null)
                {
                    videoWindow.put_AutoShow(OABool.False); // 禁用显示
                    videoWindow.put_Visible(OABool.False); // 确保隐藏
                }
                // 启动视频流
                _mediaControl = _filterGraph as IMediaControl;
                _mediaControl?.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动摄像头失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取设备的过滤器并添加到过滤图
        /// </summary>
        private IBaseFilter AddCameraFilterToGraph(DsDevice device)
        {
            IBaseFilter filter;
            _filterGraph.AddSourceFilterForMoniker(device.Mon, null, device.Name, out filter);
            return filter;
        }

        /// <summary>
        /// 配置 SampleGrabber
        /// </summary>
        private void ConfigureSampleGrabber(ISampleGrabber grabber)
        {
            var mediaType = new AMMediaType
            {
                majorType = MediaType.Video,
                subType = MediaSubType.ARGB32, // 请求支持 Alpha 的 ARGB32 媒体类型
                formatType = FormatType.VideoInfo
            };

            grabber.SetMediaType(mediaType);
            grabber.SetBufferSamples(true); // 启用缓冲模式，允许访问帧
        }

        /// <summary>
        /// 连接 pin
        /// </summary>
        private void ConnectPins(IBaseFilter source, IBaseFilter target)
        {
            var sourcePin = GetPin(source, "Capture") ?? GetPin(source, "Output");
            var targetPin = GetPin(target, "Input");

            if (sourcePin == null || targetPin == null)
            {
                throw new Exception("无法连接设备 Pin！");
            }

            _filterGraph.Connect(sourcePin, targetPin);
        }

        /// <summary>
        /// 获取过滤器的 Pin
        /// </summary>
        public static IPin GetPin(IBaseFilter filter, string direction)
        {
            IEnumPins pins;
            filter.EnumPins(out pins);
            IPin[] pin = new IPin[1];
            IntPtr fetched = IntPtr.Zero;

            while (pins.Next(1, pin, fetched) == 0)
            {
                PinDirection dir;
                pin[0].QueryDirection(out dir);
                if ((direction == "Output" && dir == PinDirection.Output) ||
                    (direction == "Input" && dir == PinDirection.Input))
                {
                    return pin[0];
                }

                Marshal.ReleaseComObject(pin[0]);
            }

            return null;
        }

        /// <summary>
        /// 更新每一帧，将其显示到控件中
        /// </summary>
        private void UpdateFrame(object sender, EventArgs e)
        {

            if (_sampleGrabber == null) return;
            
            int bufferSize = 0;
            _sampleGrabber.GetCurrentBuffer(ref bufferSize, IntPtr.Zero);
            if (bufferSize <= 0) return;

            IntPtr buffer = Marshal.AllocCoTaskMem(bufferSize);
            try
            {
                _sampleGrabber.GetCurrentBuffer(ref bufferSize, buffer);

                // 获取媒体类型信息
                AMMediaType mediaType = new AMMediaType();
                _sampleGrabber.GetConnectedMediaType(mediaType);

                var videoInfo = (VideoInfoHeader)Marshal.PtrToStructure(mediaType.formatPtr, typeof(VideoInfoHeader));
                int width = videoInfo.BmiHeader.Width;
                int height = Math.Abs(videoInfo.BmiHeader.Height); // 高度取绝对值，防止负数
                _aspectRatio = (double)width / (double) height;
                if ((int)(this.Height * _aspectRatio) != (int)this.Width)
                {
                    this.Width = this.Height * _aspectRatio;
                }
                int stride = width * 4; // 每行字节数 (BGRA32)

                // 将缓冲区从 IntPtr 转换为 byte[]
                byte[] frameData = new byte[bufferSize];
                Marshal.Copy(buffer, frameData, 0, bufferSize);

                // 对帧进行 Alpha 模拟处理
                ProcessFrameAlpha(frameData, width, height, stride);

                // 转换回 IntPtr，用于 BitmapSource 创建
                IntPtr processedBuffer = Marshal.AllocCoTaskMem(bufferSize);
                try
                {
                    Marshal.Copy(frameData, 0, processedBuffer, bufferSize);

                    // 使用 BitmapSource 创建可渲染图片
                    var bitmap = BitmapSource.Create(
                        width,
                        height,
                        96, 96,
                        PixelFormats.Bgra32, // BGRA32 格式支持透明
                        null,
                        processedBuffer,
                        bufferSize,
                        stride
                    );

                    // 输出视频到 VideoImage
                    VideoImage.Source = bitmap;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(processedBuffer);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }

        }

        /// <summary>
        /// 处理帧数据，支持透明像素
        /// </summary>
        private void ProcessFrameAlpha(byte[] frameData, int width, int height, int stride)
        {
            int bytesPerPixel = 4; // 每像素字节数 (BGRA)

            byte[] flippedFrameData = new byte[frameData.Length]; // 用于保存翻转后的帧

            // 从底部向上复制像素行
            for (int y = 0; y < height; y++)
            {
                // 原始帧数据的起始偏移
                int sourceOffset = y * stride;

                // 翻转后帧数据的目标偏移
                int targetOffset = (height - y - 1) * stride;

                // 复制该行字节数据
                Array.Copy(frameData, sourceOffset, flippedFrameData, targetOffset, stride);
            }

            // 用翻转后的帧数据覆盖原始帧
            Array.Copy(flippedFrameData, frameData, frameData.Length);

            // Alpha 处理逻辑（如果需要模拟透明效果）
            for (int i = 0; i < frameData.Length; i += 4)
            {
                byte blue = frameData[i + 0];
                byte green = frameData[i + 1];
                byte red = frameData[i + 2];
                byte alpha = frameData[i + 3]; // ARGB格式中的Alpha

                // 如果真正的 Alpha 通道不可用，可模拟处理：例如黑色背景透明
                if (green == 0 && blue == 0  && red == 0)
                {
                    frameData[i + 3] = 0; // 设置为完全透明
                }
            }

        }     
        /// <summary>
        /// 停止摄像头并释放资源
        /// </summary>
        private void StopCamera()
        {
            if (_mediaControl != null)
            {
                _mediaControl.Stop();
                Marshal.ReleaseComObject(_mediaControl);
                _mediaControl = null;
            }

            if (_filterGraph != null)
            {
                Marshal.ReleaseComObject(_filterGraph);
                _filterGraph = null;
            }

            if (_cameraFilter != null)
            {
                Marshal.ReleaseComObject(_cameraFilter);
                _cameraFilter = null;
            }

            if (_sampleGrabber != null)
            {
                Marshal.ReleaseComObject(_sampleGrabber);
                _sampleGrabber = null;
            }
        }
        
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // 支持窗口拖动
                _isDragging = true;
                _lastMousePosition = e.GetPosition(this);
                this.CaptureMouse();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                // 显示右键菜单
                ContextMenu contextMenu = this.Menu;
                contextMenu.PlacementTarget = this;
                contextMenu.IsOpen = true;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var currentPosition = e.GetPosition(this);
                var offsetX = currentPosition.X - _lastMousePosition.X;
                var offsetY = currentPosition.Y - _lastMousePosition.Y;

                this.Left += offsetX;
                this.Top += offsetY;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = false;
                this.ReleaseMouseCapture();
            }
        }
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            _capture?.Dispose();
            _frameTimer?.Stop();
            Application.Current.Shutdown();
        }
    }
}