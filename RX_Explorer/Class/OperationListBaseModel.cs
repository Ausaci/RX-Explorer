﻿using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace RX_Explorer.Class
{
    public abstract class OperationListBaseModel : INotifyPropertyChanged, IDisposable
    {
        public abstract string OperationKindText { get; }

        public abstract string FromDescription { get; }

        public abstract string ToDescription { get; }

        public int Progress { get; private set; }
        public string ProgressSpeed { get; private set; }

        public string RemainingTime { get; private set; }

        public string ActionButton1Content { get; private set; }

        public string ActionButton2Content { get; private set; }

        public string ActionButton3Content { get; private set; }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case OperationStatus.Waiting:
                        {
                            return $"{Globalization.GetString("TaskList_Task_Status_Waiting")}...";
                        }
                    case OperationStatus.Preparing:
                        {
                            return $"{Globalization.GetString("TaskList_Task_Status_Preparing")}...";
                        }
                    case OperationStatus.Processing:
                        {
                            return $"{Globalization.GetString("TaskList_Task_Status_Processing")}...";
                        }
                    case OperationStatus.NeedAttention:
                        {
                            return string.IsNullOrEmpty(AdditionalMessage) ? Globalization.GetString("TaskList_Task_Status_NeedAttention") : $"{Globalization.GetString("TaskList_Task_Status_NeedAttention")}: {AdditionalMessage}";
                        }
                    case OperationStatus.Error:
                        {
                            return string.IsNullOrEmpty(AdditionalMessage) ? Globalization.GetString("TaskList_Task_Status_Error") : $"{Globalization.GetString("TaskList_Task_Status_Error")}: {AdditionalMessage}";
                        }
                    case OperationStatus.Completed:
                        {
                            return Globalization.GetString("TaskList_Task_Status_Completed");
                        }
                    case OperationStatus.Cancelling:
                        {
                            return Globalization.GetString("TaskList_Task_Status_Cancelling");
                        }
                    case OperationStatus.Cancelled:
                        {
                            return string.IsNullOrEmpty(AdditionalMessage) ? Globalization.GetString("TaskList_Task_Status_Cancelled") : $"{Globalization.GetString("TaskList_Task_Status_Cancelled")}: {AdditionalMessage}";
                        }
                    default:
                        {
                            return string.Empty;
                        }
                }
            }
        }

        public bool ProgressIndeterminate { get; private set; }

        public bool ProgressError { get; private set; }

        public bool ProgressPause { get; private set; }

        public abstract bool CanBeCancelled { get; }

        public CancellationTokenSource Cancellation { get; }

        public Visibility RemoveButtonVisibility { get; private set; }

        public Visibility ActionButtonAreaVisibility { get; private set; }

        public Visibility SpeedAndTimeVisibility { get; private set; }

        public Visibility CancelButtonVisibility { get; private set; }

        public Visibility ActionButton1Visibility { get; private set; }

        public Visibility ActionButton2Visibility { get; private set; }

        public Visibility ActionButton3Visibility { get; private set; }


        private OperationStatus status;
        public OperationStatus Status
        {
            get
            {
                return status;
            }
            private set
            {
                status = value;
                ProgressPause = false;
                ProgressError = false;
                ProgressIndeterminate = true;

                switch (status)
                {
                    case OperationStatus.Waiting:
                        {
                            RemoveButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;
                            break;
                        }
                    case OperationStatus.Preparing:
                        {
                            CancelButtonVisibility = CanBeCancelled ? Visibility.Visible : Visibility.Collapsed;
                            RemoveButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;
                            break;
                        }
                    case OperationStatus.Processing:
                        {
                            CancelButtonVisibility = CanBeCancelled ? Visibility.Visible : Visibility.Collapsed;
                            RemoveButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Visible;
                            ActionButtonAreaVisibility = Visibility.Collapsed;
                            break;
                        }
                    case OperationStatus.NeedAttention:
                        {
                            ProgressPause = true;

                            ActionButton1Content = Globalization.GetString("NameCollision_Override");
                            ActionButton2Content = Globalization.GetString("NameCollision_Rename");
                            ActionButton3Content = Globalization.GetString("NameCollision_Skip");

                            RemoveButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Visible;
                            break;
                        }
                    case OperationStatus.Error:
                        {
                            ProgressError = true;

                            RemoveButtonVisibility = Visibility.Visible;
                            CancelButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;
                            break;
                        }
                    case OperationStatus.Cancelling:
                        {
                            ProgressPause = true;

                            RemoveButtonVisibility = Visibility.Visible;
                            CancelButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;

                            Cancellation?.Cancel();
                            break;
                        }
                    case OperationStatus.Cancelled:
                        {
                            ProgressPause = true;

                            RemoveButtonVisibility = Visibility.Visible;
                            CancelButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;

                            ActionButtonSource.TrySetResult(-1);
                            break;
                        }
                    case OperationStatus.Completed:
                        {
                            ProgressIndeterminate = false;

                            RemoveButtonVisibility = Visibility.Visible;
                            CancelButtonVisibility = Visibility.Collapsed;
                            SpeedAndTimeVisibility = Visibility.Collapsed;
                            ActionButtonAreaVisibility = Visibility.Collapsed;

                            UpdateProgress(100);
                            break;
                        }
                }

                OnPropertyChanged(nameof(ActionButton1Content));
                OnPropertyChanged(nameof(ActionButton2Content));
                OnPropertyChanged(nameof(ActionButton3Content));
                OnPropertyChanged(nameof(ActionButton1Visibility));
                OnPropertyChanged(nameof(ActionButton2Visibility));
                OnPropertyChanged(nameof(ActionButton3Visibility));
                OnPropertyChanged(nameof(ActionButtonAreaVisibility));
                OnPropertyChanged(nameof(CancelButtonVisibility));
                OnPropertyChanged(nameof(RemoveButtonVisibility));
                OnPropertyChanged(nameof(SpeedAndTimeVisibility));
                OnPropertyChanged(nameof(ProgressIndeterminate));
                OnPropertyChanged(nameof(ProgressError));
                OnPropertyChanged(nameof(ProgressPause));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }

        private string AdditionalMessage;
        private TaskCompletionSource<short> ActionButtonSource;
        private ProgressCalculator Calculator;

        public async Task PrepareSizeDataAsync(CancellationToken Token)
        {
            Calculator = await PrepareSizeDataCoreAsync(Token);
        }

        protected abstract Task<ProgressCalculator> PrepareSizeDataCoreAsync(CancellationToken Token);

        public void UpdateProgress(int NewProgress)
        {
            Progress = Math.Min(Math.Max(0, NewProgress), 100);

            if (Calculator != null)
            {
                Calculator.SetProgressValue(Progress);
                ProgressSpeed = Calculator.GetSpeed();
                RemainingTime = Calculator.GetRemainingTime().ConvertTimeSpanToString();
            }

            if (Progress > 0 && ProgressIndeterminate)
            {
                ProgressIndeterminate = false;
                OnPropertyChanged(nameof(ProgressIndeterminate));
            }

            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressSpeed));
            OnPropertyChanged(nameof(RemainingTime));
        }

        public void UpdateStatus(OperationStatus Status, string AdditionalMessage = null)
        {
            if (Status == OperationStatus.Cancelling && !CanBeCancelled)
            {
                throw new ArgumentException("This task could not be cancelled", nameof(Status));
            }

            this.AdditionalMessage = AdditionalMessage;
            this.Status = Status;
        }

        public void ActionButton1(object sender, RoutedEventArgs args)
        {
            ActionButtonSource.TrySetResult(0);
        }

        public void ActionButton2(object sender, RoutedEventArgs args)
        {
            ActionButtonSource.TrySetResult(1);
        }

        public void ActionButton3(object sender, RoutedEventArgs args)
        {
            ActionButtonSource.TrySetResult(2);
        }

        public async Task<short> WaitForButtonActionAsync()
        {
            try
            {
                if (Status != OperationStatus.NeedAttention)
                {
                    throw new ArgumentException("Status is not correct", nameof(Status));
                }

                return await ActionButtonSource.Task;
            }
            finally
            {
                ActionButtonSource = new TaskCompletionSource<short>();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Cancellation?.Dispose();
        }

        public OperationListBaseModel()
        {
            ProgressIndeterminate = true;
            Status = OperationStatus.Waiting;
            RemoveButtonVisibility = Visibility.Collapsed;
            ActionButtonAreaVisibility = Visibility.Collapsed;
            SpeedAndTimeVisibility = Visibility.Collapsed;

            ActionButtonSource = new TaskCompletionSource<short>();

            if (CanBeCancelled)
            {
                Cancellation = new CancellationTokenSource();
            }
        }

        ~OperationListBaseModel()
        {
            Dispose();
        }
    }
}
