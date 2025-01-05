using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DirectShowLib;
using System.Runtime.InteropServices;
using System.Windows.Threading;



namespace TransparentVideoWindow
{
    public partial class MainWindow : Window
    {
        private List<string> _cameraDevicesName = new List<string>();
        private DsDevice[] _cameraDevices = null;
        private bool _isDragging = false;
        private System.Windows.Point _lastMousePosition;
        double _aspectRatio;
        double[] _frameChoices = new[] { 1.0, 30.0, 60.0, 120.0,360.0};//If you want more choices, add numbers here.
        private double _frameRate = 120.0;
        private MouseButtonEventArgs _LastRightClickMouseEvent;


        //about background removal
        private double _backgroundRemveStrength;//From 0 to 1
        //Ways to remove background
        public enum BackgroundRemovalMode
        {
            None,              
            RemoveBlack,       
            RemoveGreen  
        }
        public BackgroundRemovalMode _backgroundRemovalMode = BackgroundRemovalMode.RemoveBlack;
        int _frameBorderTop = -1, _frameBorderBottom = int.MaxValue, _frameBorderLeft = -1, _frameBorderRight = int.MaxValue;
        int _frameCounter = 0; // 用于计算帧数，每一秒重置一次，方便调用DetectBorder

        //about camera
        private IFilterGraph2 _filterGraph = null;
        private IMediaControl _mediaControl = null;
        private ISampleGrabber _sampleGrabber = null;
        private IBaseFilter _cameraFilter = null;
        private DispatcherTimer _frameTimer;
        DispatcherTimer _showFrameCounter;
        private int _currentDeviceIndex = 0;
        private int _initCameraIndex;

        //form 缩放比例
        private double _formSize = 1;
        private double _originWidth, _originHeight;

