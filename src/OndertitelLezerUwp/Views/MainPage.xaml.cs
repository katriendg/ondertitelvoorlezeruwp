using System;
using OndertitelLezerUwp.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.System;

namespace OndertitelLezerUwp.Views
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new MainViewModel();

        public MainPage()
        {
            InitializeComponent();

            Application.Current.Suspending += Current_Suspending;
            Application.Current.Resuming += Current_Resuming;

        }

        private async void Current_Resuming(object sender, object e)
        {

            await ViewModel.Initialize();
        }

        private async void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();

            await ViewModel.CleanUp();

            deferral.Complete();
        }


        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            await ViewModel.Initialize();

            this.Loaded += delegate { this.Focus(FocusState.Programmatic); };
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            await ViewModel.CleanUp();

            base.OnNavigatedFrom(e);

        }

        /// <summary>
        /// When the MediaElement is tapped, calculate position and fire off tap focus/unfocus (first tap will focus, tap again to unfocus)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ContentControl_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {

            var smallEdge = Math.Min(Window.Current.Bounds.Width, Window.Current.Bounds.Height);

            // Choose to make the focus rectangle 1/4th the length of the shortest edge of the window
            var size = new Size(smallEdge / 4, smallEdge / 4);
            var position = e.GetPosition(sender as UIElement);

            await ViewModel.TapToFocus(position, size);

        }

        private void CameraPreviewGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //reflect change onto actual witdh/height
            ViewModel.CaptureElementActualWidth = CaptureContentControl.Visibility == Visibility.Visible ? CaptureContentControl.ActualWidth : PreviewOcrImage.ActualWidth;
            ViewModel.CaptureElementActualHeight = CaptureContentControl.Visibility == Visibility.Visible ? CaptureContentControl.ActualHeight : PreviewOcrImage.ActualHeight;
        }

        private void HostGrid_KeyUp(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (IsCtrlKeyPressed())
            {
                if (e.Key == VirtualKey.W)
                {
                    ViewModel.StartStopOcrCommand.Execute(null);
                }
            }
        }

        private static bool IsCtrlKeyPressed()
        {
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            return (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }
    }
}
