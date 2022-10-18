﻿using RX_Explorer.Class;
using RX_Explorer.Dialog;
using SharedLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace RX_Explorer.View
{
    public sealed partial class PdfReader : Page
    {
        private readonly ObservableCollection<BitmapImage> PdfCollection = new ObservableCollection<BitmapImage>();
        private readonly SynchronizedCollection<int> LoadTable = new SynchronizedCollection<int>();

        private PdfDocument Pdf;
        private Stream PdfStream;
        private int LastPageIndex;
        private CancellationTokenSource Cancellation;
        private double OriginHorizonOffset;
        private double OriginVerticalOffset;
        private Point OriginMousePosition;
        private volatile short DelaySelectionChangeCount;
        private readonly PointerEventHandler PointerWheelChangedEventHandler;
        private int ZoomFactor;

        public PdfReader()
        {
            InitializeComponent();

            Flip.SelectionChanged += Flip_SelectionChanged_TaskOne;
            Flip.SelectionChanged += Flip_SelectionChanged_TaskTwo;

            PointerWheelChangedEventHandler = new PointerEventHandler(Page_PointerWheelChanged);

            if (ApplicationData.Current.LocalSettings.Values["PdfPanelHorizontal"] is bool IsHorizontal)
            {
                if (IsHorizontal)
                {
                    Flip.ItemsPanel = HorizontalPanel;
                    PanelToggle.IsChecked = true;
                }
                else
                {
                    Flip.ItemsPanel = VerticalPanel;
                    PanelToggle.IsChecked = false;
                }
            }
            else
            {
                Flip.ItemsPanel = VerticalPanel;
                PanelToggle.IsChecked = false;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            AddHandler(PointerWheelChangedEvent, PointerWheelChangedEventHandler, true);

            Cancellation = new CancellationTokenSource();

            if (e.Parameter is FileSystemStorageFile Parameters)
            {
                LastPageIndex = 0;
                ZoomFactor = 100;
                FileNameDisplay.Text = Parameters.Name;

                await InitializeAsync(Parameters, Cancellation.Token);
            }
        }

        private async Task InitializeAsync(FileSystemStorageFile PdfFile, CancellationToken CancelToken)
        {
            TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(true);

            try
            {
                switch (PdfFile.Type.ToLower())
                {
                    case ".sle":
                        {
                            Stream Stream = await PdfFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);
                            SLEInputStream SLEStream = new SLEInputStream(Stream, new UTF8Encoding(false), SecureArea.AESKey);

                            if (SLEStream.Header.Core.Version >= SLEVersion.SLE150 && Path.GetExtension(SLEStream.Header.Core.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                PdfStream = SLEStream;
                            }
                            else
                            {
                                Stream.Dispose();
                                SLEStream.Dispose();
                                throw new NotSupportedException();
                            }

                            break;
                        }
                    case ".pdf":
                        {
                            PdfStream = await PdfFile.GetStreamFromFileAsync(AccessMode.Read, OptimizeOption.RandomAccess);
                            break;
                        }
                    default:
                        {
                            throw new NotSupportedException();
                        }
                }

                if (CancelToken.IsCancellationRequested)
                {
                    PdfStream.Dispose();
                }
                else
                {
                    try
                    {
                        try
                        {
                            Pdf = await PdfDocument.LoadFromStreamAsync(PdfStream.AsRandomAccessStream());
                        }
                        catch (Exception)
                        {
                            PdfPasswordDialog Dialog = new PdfPasswordDialog();

                            if (await Dialog.ShowAsync() == ContentDialogResult.Primary)
                            {
                                Pdf = await PdfDocument.LoadFromStreamAsync(PdfStream.AsRandomAccessStream(), Dialog.Password);
                            }
                            else
                            {
                                if (Frame.CanGoBack)
                                {
                                    Frame.GoBack();
                                }

                                return;
                            }
                        }

                        NumIndicator.Text = Convert.ToString(Pdf.PageCount);
                        TextBoxControl.Text = "1";

                        IEnumerable<int> InitRange = Enumerable.Range(0, Convert.ToInt32(Pdf.PageCount));

                        PdfCollection.AddRange(InitRange.Select((_) => new BitmapImage()));
                        LoadTable.AddRange(InitRange);

                        Flip.ItemsSource = PdfCollection;

                        await Task.WhenAll(JumpToPageIndexAsync(0, Cancellation.Token), Task.Delay(1000));
                    }
                    finally
                    {
                        TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not open .pdf file");

                await new QueueContentDialog
                {
                    Title = Globalization.GetString("Common_Dialog_ErrorTitle"),
                    Content = Globalization.GetString("QueueDialog_PDFOpenFailure"),
                    CloseButtonText = Globalization.GetString("Common_Dialog_GoBack")
                }.ShowAsync();

                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(false);

            RemoveHandler(PointerWheelChangedEvent, PointerWheelChangedEventHandler);

            Cancellation?.Cancel();
            Cancellation?.Dispose();

            LoadTable.Clear();
            PdfCollection.Clear();

            PdfStream?.Dispose();
        }

        private async Task JumpToPageIndexAsync(uint Index, CancellationToken CancelToken = default)
        {
            int IndexINT = Convert.ToInt32(Index);

            int LowIndex = Math.Max(IndexINT - 4, 0);
            int HighIndex = Math.Min(IndexINT + 4, Convert.ToInt32(Pdf.PageCount) - 1);

            for (int LoadIndex = LowIndex; LoadIndex <= HighIndex && !CancelToken.IsCancellationRequested; LoadIndex++)
            {
                if (LoadTable.Remove(LoadIndex))
                {
                    using (PdfPage Page = Pdf.GetPage(Convert.ToUInt32(LoadIndex)))
                    using (InMemoryRandomAccessStream PageStream = new InMemoryRandomAccessStream())
                    {
                        await Page.RenderToStreamAsync(PageStream, new PdfPageRenderOptions
                        {
                            DestinationHeight = Convert.ToUInt32(Page.Size.Height * 1.5),
                            DestinationWidth = Convert.ToUInt32(Page.Size.Width * 1.5)
                        });

                        if (PdfCollection.Count > LowIndex)
                        {
                            await PdfCollection[Convert.ToInt32(LoadIndex)].SetSourceAsync(PageStream);
                        }
                    }
                }
            }

            if (!CancelToken.IsCancellationRequested)
            {
                LastPageIndex = IndexINT;
                Flip.SelectedIndex = IndexINT;
            }
        }

        private void Flip_SelectionChanged_TaskOne(object sender, SelectionChangedEventArgs e)
        {
            int CurrentIndex = Flip.SelectedIndex;

            TextBoxControl.Text = Convert.ToString(CurrentIndex + 1);

            if (!PanelToggle.IsChecked.GetValueOrDefault())
            {
                if (CurrentIndex >= 0 && CurrentIndex < PdfCollection.Count)
                {
                    if (Flip.ContainerFromIndex(CurrentIndex)?.FindChildOfName<ScrollViewer>("ScrollViewerMain") is ScrollViewer Viewer)
                    {
                        if (PdfCollection.IndexOf(e.AddedItems.Cast<BitmapImage>().FirstOrDefault()) > PdfCollection.IndexOf(e.RemovedItems.Cast<BitmapImage>().FirstOrDefault()))
                        {
                            Viewer.ChangeView(0, 0, ZoomFactor / 100f, true);
                        }
                        else
                        {
                            Viewer.ChangeView(0, Viewer.ScrollableHeight, ZoomFactor / 100f, true);
                        }
                    }
                }
            }
        }

        private async void Flip_SelectionChanged_TaskTwo(object sender, SelectionChangedEventArgs e)
        {
            int CurrentIndex = Flip.SelectedIndex;

            int CurrentLoading = Interlocked.Exchange(ref LastPageIndex, CurrentIndex) < CurrentIndex
                                  ? Convert.ToInt32(Math.Min(CurrentIndex + 4, Pdf.PageCount - 1))
                                  : Convert.ToInt32(Math.Max(CurrentIndex - 4, 0));

            try
            {
                if (LoadTable.Remove(CurrentLoading))
                {
                    using (PdfPage Page = Pdf.GetPage(Convert.ToUInt32(CurrentLoading)))
                    using (InMemoryRandomAccessStream PageStream = new InMemoryRandomAccessStream())
                    {
                        await Page.RenderToStreamAsync(PageStream, new PdfPageRenderOptions
                        {
                            DestinationHeight = Convert.ToUInt32(Page.Size.Height * 1.5),
                            DestinationWidth = Convert.ToUInt32(Page.Size.Width * 1.5)
                        });

                        if (!Cancellation.IsCancellationRequested)
                        {
                            await PdfCollection[CurrentLoading].SetSourceAsync(PageStream);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not load the page on selection changed");
            }
        }

        private void ScrollViewerMain_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType != PointerDeviceType.Touch)
            {
                if (sender is ScrollViewer Viewer)
                {
                    Point TapPoint = e.GetPosition(Viewer);

                    if (Math.Abs(Viewer.ZoomFactor - 1f) < 1E-6)
                    {
                        ZoomFactor = 200;
                        Viewer.ChangeView(TapPoint.X, TapPoint.Y, 2f);
                    }
                    else
                    {
                        ZoomFactor = 100;
                        Viewer.ChangeView(null, null, 1f);
                    }
                }
            }
        }

        private void ScrollViewerMain_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is ScrollViewer Viewer)
            {
                if (Viewer.ZoomFactor != 1 && e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
                {
                    PointerPoint Point = e.GetCurrentPoint(Viewer);

                    if (Point.Properties.IsLeftButtonPressed)
                    {
                        Viewer.ChangeView(OriginHorizonOffset + (OriginMousePosition.X - Point.Position.X), OriginVerticalOffset + (OriginMousePosition.Y - Point.Position.Y), null);
                    }
                }
            }
        }

        private void ScrollViewerMain_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
            {
                if (sender is ScrollViewer Viewer)
                {
                    if (Viewer.ZoomFactor > 1f)
                    {
                        PointerPoint Point = e.GetCurrentPoint(Viewer);

                        if (Point.Properties.IsLeftButtonPressed)
                        {
                            OriginMousePosition = Point.Position;
                            OriginHorizonOffset = Viewer.HorizontalOffset;
                            OriginVerticalOffset = Viewer.VerticalOffset;

                            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Hand, 0);
                        }
                    }
                }
            }
        }

        private void ScrollViewerMain_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            int Mod = ZoomFactor % 10;
            int NextFactor = ZoomFactor + (Mod > 0 ? (10 - Mod) : 10);

            if (NextFactor <= 500)
            {
                ZoomFactor = NextFactor;
                ZoomFactorDisplay.Text = $"{NextFactor}%";

                if (Flip.ContainerFromIndex(Flip.SelectedIndex).FindChildOfName<ScrollViewer>("ScrollViewerMain") is ScrollViewer Viewer)
                {
                    Viewer.ChangeView(null, null, NextFactor / 100f);
                }
            }
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            int Mod = ZoomFactor % 10;
            int NextFactor = ZoomFactor - (Mod > 0 ? Mod : 10);

            if (NextFactor >= 50)
            {
                ZoomFactor = NextFactor;
                ZoomFactorDisplay.Text = $"{NextFactor}%";

                if (Flip.ContainerFromIndex(Flip.SelectedIndex).FindChildOfName<ScrollViewer>("ScrollViewerMain") is ScrollViewer Viewer)
                {
                    Viewer.ChangeView(null, null, NextFactor / 100f);
                }
            }
        }

        private void PanelToggle_Checked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["PdfPanelHorizontal"] = true;

            int CurrentIndex = Flip.SelectedIndex;

            Flip.SelectionChanged -= Flip_SelectionChanged_TaskOne;
            Flip.SelectionChanged -= Flip_SelectionChanged_TaskTwo;
            Flip.SelectedIndex = -1;
            Flip.ItemsPanel = HorizontalPanel;
            Flip.SelectedIndex = CurrentIndex;
            Flip.SelectionChanged += Flip_SelectionChanged_TaskOne;
            Flip.SelectionChanged += Flip_SelectionChanged_TaskTwo;
        }

        private void PanelToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["PdfPanelHorizontal"] = false;

            int CurrentIndex = Flip.SelectedIndex;

            Flip.SelectionChanged -= Flip_SelectionChanged_TaskOne;
            Flip.SelectionChanged -= Flip_SelectionChanged_TaskTwo;
            Flip.SelectedIndex = -1;
            Flip.ItemsPanel = VerticalPanel;
            Flip.SelectedIndex = CurrentIndex;
            Flip.SelectionChanged += Flip_SelectionChanged_TaskOne;
            Flip.SelectionChanged += Flip_SelectionChanged_TaskTwo;
        }

        private void Flyout_Opening(object sender, object e)
        {
            if (Flip.ContainerFromIndex(Flip.SelectedIndex).FindChildOfName<ScrollViewer>("ScrollViewerMain") is ScrollViewer Viewer)
            {
                ZoomFactor = Convert.ToInt32(Viewer.ZoomFactor * 100);
                ZoomFactorDisplay.Text = $"{ZoomFactor}%";
            }
        }

        private async void TextBoxControl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (uint.TryParse(TextBoxControl.Text, out uint PageNum))
            {
                if (PageNum > 0 && PageNum <= PdfCollection.Count)
                {
                    if (PageNum != Flip.SelectedIndex + 1)
                    {
                        TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(true);

                        await Task.WhenAll(JumpToPageIndexAsync(PageNum - 1, Cancellation.Token), Task.Delay(1000));

                        TabViewContainer.Current.CurrentTabRenderer?.SetLoadingTipsStatus(false);
                    }
                }
                else
                {
                    TextBoxControl.Text = Convert.ToString(Flip.SelectedIndex + 1);
                }
            }
            else
            {
                TextBoxControl.Text = Convert.ToString(Flip.SelectedIndex + 1);
            }
        }

        private void TextBoxControl_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Flip.Focus(FocusState.Programmatic);
            }
        }

        private void Page_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!PanelToggle.IsChecked.GetValueOrDefault())
            {
                if (Flip.ContainerFromIndex(Flip.SelectedIndex).FindChildOfName<ScrollViewer>("ScrollViewerMain") is ScrollViewer Viewer)
                {
                    if (Viewer.ZoomFactor > 1f)
                    {
                        int Delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;

                        if (Delta > 0)
                        {
                            if (Viewer.VerticalOffset == 0 && Flip.SelectedIndex > 0)
                            {
                                if (++DelaySelectionChangeCount > 1)
                                {
                                    DelaySelectionChangeCount = 0;
                                    Flip.SelectedIndex--;
                                }
                            }
                        }
                        else
                        {
                            if (Viewer.VerticalOffset == Viewer.ScrollableHeight && Flip.SelectedIndex < PdfCollection.Count - 1)
                            {
                                if (++DelaySelectionChangeCount > 1)
                                {
                                    DelaySelectionChangeCount = 0;
                                    Flip.SelectedIndex++;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
