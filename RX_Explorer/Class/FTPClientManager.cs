﻿using RX_Explorer.Dialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;

namespace RX_Explorer.Class
{
    public static class FTPClientManager
    {
        private static readonly List<FTPClientController> ControllerList = new List<FTPClientController>();

        private static readonly SemaphoreSlim Locker = new SemaphoreSlim(1, 1);

        public static async Task<FTPClientController> GetClientControllerAsync(FTPPathAnalysis Analysis)
        {
            await Locker.WaitAsync();

            try
            {
                if (ControllerList.FirstOrDefault((Controller) => Controller.ServerHost == Analysis.Host && Controller.ServerPort == Analysis.Port) is FTPClientController ExistController)
                {
                    if (ExistController.IsAvailable)
                    {
                        return ExistController;
                    }

                    try
                    {
                        if (await ExistController.ConnectAsync())
                        {
                            return ExistController;
                        }
                    }
                    catch (Exception)
                    {
                        //No need to handle this exception
                    }

                    ControllerList.Remove(ExistController);
                    ExistController.Dispose();
                }

                FTPClientController NewClient = await CoreApplication.MainView.CoreWindow.Dispatcher.RunAndWaitAsyncTask(CoreDispatcherPriority.Normal, async () =>
                {
                    FTPCredentialDialog Dialog = new FTPCredentialDialog(Analysis);

                    if (await Dialog.ShowAsync() == ContentDialogResult.Primary)
                    {
                        return Dialog.FtpController;
                    }

                    return null;
                });

                if (NewClient != null)
                {
                    ControllerList.Add(NewClient);
                }

                return NewClient;
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, "Could not get the ftp client as expected");
            }
            finally
            {
                Locker.Release();
            }

            return null;
        }

        public static async Task CloseAllClientAsync()
        {
            if (ControllerList.Count > 0)
            {
                try
                {
                    List<Task> ParallelTask = new List<Task>(ControllerList.Count);

                    foreach (FTPClientController Client in ControllerList)
                    {
                        ParallelTask.Add(Task.Run(() => Client.Dispose()));
                    }

                    await Task.WhenAll(ParallelTask);
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, "Could not disconnect normally from ftp server");
                }
            }
        }
    }
}