        bool hasInited = false;
        //================================Initialize================================
        public MainWindow()
        {
            InitializeComponent();
            InitializeCamera();
            //GetIcon(); //现在在xaml中完成了
            GetCameraDevices();
            _initCameraIndex = _cameraDevices.Length - 1;//默认启动最后一个摄像机
            PopulateMenuItems();
            StartCamera(_initCameraIndex);
            //使得右键右下角托盘显现一样的菜单
            MyNotifyIcon.ContextMenu = Menu;
            InitializeSlider();
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

            _showFrameCounter?.Stop();
            _showFrameCounter = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000.0)
            };
            _showFrameCounter.Tick += ShowFrameRate;
            _showFrameCounter.Start();
        }
        //Test
        private void ShowFrameRate(object sender, EventArgs e)
        {
            FrameCounter.Text = "FrameRate: " + _frameCounter;
            _frameCounter = 0;
        }
        private void InitializeSlider()
        {
            _originHeight = this.Height;
            _originWidth = this.Width;
            //窗口不能大过屏幕
            Slider_FormSize.Maximum = Math.Min(SystemParameters.PrimaryScreenHeight / _originHeight, SystemParameters.PrimaryScreenWidth / _originWidth)-0.05;
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

        /*private void GetIcon()
        {
            //遍历目录及其子目录中的所有文件
            string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] icoFiles = Directory.GetFiles(workingDirectory, "*.ico", SearchOption.TopDirectoryOnly);
            if (icoFiles.Length > 0)
            {
                try
                {
                    string iconPath = icoFiles[0];
                    Icon icon = new Icon(iconPath);
                    MyNotifyIcon.Icon = System.Drawing.Icon.FromHandle(icon.Handle);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading icon: {ex.Message}");
                }
            }
        }*/
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
                        (item as MenuItem).IsChecked = item.Equals(menuItem);
                    }
                    StartCamera(index);
                };
                this.CameraListMenu.Items.Add(menuItem);
            }

            //Frames
            foreach (double frameRate in _frameChoices)
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
                        (item as MenuItem).IsChecked = item.Equals(menuItem);
                    }
                    InitializeTimer();
                };
                this.FrameListMenu.Items.Add(menuItem);
            }

            //Background Removal
            foreach (BackgroundRemovalMode mode in Enum.GetValues(typeof(BackgroundRemovalMode)))
            {
                MenuItem menuItem = new MenuItem();
                menuItem.Header = mode.ToString();
                menuItem.Tag = mode;
                menuItem.IsCheckable = true;
                menuItem.IsChecked = false;
                if (mode == _backgroundRemovalMode) menuItem.IsChecked = true;
                menuItem.Click += (sender, e) =>
                {
                    _backgroundRemovalMode = (BackgroundRemovalMode)(sender as MenuItem).Tag;
                    foreach (var item in BRListMenu.Items)
                    {
                        (item as MenuItem).IsChecked = item.Equals(menuItem);
                    }
                };
                this.BRListMenu.Items.Add(menuItem);
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
        //================================Camera and Video================================
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

            _frameCounter++;
            if(_frameCounter> _frameRate)
            {
                _frameCounter = 0;
            }

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
                _aspectRatio = (double)width / (double)height;
                if ((int)(this.Height * _aspectRatio) != (int)this.Width)
                {
                    this.Width = this.Height * _aspectRatio;
                    _originWidth = _originHeight * _aspectRatio;
                }
                int stride = width * 4; // 每行字节数 (BGRA32)

                // 将缓冲区从 IntPtr 转换为 byte[]
                byte[] frameData = new byte[bufferSize];
                Marshal.Copy(buffer, frameData, 0, bufferSize);

                //每5帧检测一次，节省性能
                if (_frameCounter%5 == 0)
                {
                    DetectBorder(frameData, stride);
                }
                // 对帧进行 Alpha 模拟处理
                ProcessFrame(frameData, width, height, stride);

                // 转换回 IntPtr，用于 BitmapSource 创建
                IntPtr processedBuffer = Marshal.AllocCoTaskMem(bufferSize);
                try
                {
                    Marshal.Copy(frameData, 0, processedBuffer, bufferSize);

                    // 使用 BitmapSource 创建可渲染图片
                    var bitmap = BitmapSource.Create(
                        width,
                        height,
                        192, 192,
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
        /// 处理帧数据
        /// </summary>
        private void ProcessFrame(byte[] frameData, int width, int height, int stride)
        {
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

            // 用翻转后的帧数据覆盖原始帧(反正是为了修视频上下翻转的奇怪bug)
            Array.Copy(flippedFrameData, frameData, frameData.Length);
            RemoveBackGround(frameData,stride);
        }
        private void RemoveBackGround(byte[] frameData,int stride)
        {
            if (_backgroundRemovalMode == BackgroundRemovalMode.None) return;

            //去除黑色边框
            RemoveBorder(frameData,stride);

            for (int i = 0; i < frameData.Length; i += 4)
            {
                byte blue = frameData[i + 0];
                byte green = frameData[i + 1];
                byte red = frameData[i + 2];

                //有点hack的方法，但是效果还不错
                //主要是检测周围像素是否是纯正的背景，如果是，那么就降低这个像素判断为背景的标准
                //这样可以很好的防止误判
                int biteBias = 12;//偏移biteBias/4个像素检测
                int num_nearPixelIsBlack = 0;//周围是纯正背景的像素个数
                foreach (int dir in new int[]{1,-1}) {
                    int j = i + dir * biteBias;
                    if (j >= 0 && j < frameData.Length-4)
                    {
                        if (IsBackground(frameData[j + 0], frameData[j + 1] , frameData[j + 2] ,0))
                        {
                            num_nearPixelIsBlack++;
                        }
                    }
                }
                int sumOfColor = green + blue + red;
                if (IsBackground(red, green, blue, (int)(num_nearPixelIsBlack * 250 * _backgroundRemveStrength)))
                {
                    frameData[i + 3] = 0; // 设置为完全透明
                    continue;
                }
            }
        }
        private void DetectBorder(byte[] frameData, int stride)
        {
            if (_backgroundRemovalMode == BackgroundRemovalMode.RemoveBlack) return;
            // 检测并去除黑色边框
            int width = stride / 4; // 每行的像素数
            int height = frameData.Length / stride; // 总高度

            // 检测顶部边框
            for (int y = 0; y < height; y++)
            {
                bool isBlackRow = true;
                for (int x = 0; x < width; x++)
                {
                    int index = (y * stride) + (x * 4);
                    if (frameData[index + 0] + frameData[index + 1] + frameData[index + 2] != 0)
                    {
                        isBlackRow = false;
                        break;
                    }
                }
                if (isBlackRow) _frameBorderTop = y; else break;
            }

            // 检测底部边框
            for (int y = height - 1; y >= 0; y--)
            {
                bool isBlackRow = true;
                for (int x = 0; x < width; x++)
                {
                    int index = (y * stride) + (x * 4);
                    if (frameData[index + 0] + frameData[index + 1] + frameData[index + 2] != 0)
                    {
                        isBlackRow = false;
                        break;
                    }
                }
                if (isBlackRow) _frameBorderBottom = y; else break;
            }

            // 检测左边框
            for (int x = 0; x < width; x++)
            {
                bool isBlackColumn = true;
                for (int y = 0; y < height; y++)
                {
                    int index = (y * stride) + (x * 4);
                    if (frameData[index + 0] + frameData[index + 1] + frameData[index + 2] != 0)
                    {
                        isBlackColumn = false;
                        break;
                    }
                }
                if (isBlackColumn) _frameBorderLeft = x; else break;
            }

            // 检测右边框
            for (int x = width - 1; x >= 0; x--)
            {
                bool isBlackColumn = true;
                for (int y = 0; y < height; y++)
                {
                    int index = (y * stride) + (x * 4);
                    if (frameData[index + 0] + frameData[index + 1] + frameData[index + 2] != 0)
                    {
                        isBlackColumn = false;
                        break;
                    }
                }
                if (isBlackColumn) _frameBorderRight = x; else break;
            }
        }
        private void RemoveBorder(byte[] frameData, int stride)
        {
            if (_backgroundRemovalMode == BackgroundRemovalMode.RemoveBlack) return;
            // 检测并去除黑色边框
            int width = stride / 4; // 每行的像素数
            int height = frameData.Length / stride; // 总高度
            // 设置边框像素为透明
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (y <= _frameBorderTop || y >= _frameBorderBottom || x <= _frameBorderLeft || x >= _frameBorderRight)
                    {
                        int index = (y * stride) + (x * 4);
                        frameData[index + 3] = 0; // 设置为完全透明
                    }
                }
            }
        }
        private bool IsBackground(byte red, byte green, byte blue,int bias)
        {
            switch (_backgroundRemovalMode)
            {
                case BackgroundRemovalMode.None:
                    return false;
                case BackgroundRemovalMode.RemoveBlack:
                    return red + green + blue <= bias;
                case BackgroundRemovalMode.RemoveGreen:
                    //这些数据是我试出来的（
                    return green >= 254 - _backgroundRemveStrength * 200 &&
                           red <= Math.Max(0, green - 180 + _backgroundRemveStrength * 150) && 
                           blue <= Math.Max(0, green - 180) + _backgroundRemveStrength * 150;
            }
            return false;
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

        //================================About Form and Click and Menu================================
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
                //保存右键点击事件,方便缩放
                _LastRightClickMouseEvent = e;
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
            _frameTimer?.Stop();
            Application.Current.Shutdown();
        }

        private void ResizeForm(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //初始化时候会调用这个函数，dame，这是不行的
            if (!hasInited) return;

            if (_originHeight == 0 || _originWidth == 0)
            {
                MessageBox.Show("Initailized Height or Width is 0! That may lead the form to disapear");
            }
            try
            {
                double newScale = e.NewValue;
                double scaleChange = newScale / _formSize;

                System.Windows.Point cursorPositionBefore = _LastRightClickMouseEvent.GetPosition(this);
                this.Left -= cursorPositionBefore.X * (scaleChange - 1);
                this.Top -= cursorPositionBefore.Y * (scaleChange - 1);

                this.Width = newScale * _originWidth;
                this.Height = newScale * _originHeight;
                _formSize = newScale;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Resize Error! {ex.ToString}");
            }
        }
        private void ChangeBRStrength(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _backgroundRemveStrength = e.NewValue;
        }
    }
}