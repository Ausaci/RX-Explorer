﻿using Microsoft.Toolkit.Uwp.UI.Controls;
using RX_Explorer.Class;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;

namespace RX_Explorer.Dialog
{
    public sealed partial class WhatIsNew : QueueContentDialog
    {
        public WhatIsNew()
        {
            InitializeComponent();
            Loaded += WhatIsNew_Loaded;
        }

        private async void WhatIsNew_Loaded(object sender, RoutedEventArgs e)
        {
            Task MinDelayTask = Task.Delay(1000);

            MarkDown.Text = Globalization.CurrentLanguage switch
            {
                LanguageEnum.Chinese_Simplified => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-Chinese_S.txt"))),
                LanguageEnum.English => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-English.txt"))),
                LanguageEnum.French => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-French.txt"))),
                LanguageEnum.Chinese_Traditional => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-Chinese_T.txt"))),
                LanguageEnum.Spanish => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-Spanish.txt"))),
                LanguageEnum.German => await FileIO.ReadTextAsync(await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/UpdateLog-German.txt"))),
                _ => throw new Exception("Unsupported language")
            };

            await MinDelayTask.ContinueWith((_) => LoadingTip.Visibility = Visibility.Collapsed, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private async void MarkDown_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri(e.Link));
        }
    }
}
