using System;
using System.ComponentModel;
using System.Linq;
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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.OnLoaded();
        }

        private void ListViewOnMouseDown(object sender, MouseButtonEventArgs e)
        {
            HitTestResult r = VisualTreeHelper.HitTest(this, e.GetPosition(this));
            if (r.VisualHit.GetType() != typeof(ListBoxItem))
                ((ListView)sender).UnselectAll();
        }

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_viewModel.HasRunningOperations)
            {
                if (IsEnabled)
                    await _viewModel.PreDisposeAsync();
                return;
            }

            e.Cancel = true;
            if (!IsEnabled)
                return;

            if (_viewModel.Operations.Any(x => x.IsRunning))
            {
                MessageBoxResult messageBoxResult = MessageBox.Show(
                    $"Are you sure you want to close {MainViewModel.ApplicationName}? Currently running operations will be aborted.",
                    $"Closing {MainViewModel.ApplicationName}",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (messageBoxResult != MessageBoxResult.Yes)
                    return;
            }

            IsEnabled = false;
            await _viewModel.PreDisposeAsync();
            Application.Current.Dispatcher.Invoke(Close);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _viewModel.Dispose();
        }
    }
}
