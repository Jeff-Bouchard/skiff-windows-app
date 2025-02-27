﻿using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Position;
using ToastNotifications.Core;
using CustomNotificationsExample.CustomMessage;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Skiff_Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Action UnreadCounterChanged;
        public int UnreadCount { get; private set; }
        public HttpClient HttpClient { get; private set; }

        private string baseURL = "https://app.skiff.com/";
        private Notifier _notifier;
        private TrayController _trayController;
        private MessageProcessor _messageProcessor;
        private PreferencesController _preferencesController;

        private WindowState _prevState;


        public MainWindow()
        {
            InitializeComponent();
            InitializeBrowser();

            _notifier = new Notifier(cfg =>
            {
                cfg.PositionProvider = new PrimaryScreenPositionProvider(
                    corner: Corner.BottomRight,
                    offsetX: 10,
                    offsetY: 10);
                cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                    notificationLifetime: TimeSpan.FromSeconds(15),
                    maximumNotificationCount: MaximumNotificationCount.FromCount(5));
                cfg.Dispatcher = System.Windows.Application.Current.Dispatcher;
                cfg.DisplayOptions.Width = 360;
                cfg.DisplayOptions.TopMost = true;
            });

            HttpClient = new HttpClient();
            HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Skiff-Mail", "1.0"));
            _preferencesController = new PreferencesController(this);
            _trayController = new TrayController(this, _preferencesController);
            _messageProcessor = new MessageProcessor(this);

            StateChanged += OnWindowStateChanged;
            SizeChanged += OnWindowSizeChanged;
            RestoreWindow();
            ApplyWindowPreferences();
        }

        internal void RestoreWindow()
        {
            var windowData = _preferencesController.WindowData;
            if (windowData != null)
            {
                Top = windowData.Top;
                Left = windowData.Left;
                Width = windowData.Width;
                Height = windowData.Height;
                WindowState = windowData.Maximized ? WindowState.Maximized : WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void ApplyWindowPreferences()
        {
            if (_preferencesController.StartMinimized)
            {
                WindowState = WindowState.Minimized;
                if (_preferencesController.MinimizeToTray)
                    Hide();
            }
        }

        private void SaveWindowData()
        {
            var windowData = new WindowData()
            {
                Top = Top,
                Left = Left,
                Width = Width,
                Height = Height,
                Maximized = WindowState == WindowState.Maximized
            };
            _preferencesController.SetWindowPosAndState(windowData);
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
                SaveWindowData();
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState != WindowState.Minimized)
                SaveWindowData();
        }

        internal void ShowToastNotification(string title, string message)
        {
             var options = new MessageOptions { FreezeOnMouseEnter = true };
            _notifier.ShowCustomMessage(title, message, options);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (WindowState != WindowState.Minimized)
                SaveWindowData();

            _notifier.Dispose();
            base.OnClosed(e);
        }

        private async Task InitializeBrowser()
        {
            // When installing the app webview attempts to create a folder with cache folders in root app directory
            // which crashes the app, to fix this we must create the webview with the user data path set to temp where we
            // have permissions
            var userDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\skiff";
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView2.EnsureCoreWebView2Async(env);
            WebView2.Source = new Uri(baseURL);

            WebView2.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView2.CoreWebView2.Settings.IsScriptEnabled = true;
            WebView2.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView2.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            WebView2.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            WebView2.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
            WebView2.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
            WebView2.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // this is needed to allow the webview to communicate with the app
            // right now, only for sending notifications
            WebView2.CoreWebView2.WebMessageReceived += _messageProcessor.CoreWebView2_WebMessageReceived;
            WebView2.CoreWebView2.Settings.IsWebMessageEnabled = true; // Make sure this is set to true

            await WebView2.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.IsSkiffWindowsDesktop = true;");
        }

        private async void WebView2_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            WebView2.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            // In the link is a skiff link open in current window
            if (e.Uri.StartsWith(baseURL))
            {
                // If the link is a skiff link, open in the current window.
                e.Handled = true;
                WebView2.CoreWebView2.Navigate(e.Uri);
            }
            else if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == "http" || uri.Scheme == "https")
                {
                    OpenInDefaultBrowser(e.Uri);
                }
            }
        }

        internal void OpenInDefaultBrowser(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void WebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!e.Uri.StartsWith(baseURL))
            {
                // Do not allow navigation to url's that are not skiff
                e.Cancel = true;

                OpenInDefaultBrowser(e.Uri);
            }
        }

        internal void UpdateUnreadCount(int newTotal)
        {
            UnreadCount = newTotal;
            UnreadCounterChanged?.Invoke();
        }
    }

    [Serializable]
    class WindowData
    {
        public double Top { get; set; }
        public double Left { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }

        public override string ToString()
        {
            return $"{Top} {Left} {Width} {Height} {Maximized}";
        }

        public static WindowData Parse(string strData)
        {
            if (string.IsNullOrEmpty(strData))
                return null;

            string[] values = strData.Split(' ');
            WindowData windowData = new()
            {
                Top = double.Parse(values[0]),
                Left = double.Parse(values[1]),
                Width = double.Parse(values[2]),
                Height = double.Parse(values[3]),
                Maximized = bool.Parse(values[4])
            };

            return windowData;
        }
    }
}
