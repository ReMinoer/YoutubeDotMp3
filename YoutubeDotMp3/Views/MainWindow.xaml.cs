using System;
using System.Windows.Input;
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

        private void OnClosed(object sender, EventArgs e)
        {
            (DataContext as IDisposable)?.Dispose();
        }

        private void UrlTextBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
                _viewModel.AddOperation(UrlTextBox.Text);
        }
    }
}
