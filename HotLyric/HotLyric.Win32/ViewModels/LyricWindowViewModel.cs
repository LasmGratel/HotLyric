﻿using HotLyric.Win32.Controls;
using HotLyric.Win32.Models;
using HotLyric.Win32.Utils;
using HotLyric.Win32.Utils.SystemMediaTransportControls;
using HotLyric.Win32.Utils.WindowBackgrounds;
using HotLyric.Win32.Views;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace HotLyric.Win32.ViewModels
{
    public partial class LyricWindowViewModel : ObservableObject
    {
        private readonly SMTCFactory smtcFactory;
        private readonly SettingsWindowViewModel settingVm;
        private SMTCManager? smtcManager;
        private SMTCSession[]? sessions;
        private Brush hitTestBrush = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255));

        public LyricWindowViewModel(SMTCFactory smtcFactory, SettingsWindowViewModel settingVm)
        {
            this.smtcFactory = smtcFactory;
            this.settingVm = settingVm;
            InitSessions();

            settingVm.SettingsChanged += SettingVm_SettingsChanged;
            UpdateSettings();
        }


        private SMTCSessionModel? selectedSession;

        private bool isTitleVisible;
        private bool isMouseOver;
        private bool alwaysShowBackground;
        private HorizontalAlignment lyricHorizontalAlignment;
        private bool isTransparent;
        private bool showShadow;
        private bool textStrokeEnabled;

        private ObservableCollection<SMTCSessionModel>? sessionModels;

        private MediaModel? mediaModel = MediaModel.CreateEmptyMedia();
        private bool isMinimized;
        private CancellationTokenSource? showBgCts;
        private bool sessionInited;
        private bool karaokeEnabled;
        private ICommand? openCurrentSessionAppCmd;
        private LyricThemeView? lyricTheme;
        private bool backgroundTransientVisible;
        private WindowBackgroundHelper? backgroundHelper;

        private string lyricPlaceholderText = "";
        private string lyricNextLinePlaceholderText = "";

        public SettingsWindowViewModel SettingViewModel => settingVm;

        public WindowBackgroundHelper? BackgroundHelper
        {
            get => backgroundHelper;
            set => SetProperty(ref backgroundHelper, value);
        }

        public bool IsTitleVisible
        {
            get => isTitleVisible;
            set => SetProperty(ref isTitleVisible, value);
        }

        public bool IsTitleButtonVisible => !IsTransparent;

        public bool IsMouseOver
        {
            get => isMouseOver;
            set
            {
                if (SetProperty(ref isMouseOver, value))
                {
                    OnPropertyChanged(nameof(IsBackgroundVisible));
                    OnPropertyChanged(nameof(IsTitleButtonVisible));
                    OnPropertyChanged(nameof(LyricOpacity));
                }

                showBgCts?.Cancel();
                showBgCts = null;
            }
        }

        public bool AlwaysShowBackground
        {
            get => alwaysShowBackground;
            private set
            {
                if (SetProperty(ref alwaysShowBackground, value))
                {
                    OnPropertyChanged(nameof(IsBackgroundVisible));
                    OnPropertyChanged(nameof(IsTitleButtonVisible));
                    OnPropertyChanged(nameof(LyricOpacity));
                }

                showBgCts?.Cancel();
                showBgCts = null;
            }
        }

        public HorizontalAlignment LyricHorizontalAlignment
        {
            get => lyricHorizontalAlignment;
            private set => SetProperty(ref lyricHorizontalAlignment, value);
        }

        public bool IsBackgroundVisible => !ActualMinimized && (IsMouseOver || AlwaysShowBackground);

        public bool IsMinimized
        {
            get => isMinimized;
            set
            {
                if (SetProperty(ref isMinimized, value))
                {
                    OnPropertyChanged(nameof(ActualMinimized));
                    OnPropertyChanged(nameof(IsBackgroundVisible));
                    OnPropertyChanged(nameof(LyricOpacity));

                    if (!isMinimized && !IsBackgroundVisible)
                    {
                        ShowBackgroundTransient(TimeSpan.FromSeconds(2));
                    }
                }
            }
        }

        public bool ActualMinimized => SelectedSession == null || IsMinimized || MediaModel == null || MediaModel.IsEmptyLyric;

        public bool IsTransparent
        {
            get => isTransparent;
            private set
            {
                if (SetProperty(ref isTransparent, value))
                {
                    OnPropertyChanged(nameof(IsTitleButtonVisible));
                }
            }
        }

        public bool ShowShadow
        {
            get => showShadow;
            private set
            {
                if (SetProperty(ref showShadow, value))
                {
                    ShowBackgroundTransient(TimeSpan.FromSeconds(2));
                }
            }
        }

        public bool TextStrokeEnabled
        {
            get => textStrokeEnabled;
            private set => SetProperty(ref textStrokeEnabled, value);
        }

        public bool ActualKaraokeEnabled => KaraokeEnabled && MediaModel?.HasLyric == true;

        public bool KaraokeEnabled
        {
            get => karaokeEnabled;
            private set
            {
                if (SetProperty(ref karaokeEnabled, value))
                {
                    OnPropertyChanged(nameof(ActualKaraokeEnabled));
                }
            }
        }

        public double LyricOpacity => IsBackgroundVisible ? 1d : settingVm.LyricOpacity;

        public ObservableCollection<SMTCSessionModel>? SessionModels
        {
            get => sessionModels;
            set => SetProperty(ref sessionModels, value);
        }

        public MediaModel? MediaModel
        {
            get => mediaModel;
            set
            {
                var oldModel = mediaModel;
                if (SetProperty(ref mediaModel, value))
                {
                    OnPropertyChanged(nameof(ActualMinimized));
                    OnPropertyChanged(nameof(IsBackgroundVisible));
                    OnPropertyChanged(nameof(ActualKaraokeEnabled));
                    OnPropertyChanged(nameof(LyricOpacity));

                    UpdateLyricPlaceholder();

                    App.Current.NotifyIcon?.UpdateToolTipText();

                    if (oldModel != null)
                    {
                        WeakEventManager<MediaModel, PropertyChangedEventArgs>.RemoveHandler(oldModel, "PropertyChanged", new EventHandler<PropertyChangedEventArgs>(MediaModel_PropertyChanged));
                    }

                    if (mediaModel != null)
                    {
                        WeakEventManager<MediaModel, PropertyChangedEventArgs>.AddHandler(mediaModel, "PropertyChanged", new EventHandler<PropertyChangedEventArgs>(MediaModel_PropertyChanged));
                    }
                }
            }
        }

        private void MediaModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateLyricPlaceholder();
            OnPropertyChanged(nameof(ActualKaraokeEnabled));
            App.Current.NotifyIcon?.UpdateToolTipText();
        }

        public SMTCSessionModel? SelectedSession
        {
            get => selectedSession;
            set
            {
                var old = selectedSession;
                var oldId = old?.AppTitle;
                if (SetProperty(ref selectedSession, value))
                {
                    if (old != null)
                    {
                        old.MediaChanged -= SelectedSession_MediaChanged;
                    }

                    MediaModel? model;

                    if (selectedSession != null)
                    {
                        selectedSession.MediaChanged += SelectedSession_MediaChanged;
                        model = selectedSession?.CreateMediaModel();
                    }
                    else
                    {
                        model = MediaModel.CreateEmptyMedia();
                    }

                    if (MediaModel != model)
                    {
                        MediaModel?.Cancel();
                        MediaModel = model;
                        MediaModel?.StartLoad();
                    }

                }
                IsTitleVisible = SelectedSession != null;
                if (oldId != selectedSession?.AppTitle && !IsBackgroundVisible)
                {
                    ShowBackgroundTransient(TimeSpan.FromSeconds(2));
                }
                OnPropertyChanged(nameof(ActualMinimized));
                OnPropertyChanged(nameof(IsBackgroundVisible));
                OnPropertyChanged(nameof(LyricOpacity));
                App.Current.NotifyIcon?.UpdateToolTipText();
            }
        }

        //public SecondRowType SecondRowType
        //{
        //    get => secondRowType;
        //    private set => SetProperty(ref secondRowType, value);
        //}

        public ICommand OpenCurrentSessionAppCmd => openCurrentSessionAppCmd ?? (openCurrentSessionAppCmd = new AsyncRelayCommand(async () =>
        {
            var curSessionAUMID = SelectedSession?.Session?.AppUserModelId;
            if (string.IsNullOrEmpty(curSessionAUMID)) return;

            var package = await ApplicationHelper.TryGetPackageFromAppUserModelIdAsync(curSessionAUMID);
            var pfn = package?.Id?.FamilyName;

            if (!string.IsNullOrEmpty(pfn))
            {
                await ApplicationHelper.TryLaunchAppAsync(pfn);
            }
        }));

        public bool HasMoreSession => sessions != null && sessions.Length > 1;

        public LyricThemeView? LyricTheme
        {
            get => lyricTheme;
            private set
            {
                if (SetProperty(ref lyricTheme, value))
                {
                    ShowBackgroundTransient(TimeSpan.FromSeconds(3));
                }
            }
        }

        //public FontFamily? FontFamily
        //{
        //    get => fontFamily;
        //    private set => SetProperty(ref fontFamily, value);
        //}


        #region SMTC Session

        private async void InitSessions()
        {
            smtcManager = await smtcFactory.GetManagerAsync();
            if (smtcManager != null)
            {
                sessions = smtcManager.Sessions.ToArray();

                CommandLineArgsHelper.ActivateMainInstanceEventReceived += CommandLineArgsHelper_ActivateMainInstanceEventReceived;

                UpdateSesions();
                smtcManager.SessionsChanged += SmtcManager_SessionsChanged;
            }
        }


        private void CommandLineArgsHelper_ActivateMainInstanceEventReceived(object? sender, EventArgs e)
        {
            UpdateSesions();
        }

        private void SelectedSession_MediaChanged(object? sender, EventArgs e)
        {
            var model = SelectedSession?.CreateMediaModel();
            if (MediaModel != model)
            {
                MediaModel?.Cancel();
                MediaModel = model;
                MediaModel?.StartLoad();
            }
        }

        private void SmtcManager_SessionsChanged(object? sender, EventArgs e)
        {
            if (smtcManager != null)
            {
                var oldSessions = sessions;
                sessions = smtcManager.Sessions.ToArray();

                UpdateSesions();

                if (oldSessions != null)
                {
                    foreach (var session in oldSessions)
                    {
                        session.Dispose();
                    }
                }
            }
        }

        private SMTCSession? GetNamedSession(string? appId)
        {
            if (sessions == null || sessions.Length == 0) return null;

            if (string.IsNullOrEmpty(appId)) return null;

            var prefix = appId.Substring(0, appId.IndexOf("_") + 1);
            if (string.IsNullOrEmpty(prefix)) return null;

            foreach (var session in sessions)
            {
                if (session.AppUserModelId?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return session;
                }
            }

            return null;
        }

        private async void UpdateSesions()
        {
            string? from = "";
            if (CommandLineArgsHelper.HasLaunchParameters)
            {
                from = CommandLineArgsHelper.LaunchFromPackageFamilyName;

                // 从参数启动时不弹出启动app的窗口
                sessionInited = true;
            }

            var lastSelectedAppId = SelectedSession?.Session.AppUserModelId ?? "";
            if (smtcManager != null)
            {
                SMTCSession? curSession = GetNamedSession(from);

                if (curSession != null)
                {
                    // 启动参数已消费
                    CommandLineArgsHelper.LaunchFromPackageFamilyName = null;
                }
                else
                {
                    curSession = GetNamedSession(lastSelectedAppId) ?? smtcManager.CurrentSession;
                }

                lastSelectedAppId = curSession?.AppUserModelId ?? string.Empty;

                var models = await Task.WhenAll(sessions.Select(async c => await SMTCSessionModel.CreateAsync(c)));

                if (models != null)
                {
                    SessionModels = new ObservableCollection<SMTCSessionModel>(models!);
                }
                else
                {
                    SessionModels = null;
                }

                DispatcherHelper.UIDispatcher?.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    SelectedSession = SessionModels?.FirstOrDefault(c => c.Session.AppUserModelId == lastSelectedAppId)
                        ?? SessionModels?.FirstOrDefault();

                    if (!sessionInited)
                    {
                        if (SelectedSession == null)
                        {
                            settingVm.ShowLauncherWindow();
                        }
                    }
                    sessionInited = true;

                    OnPropertyChanged(nameof(HasMoreSession));
                }));
            }
        }

        #endregion SMTC Session

        public bool BackgroundTransientVisible
        {
            get => backgroundTransientVisible;
            private set => SetProperty(ref backgroundTransientVisible, value);
        }

        private void SettingVm_SettingsChanged(object? sender, EventArgs e)
        {
            UpdateSettings();
        }

        public string LyricPlaceholderText
        {
            get => lyricPlaceholderText;
            private set => SetProperty(ref lyricPlaceholderText, value);
        }

        public string LyricNextLinePlaceholderText
        {
            get => lyricNextLinePlaceholderText;
            private set => SetProperty(ref lyricNextLinePlaceholderText, value);
        }

        private void UpdateLyricPlaceholder()
        {
            LyricPlaceholderText = MediaModel?.Name ?? "";
            LyricNextLinePlaceholderText = MediaModel?.Artist ?? "";
        }

        private void UpdateSettings()
        {
            IsTransparent = settingVm.WindowTransparent;
            //SecondRowType = settingVm.SecondRowType;
            KaraokeEnabled = settingVm.KaraokeEnabled;
            LyricHorizontalAlignment = settingVm.LyricHorizontalAlignment;

            AlwaysShowBackground = settingVm.AlwaysShowBackground;
            ShowShadow = settingVm.ShowShadow;
            TextStrokeEnabled = settingVm.TextStrokeEnabled;

            LyricTheme = settingVm.CurrentTheme;

            OnPropertyChanged(nameof(LyricOpacity));
        }

        public async void ShowBackgroundTransient(TimeSpan time)
        {
            if (ActualMinimized || AlwaysShowBackground) return;
            IsMouseOver = true;
            var cts = new CancellationTokenSource();

            showBgCts?.Cancel();
            showBgCts = cts;

            try
            {
                await Task.Yield();
                BackgroundTransientVisible = true;

                await Task.Delay(time, cts.Token);
                IsMouseOver = false;
            }
            catch { }
            BackgroundTransientVisible = false;
        }
    }
}
