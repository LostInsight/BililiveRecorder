﻿using Autofac;
using BililiveRecorder.Core;
using BililiveRecorder.FlvProcessor;
using CommandLine;
using Hardcodet.Wpf.TaskbarNotification;
using NLog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BililiveRecorder.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private const int MAX_LOG_ROW = 25;
        private const string LAST_WORK_DIR_FILE = "lastworkdir";

        private IContainer Container { get; set; }
        private ILifetimeScope RootScope { get; set; }

        public IRecorder Recorder { get; set; }
        public ObservableCollection<string> Logs { get; set; } =
            new ObservableCollection<string>()
            {
                "当前版本：" + BuildInfo.Version,
                "网站： https://rec.danmuji.org",
                "QQ群： 689636812",
                "",
                "【公告】问卷调查",
                "如果有空的话麻烦填一下问卷调查，问卷统计结果将会影响到增删什么功能的决定。",
                "问卷共 4 页，大约 25 题，预计用时 7 分钟。右键点击链接可以复制。谢谢。",
                "https://wj.qq.com/s2/3740592/5e24",
                "",
            };

        public static void AddLog(string message) => _AddLog?.Invoke(message);
        private static Action<string> _AddLog;

        public MainWindow()
        {

            _AddLog = (message) =>
                Log.Dispatcher.BeginInvoke(
                    DispatcherPriority.DataBind,
                    new Action(() => { Logs.Add(message); while (Logs.Count > MAX_LOG_ROW) { Logs.RemoveAt(0); } })
                    );

            var builder = new ContainerBuilder();
            builder.RegisterModule<FlvProcessorModule>();
            builder.RegisterModule<CoreModule>();
            Container = builder.Build();
            RootScope = Container.BeginLifetimeScope("recorder_root");

            Recorder = RootScope.Resolve<IRecorder>();

            InitializeComponent();

            DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Title += " " + BuildInfo.Version + " " + BuildInfo.HeadShaShort;

            bool skip_ui = false;
            string workdir = string.Empty;

            CommandLineOption commandLineOption = null;
            Parser.Default
                .ParseArguments<CommandLineOption>(Environment.GetCommandLineArgs())
                .WithParsed(x => commandLineOption = x);

            if (commandLineOption?.WorkDirectory != null)
            {
                skip_ui = true;
                workdir = commandLineOption.WorkDirectory;
            }

            if (!skip_ui)
            {
                try
                {
                    workdir = File.ReadAllText(LAST_WORK_DIR_FILE);
                }
                catch (Exception) { }
                var wdw = new WorkDirectoryWindow()
                {
                    Owner = this,
                    WorkPath = workdir,
                };

                if (wdw.ShowDialog() == true)
                {
                    workdir = wdw.WorkPath;
                }
                else
                {
                    Environment.Exit(-1);
                    return;
                }
            }

            if (!Recorder.Initialize(workdir))
            {
                if (!skip_ui)
                {
                    MessageBox.Show("初始化错误", "录播姬", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                Environment.Exit(-2);
                return;
            }

            NotifyIcon.Visibility = Visibility.Visible;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (new TimedMessageBox { Title = "关闭录播姬？", Message = "确定要关闭录播姬吗？", CountDown = 10 }.ShowDialog() == true)
            {
                _AddLog = null;
                Recorder.Shutdown();
                try
                {
                    File.WriteAllText(LAST_WORK_DIR_FILE, Recorder.Config.WorkDirectory);
                }
                catch (Exception) { }
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void TextBlock_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                Clipboard.SetText(textBlock.Text);
            }
        }

        /// <summary>
        /// 触发回放剪辑
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Clip_Click(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Task.Run(() => rr.Clip());
        }

        /// <summary>
        /// 启用自动录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EnableAutoRec(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Task.Run(() => rr.Start());
        }

        /// <summary>
        /// 禁用自动录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DisableAutoRec(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Task.Run(() => rr.Stop());
        }

        /// <summary>
        /// 手动触发尝试录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TriggerRec(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Task.Run(() => rr.StartRecord());
        }

        /// <summary>
        /// 切断当前录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CutRec(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Task.Run(() => rr.StopRecord());
        }

        /// <summary>
        /// 删除当前房间
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void RemoveRecRoom(object sender, RoutedEventArgs e)
        {
            var rr = _GetSenderAsRecordedRoom(sender);
            if (rr == null)
            {
                return;
            }

            Recorder.RemoveRoom(rr);
        }

        /// <summary>
        /// 全部直播间启用自动录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EnableAllAutoRec(object sender, RoutedEventArgs e)
        {
            Recorder.ToList().ForEach(rr => Task.Run(() => rr.Start()));
        }

        /// <summary>
        /// 全部直播间禁用自动录制
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DisableAllAutoRec(object sender, RoutedEventArgs e)
        {
            Recorder.ToList().ForEach(rr => Task.Run(() => rr.Stop()));
        }

        /// <summary>
        /// 添加直播间
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddRoomidButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(AddRoomidTextBox.Text, out int roomid))
            {
                if (roomid > 0)
                {
                    Recorder.AddRoom(roomid);
                }
                else
                {
                    logger.Info("房间号是大于0的数字！");
                }
            }
            else
            {
                logger.Info("房间号是数字！");
            }
            AddRoomidTextBox.Text = string.Empty;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowSettingsWindow();
        }

        private void ShowSettingsWindow()
        {
            var sw = new SettingsWindow(this, Recorder.Config);
            if (sw.ShowDialog() == true)
            {
                sw.Config.CopyPropertiesTo(Recorder.Config);
            }
        }

        private IRecordedRoom _GetSenderAsRecordedRoom(object sender) => (sender as Button)?.DataContext as IRecordedRoom;

        private void Taskbar_Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                NotifyIcon.ShowBalloonTip("B站录播姬", "录播姬已最小化到托盘，左键单击图标恢复界面。", BalloonIcon.Info);
            }
        }

        private void Taskbar_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Topmost ^= true;
            Topmost ^= true;
            Focus();
        }
    }
}
