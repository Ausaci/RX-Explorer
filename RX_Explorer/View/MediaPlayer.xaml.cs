﻿using RX_Explorer.Class;
using SharedLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using Windows.ApplicationModel.Core;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace RX_Explorer.View
{
    public sealed partial class MediaPlayer : Page
    {
        private MediaSource Source;
        private CancellationTokenSource Cancellation;

        private readonly Dictionary<string, string> MIMEDictionary = new Dictionary<string, string>
        {
            {".mp3","audio/mpeg" },
            {".flac", "audio/x-flac" },
            {".wma", "audio/x-ms-wma" },
            {".m4a", "audio/mp4a-latm" },
            {".mkv", "video/x-matroska" },
            {".mp4", "video/mp4" },
            {".wmv", "video/x-ms-wmv" },
            {".mov", "video/quicktime" }
        };

        public MediaPlayer()
        {
            InitializeComponent();
            Loaded += MediaPlayer_Loaded;
            Unloaded += MediaPlayer_Unloaded;
        }

        private void MediaPlayer_Unloaded(object sender, RoutedEventArgs e)
        {
            CoreApplication.MainView.CoreWindow.KeyDown -= MediaPlayer_KeyDown;
        }

        private void MediaPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            CoreApplication.MainView.CoreWindow.KeyDown += MediaPlayer_KeyDown;
        }

        private async Task InitializeAsync(FileSystemStorageFile MediaFile, CancellationToken CancelToken)
        {
            TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(true);

            try
            {
                Stream MediaStream = null;
                string TypeString = string.Empty;

                if (MediaFile.Type.Equals(".sle", StringComparison.OrdinalIgnoreCase))
                {
                    Stream Stream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);

                    SLEHeader Header = SLEHeader.GetHeader(Stream);

                    if (Header.Version >= SLEVersion.Version_1_5_0)
                    {
                        MediaStream = new SLEInputStream(Stream, SecureArea.AESKey);
                        TypeString = Path.GetExtension(Header.FileName).ToLower();
                    }
                }
                else
                {
                    MediaStream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);
                    TypeString = MediaFile.Type.ToLower();
                }

                if (CancelToken.IsCancellationRequested)
                {
                    MediaStream?.Dispose();
                }
                else
                {
                    try
                    {
                        if (MediaStream != null)
                        {
                            switch (TypeString)
                            {
                                case ".mp3":
                                case ".flac":
                                case ".wma":
                                case ".m4a":
                                    {
                                        MusicCover.Visibility = Visibility.Visible;

                                        MediaPlaybackItem Item = new MediaPlaybackItem(Source = MediaSource.CreateFromStream(MediaStream.AsRandomAccessStream(), MIMEDictionary[TypeString]));

                                        MediaItemDisplayProperties Props = Item.GetDisplayProperties();
                                        Props.Type = Windows.Media.MediaPlaybackType.Music;
                                        Props.MusicProperties.Title = MediaFile.DisplayName;
                                        Props.MusicProperties.AlbumArtist = await GetArtistAsync(MediaFile);

                                        Item.ApplyDisplayProperties(Props);

                                        if (await GetMusicCoverAsync(MediaFile) is BitmapImage Thumbnail)
                                        {
                                            Cover.Source = Thumbnail;
                                            Cover.Visibility = Visibility.Visible;
                                        }
                                        else
                                        {
                                            Cover.Visibility = Visibility.Collapsed;
                                        }

                                        Display.Text = $"{Globalization.GetString("Media_Tip_Text")} {MediaFile.DisplayName}";
                                        MVControl.Source = Item;

                                        break;
                                    }
                                case ".mkv":
                                case ".wmv":
                                case ".mp4":
                                case ".mov":
                                    {
                                        MusicCover.Visibility = Visibility.Collapsed;

                                        MediaPlaybackItem Item = new MediaPlaybackItem(Source = MediaSource.CreateFromStream(MediaStream.AsRandomAccessStream(), MIMEDictionary[TypeString]));

                                        MediaItemDisplayProperties Props = Item.GetDisplayProperties();
                                        Props.Type = Windows.Media.MediaPlaybackType.Video;
                                        Props.VideoProperties.Title = MediaFile.DisplayName;
                                        Item.ApplyDisplayProperties(Props);

                                        MVControl.Source = Item;

                                        break;
                                    }
                                default:
                                    {
                                        throw new NotSupportedException();
                                    }
                            }

                            await Task.Delay(1000);
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }
                    finally
                    {
                        TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not load media because an exception was threw");

                await new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_CouldNotLoadMedia_Content"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_CloseButton")
                }.ShowAsync();

                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        }

        /// <summary>
        /// 异步获取音乐封面
        /// </summary>
        /// <returns>艺术家名称</returns>
        private async Task<BitmapImage> GetMusicCoverAsync(FileSystemStorageFile MediaFile)
        {
            Stream FStream = null;

            try
            {
                if (MediaFile.Type.Equals(".sle", StringComparison.OrdinalIgnoreCase))
                {
                    Stream Stream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);

                    SLEHeader Header = SLEHeader.GetHeader(Stream);

                    if (Header.Version >= SLEVersion.Version_1_5_0)
                    {
                        FStream = new SLEInputStream(Stream, SecureArea.AESKey);
                    }
                }
                else
                {
                    FStream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);
                }

                if (FStream != null)
                {
                    using (TagLib.File TagFile = TagLib.File.Create(new StreamFileAbstraction(MediaFile.Name, FStream, FStream)))
                    {
                        if ((TagFile.Tag.Pictures?.Length).GetValueOrDefault() > 0)
                        {
                            foreach (IPicture Picture in TagFile.Tag.Pictures)
                            {
                                byte[] ImageData = Picture.Data.Data;

                                if ((ImageData?.Length).GetValueOrDefault() > 0)
                                {
                                    return await Helper.CreateBitmapImageAsync(ImageData);
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get music cover");
                return null;
            }
            finally
            {
                FStream?.Dispose();
            }
        }

        private async Task<string> GetArtistAsync(FileSystemStorageFile MediaFile)
        {
            Stream FStream = null;

            try
            {
                if (MediaFile.Type.Equals(".sle", StringComparison.OrdinalIgnoreCase))
                {
                    Stream Stream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);

                    SLEHeader Header = SLEHeader.GetHeader(Stream);

                    if (Header.Version >= SLEVersion.Version_1_5_0)
                    {
                        FStream = new SLEInputStream(Stream, SecureArea.AESKey);
                    }
                }
                else
                {
                    FStream = await MediaFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);
                }

                if (FStream != null)
                {
                    using (TagLib.File TagFile = TagLib.File.Create(new StreamFileAbstraction(MediaFile.Name, FStream, FStream)))
                    {
                        if (TagFile.Tag.AlbumArtists != null && TagFile.Tag.AlbumArtists.Length != 0)
                        {
                            string Artist = "";

                            if (TagFile.Tag.AlbumArtists.Length == 1)
                            {
                                return TagFile.Tag.AlbumArtists[0];
                            }
                            else
                            {
                                Artist = TagFile.Tag.AlbumArtists[0];
                            }

                            foreach (var item in TagFile.Tag.AlbumArtists)
                            {
                                Artist = Artist + "/" + item;
                            }

                            return Artist;
                        }
                        else
                        {
                            return Globalization.GetString("UnknownText");
                        }
                    }
                }
                else
                {
                    return Globalization.GetString("UnknownText");
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get artist");
                return Globalization.GetString("UnknownText");
            }
            finally
            {
                FStream?.Dispose();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(false);

            Cancellation?.Cancel();
            Cancellation?.Dispose();
            MVControl.MediaPlayer.Pause();
            MVControl.Source = null;
            Cover.Source = null;
            Source?.Dispose();
            Source = null;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            Cancellation = new CancellationTokenSource();

            if (e.Parameter is FileSystemStorageFile MediaFile)
            {
                await InitializeAsync(MediaFile, Cancellation.Token);
            }
        }

        private void MediaPlayer_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            if (args.VirtualKey == Windows.System.VirtualKey.Escape && MVControl.IsFullWindow)
            {
                MVControl.IsFullWindow = false;
            }
        }

        private void MVControl_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (MVControl.ActualHeight - e.GetPosition(MVControl).Y > 100)
            {
                if (MusicCover.Visibility != Visibility.Visible)
                {
                    MVControl.IsFullWindow = !MVControl.IsFullWindow;
                }
            }
        }

        private void MVControl_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (MVControl.ActualHeight - e.GetCurrentPoint(MVControl).Position.Y > 100)
            {
                if (MVControl.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused)
                {
                    MVControl.MediaPlayer.Play();
                }
                else if (MVControl.MediaPlayer.PlaybackSession.CanPause)
                {
                    MVControl.MediaPlayer.Pause();
                }
            }
        }
    }
}
