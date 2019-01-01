using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YoutubeDotMp3.ViewModels;

namespace YoutubeDotMp3.Views
{
    public partial class MainWindow
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = (MainViewModel)DataContext;
        }

        private void ListViewOnMouseDown(object sender, MouseButtonEventArgs e)
        {
            HitTestResult r = VisualTreeHelper.HitTest(this, e.GetPosition(this));
            if (r.VisualHit.GetType() != typeof(ListBoxItem))
                ((ListView)sender).UnselectAll();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_viewModel.HasRunningOperations)
            {
                if (!IsEnabled)
                {
                    e.Cancel = true;
                    return;
                }

                MessageBoxResult messageBoxResult = MessageBox.Show(
                    $"Are you sure you want to close {MainViewModel.ApplicationName}? Currently running operations will be aborted.",
                    $"Closing {MainViewModel.ApplicationName}", 
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (messageBoxResult != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                
                IsEnabled = false;
                e.Cancel = true;
                
                _viewModel.PreDisposeAsync().ContinueWith(_ => Application.Current.Dispatcher.Invoke(Close));
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _viewModel.Dispose();
        }
    }
}
