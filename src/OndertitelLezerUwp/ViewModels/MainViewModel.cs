using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using OndertitelLezerUwp.Helpers;
using OndertitelLezerUwp.Services;
using Serilog;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Media.Devices;
using System.Globalization;
using Windows.Media.SpeechSynthesis;
using Windows.Media.MediaProperties;
using Windows.Media;

namespace OndertitelLezerUwp.ViewModels
{
    public class MainViewModel : Observable
    {
        private bool _isOcrStarted = false;
        private OcrDectection _ocrDetectionService;
        private DispatcherTimer _dispatcherTimer = new DispatcherTimer();
        private readonly CoreDispatcher _dispatcher;

        // Receive notifications about rotation of the UI and apply any necessary rotation to the preview stream.     
        private readonly DisplayInformation displayInformation = DisplayInformation.GetForCurrentView();
        // Rotation metadata to apply to the preview stream (MF_MT_VIDEO_ROTATION).
        // Reference: http://msdn.microsoft.com/en-us/library/windows/apps/xaml/hh868174.aspx
        private static readonly Guid RotationKey = new Guid("C380465D-2271-428C-9B83-ECEA3B4A85C1");

        // Prevent the screen from sleeping while the camera is running.
        private readonly DisplayRequest displayRequest = new DisplayRequest();

        // MediaCapture and its state variables.
        private bool isInitialized = false;
        private bool _isPreviewing = false;
        private bool _isFocused = false;
        private bool _mediaElementTtsReady = false;

        private double _threshold = 0.2;
        private int _accuracyThreshold = 90;

        // Information about the camera device.
        private bool mirroringPreview = false;
        private bool externalCamera = false;

        //Speech synthesizer stuff
        private TtsSynthesizer _ttsSynthesizer;
        private bool _isSpeechPlaying = false;
        private List<string> _trackSpokenSentences = null;

        public MainViewModel()
        {
            if (Windows.ApplicationModel.DesignMode.DesignModeEnabled)
            {
                return;
            }
            _dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;

        }

        public async Task Initialize()
        {
            //if all OK - set message to ready and button to enabled state
            try
            {
                _ocrDetectionService = new OcrDectection();
                _ttsSynthesizer = new TtsSynthesizer();
            }
            catch (System.Exception exc)
            {
                StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
                StatusMessage = "OCR / TTS fout"; //TODO resource message
                //return from here - cannot continue
                Log.Error(exc.InnerException, "Cannot continue, OCR or Synth base service failure");
                return;
            }
            
            displayInformation.OrientationChanged += DisplayInformation_OrientationChanged;

            await StartCameraAsync();
            LoadMediaElementTts();
        }

        //cleanup ocr and synthesizer stuff
        public async Task CleanUp()
        {
            await CleanupCameraAsync();
            _ocrDetectionService?.Reset();
            _ttsSynthesizer?.Reset();
            _trackSpokenSentences = null;

            displayInformation.OrientationChanged -= DisplayInformation_OrientationChanged;
        }

        private async Task OnUiThread(Action action)
        {
            await this._dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        private async Task OnUiThread(Func<Task> action)
        {
            await this._dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => await action());
        }
        
        private async void _dispatcherTimer_Tick(object sender, object e)
        {
            try
            {
                await ProcessFrameOcr(_threshold);
            }
            catch (System.Exception oexc)
            {
                Log.Error(oexc, "Error from await _ocrDetectionService.PerformOcr(bitmapToOcr, _dispatcher);");
            }

        }

        /// <summary>
        /// Every x milliseconds, capture a frame from the webcam and process for OCR. 
        /// </summary>
        /// <param name="threshold"></param>
        /// <returns></returns>
        private async Task ProcessFrameOcr(double threshold)
        {
            //Get information about the preview.
            var previewProperties = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
            int videoFrameWidth = (int)previewProperties.Width;
            int videoFrameHeight = (int)previewProperties.Height;
            
            if (!externalCamera && (displayInformation.CurrentOrientation == DisplayOrientations.Portrait || displayInformation.CurrentOrientation == DisplayOrientations.PortraitFlipped))
            {
                videoFrameWidth = (int)previewProperties.Height;
                videoFrameHeight = (int)previewProperties.Width;
            }

            // Create the video frame to request a SoftwareBitmap preview frame.
            var videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, videoFrameWidth, videoFrameHeight);

            // Capture the preview frame.
            using (var currentFrame = await _mediaCapture.GetPreviewFrameAsync(videoFrame))
            {
                // Collect the resulting frame.
                SoftwareBitmap bitmap = currentFrame.SoftwareBitmap;
                WriteableBitmap processedBitmap;
                SoftwareBitmap bitmapToOcr;
                int oneThirdHeight = bitmap.PixelHeight / 3;

                processedBitmap = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
                bitmap.CopyToBuffer(processedBitmap.PixelBuffer);

                processedBitmap = await Helpers.ImageProcessor.ApplyGaussianBlur(processedBitmap, 4);

                if (IsImageEffectsOn)
                {
                    processedBitmap = await Helpers.ImageProcessor.AdjustCurvesEffect(processedBitmap);
                    processedBitmap = await Helpers.ImageProcessor.ApplyStampThreshold(processedBitmap, threshold);

                    Log.Information($"IMG - threshold @ {threshold}, gaussian at 4 applied before OCR.");
                }

                //set the preview image source - if doing image preprocessing and effects
                if (_isOcrStarted)
                {
                    PreviewImageSource = processedBitmap;
                    PreviewImageIsVisible = true;
                    CaptureElementIsVisible = false;
                }

                if (IsOneThirdCapture)
                {
                    Rect rectOneThird = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight / 3);
                    WriteableBitmap cropped = Helpers.ImageProcessor.CropToRectangle(processedBitmap, rectOneThird);
                    bitmapToOcr = new SoftwareBitmap(BitmapPixelFormat.Bgra8, cropped.PixelWidth, cropped.PixelHeight);
                    bitmapToOcr.CopyFromBuffer(cropped.PixelBuffer);
                }
                else
                {
                    bitmapToOcr = new SoftwareBitmap(BitmapPixelFormat.Bgra8, processedBitmap.PixelWidth, processedBitmap.PixelHeight);
                    bitmapToOcr.CopyFromBuffer(processedBitmap.PixelBuffer);
                }

                Tuple<string, int> result = null;
                
                result = await _ocrDetectionService.PerformOcr(bitmapToOcr, _accuracyThreshold, _isOneThirdCapture, oneThirdHeight);
                
                if (result != null && result.Item1 != null && result.Item1 != "")
                {

                    //add to internal list keep track of spoken sentences - this is useful if an empty result is returned between two similar Ocr results (can happen if contrast is really bad for a few milliseconds)
                    if (!SentenceHasBeenSpoken(result.Item1))
                    {
                        _trackSpokenSentences.Add(result.Item1);
                        _ttsSynthesizer.AddUtteranceToSynthesize(result.Item1);
                        await SpeechSynthiserCall();
                        Log.Debug($"OCR result spoken and assigning to Textblox onscreen (Conf: {result.Item2}) - {result.Item1}");
                        OcrResultText = result.Item1;
                    }

                    StatusMessage = "";
                    StatusBackground = new SolidColorBrush(Windows.UI.Colors.Transparent);
                }
            }
        }

        private bool SentenceHasBeenSpoken(string item1)
        {
            bool returnValue = false;
            if (_trackSpokenSentences != null && _trackSpokenSentences.Count > 1)
            {
                _trackSpokenSentences.Contains(item1);
            }
            return returnValue;
        }


        #region Public props
        public double StampThreshold
        {
            get { return _threshold; }
            set
            {
                Set(ref _threshold, value);
            }
        }

        private bool _isCameraInitialized = false;
        public bool IsCameraInitialized
        {
            get { return _isCameraInitialized; }
            set { Set(ref _isCameraInitialized, value); }
        }

        private ImageSource _previewImageSource;
        public ImageSource PreviewImageSource
        {
            get { return _previewImageSource; }
            set { Set(ref _previewImageSource, value); }
        }

        private bool _previewImageIsVisible = false;
        public bool PreviewImageIsVisible
        {
            get { return _previewImageIsVisible; }
            set
            {
                if (_previewImageIsVisible != value)
                    Set(ref _previewImageIsVisible, value);
            }
        }

        private bool _startStopOcrEnabled;
        public bool StartStopOcrEnabled
        {
            get { return _startStopOcrEnabled; }
            set { Set(ref _startStopOcrEnabled, value); }
        }

        private bool _isOneThirdCapture;
        public bool IsOneThirdCapture
        {
            get { return _isOneThirdCapture; }
            set { Set(ref _isOneThirdCapture, value); }
        }
        private double _captureElementActualWidth;
        public double CaptureElementActualWidth
        {
            get { return _captureElementActualWidth; }
            set { Set(ref _captureElementActualWidth, value); }
        }
        private double _captureElementActualHeight;
        public double CaptureElementActualHeight
        {
            get { return _captureElementActualHeight; }
            set { Set(ref _captureElementActualHeight, value); }
        }

        private string _ocrResultText;
        public string OcrResultText
        {
            get { return _ocrResultText; }
            set { Set(ref _ocrResultText, value); }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get { return _statusMessage; }

            set { Set(ref _statusMessage, value); }

        }

        private Brush _statusBackground;
        public Brush StatusBackground
        {
            get => _statusBackground ?? (_statusBackground = new SolidColorBrush(Windows.UI.Colors.Transparent));

            set
            {
                if (_statusBackground != value)
                    Set(ref _statusBackground, value);
            }
        }

        private bool _isImageEffectOn = true;
        public bool IsImageEffectsOn
        {
            get { return _isImageEffectOn; }
            set { Set(ref _isImageEffectOn, value); }
        }

        private Symbol _symbolStartStop = Symbol.Play;
        public Symbol SymbolStartStop
        {
            get { return _symbolStartStop; }
            set { Set(ref _symbolStartStop, value); }
        }

        private Brush _symbolStartStopColor;
        public Brush SymbolStartStopColor
        {
            get => _symbolStartStopColor ?? (_symbolStartStopColor = new SolidColorBrush(Windows.UI.Colors.Green));

            set
            {
                Set(ref _symbolStartStopColor, value);
            }
        }
        #endregion  


        #region Commands
        private ICommand _startStopOcrCommand;
        public ICommand StartStopOcrCommand
        {
            get
            {
                return _startStopOcrCommand
                  ?? (_startStopOcrCommand = new RelayCommand(
                    async () =>
                    {
                        await StartOrStopOcr();
                    }));
            }
        }

        //Task managed by the start/stop button on the screen
        private async Task StartOrStopOcr()
        {
            //if started, stop ocr process, else start it
            if (_isOcrStarted)
            {
                _isOcrStarted = false;
                PreviewImageSource = null;
                PreviewImageIsVisible = false;
                CaptureElementIsVisible = true;


                _dispatcherTimer.Stop();
                _dispatcherTimer.Tick -= _dispatcherTimer_Tick;
                SymbolStartStop = Symbol.Play;
                SymbolStartStopColor = new SolidColorBrush(Windows.UI.Colors.Green);

                _ocrDetectionService.Reset();
                _ttsSynthesizer.Reset();
                _trackSpokenSentences = null;

                _mediaCapture.VideoDeviceController.Focus.TrySetAuto(true);

            }
            else
            {
                SymbolStartStopColor = new SolidColorBrush(Windows.UI.Colors.Gray);

                PreviewImageSource = null;
                PreviewImageIsVisible = false;
                CaptureElementIsVisible = true;

                _ocrDetectionService.Reset();
                _ttsSynthesizer.Reset();
                _trackSpokenSentences = new List<string>();

                if(_mediaCapture.VideoDeviceController.ExposureControl.Supported)
                {
                    await _mediaCapture.VideoDeviceController.ExposureControl.SetAutoAsync(false);
                }

                SymbolStartStop = Symbol.Pause;
                SymbolStartStopColor = new SolidColorBrush(Windows.UI.Colors.Red);
                _isOcrStarted = true;

                _dispatcherTimer.Tick += _dispatcherTimer_Tick;
                _dispatcherTimer.Interval = new TimeSpan(0, 0, 0, 0, 600);
                _dispatcherTimer.Start();


            }

        }
        #endregion

        #region Media Capture stuff - init and setup
        private MediaCapture _mediaCapture;
        public MediaCapture MediaCapture
        {
            get
            {
                if (_mediaCapture == null) _mediaCapture = new MediaCapture();
                return _mediaCapture;
            }
            set
            {
                Set(ref _mediaCapture, value);
            }
        }

        private CaptureElement _captureElement;
        public CaptureElement CaptureElement
        {
            get
            {
                if (_captureElement == null) _captureElement = new CaptureElement();
                return _captureElement;
            }
            set
            {
                Set(ref _captureElement, value);
            }
        }

        private bool _captureElementVisible = false;
        public bool CaptureElementIsVisible
        {
            get
            {
                return _captureElementVisible;
            }
            set
            {
                if (_captureElementVisible != value)
                    Set(ref _captureElementVisible, value);
            }
        }

        private Windows.UI.Xaml.FlowDirection _mediaFlowDirection;
        public Windows.UI.Xaml.FlowDirection MediaFlowDirection
        {
            get { return _mediaFlowDirection; }
            set { Set(ref _mediaFlowDirection, value); }
        }

        private async Task CleanupCameraAsync()
        {
            if (isInitialized)
            {
                if (_isPreviewing)
                {
                    await StopPreviewAsync();

                }

                isInitialized = false;
            }

            if (_mediaCapture != null)
            {
                _mediaCapture.Failed -= MediaCapture_Failed;
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }
        }

        private async Task StopPreviewAsync()
        {
            try
            {
                _isPreviewing = false;

                PreviewImageIsVisible = false;
                CaptureElementIsVisible = true;
                CaptureElement = null;
                await _mediaCapture.StopPreviewAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MainViewModel - StopPreview error");
                StatusMessage = $"Error - {ex.Message}";
                StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
            }

            await this.OnUiThread(() => displayRequest.RequestRelease());

        }



        private async Task StartCameraAsync()
        {
            if (!isInitialized)
            {
                await InitializeCameraAsync();
            }

            if (isInitialized)
            {

                PreviewImageIsVisible = false;
                PreviewImageSource = null;

                IsCameraInitialized = true;
                StartStopOcrEnabled = true;
                StatusMessage = "OK"; //TODO correct message with resource
                StatusBackground = new SolidColorBrush(Windows.UI.Colors.Transparent);

            }
        }


        private async void DisplayInformation_OrientationChanged(DisplayInformation sender, object args)
        {
            if (_isPreviewing)
            {
                await SetPreviewRotationAsync();
            }
        }



        #endregion

        #region ManipulateControls MediaCapture

        private async Task SetPreviewRotationAsync()
        {
            if (externalCamera) return;

            // Calculate which way and how far to rotate the preview.
            CalculatePreviewRotation(out VideoRotation sourceRotation, out int rotationDegrees);

            // Set preview rotation in the preview source.
            _mediaCapture.SetPreviewRotation(sourceRotation);

            // Add rotation metadata to the preview stream to make sure the aspect ratio / dimensions match when rendering and getting preview frames
            var props = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview);
            props.Properties.Add(RotationKey, rotationDegrees);
            await _mediaCapture.SetEncodingPropertiesAsync(MediaStreamType.VideoPreview, props, null);
        }


        private void CalculatePreviewRotation(out VideoRotation sourceRotation, out int rotationDegrees)
        {
           
            switch (displayInformation.CurrentOrientation)
            {
                case DisplayOrientations.Portrait:
                    if (mirroringPreview)
                    {
                        rotationDegrees = 270;
                        sourceRotation = VideoRotation.Clockwise270Degrees;
                    }
                    else
                    {
                        rotationDegrees = 90;
                        sourceRotation = VideoRotation.Clockwise90Degrees;
                    }
                    break;

                case DisplayOrientations.LandscapeFlipped:
                    // No need to invert this rotation, as rotating 180 degrees is the same either way.
                    rotationDegrees = 180;
                    sourceRotation = VideoRotation.Clockwise180Degrees;
                    break;

                case DisplayOrientations.PortraitFlipped:
                    if (mirroringPreview)
                    {
                        rotationDegrees = 90;
                        sourceRotation = VideoRotation.Clockwise90Degrees;
                    }
                    else
                    {
                        rotationDegrees = 270;
                        sourceRotation = VideoRotation.Clockwise270Degrees;
                    }
                    break;

                case DisplayOrientations.Landscape:
                default:
                    rotationDegrees = 0;
                    sourceRotation = VideoRotation.None;
                    break;
            }
        }

        private async Task InitializeCameraAsync()
        {
            if (_mediaCapture == null)
            {
                var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

                //only wors with backpanel hard coded/ can be refactored to leverage external camera
                DeviceInformation cameraDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation?.Panel == Windows.Devices.Enumeration.Panel.Back);
               
                
                if (cameraDevice == null)
                {
                    StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
                    StatusMessage = "Camera error"; //TODO resource message
                    Log.Error("MainViewModel - InitializeCameraAsync() - Camera device null");

                    StartStopOcrEnabled = false;

                    return;
                }

                // Create MediaCapture and its settings.
                _mediaCapture = new MediaCapture();

                // Register for a notification when something goes wrong
                _mediaCapture.Failed += MediaCapture_Failed;

                var settings = new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = cameraDevice.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                };

                // Initialize MediaCapture
                try
                {
                    await _mediaCapture.InitializeAsync(settings);
                    isInitialized = true;
                }
                catch (UnauthorizedAccessException)
                {
                    //TODO
                    //rootPage.NotifyUser("Denied access to the camera.", NotifyType.ErrorMessage);
                }
                catch (Exception ex)
                {
                    //TODO
                    //rootPage.NotifyUser("Exception when init MediaCapture. " + ex.Message, NotifyType.ErrorMessage);
                    StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
                    StatusMessage = "OCR / TTS fout"; //TODO resource message
                    Log.Error(ex, "_mediaCapture.InitializeAsync error");
                }

                // If initialization succeeded, start the preview.

                if (isInitialized)
                {
                    _mediaCapture.VideoDeviceController.Focus.TrySetAuto(true);

                    //exposure/brightness
                    //TODO optimize for exposure/
                    if (_mediaCapture.VideoDeviceController.ExposureControl.Supported)
                    {
                        var exposureControl = _mediaCapture.VideoDeviceController.ExposureControl;
                        var max = exposureControl.Max;
                        var min = exposureControl.Min;
                        var step = exposureControl.Step;
                        //TimeSpan exposureSet = max.Subtract(new TimeSpan(1000000));
                        //TimeSpan exposureSet = min.Add(new TimeSpan(100000));
                        await _mediaCapture.VideoDeviceController.ExposureControl.SetAutoAsync(true);
                        //exposureControl.Value

                        // await _mediaCapture.VideoDeviceController.ExposureControl.SetValueAsync(exposureSet);

                    }


                    var focusControl = _mediaCapture.VideoDeviceController.FocusControl;
                    if (focusControl.Supported)
                    {
                        var focusRange = focusControl.SupportedFocusRanges.Contains(AutoFocusRange.FullRange) ? AutoFocusRange.FullRange : focusControl.SupportedFocusRanges.FirstOrDefault();
                        var focusMode = focusControl.SupportedFocusModes.Contains(FocusMode.Single) ? FocusMode.Single : focusControl.SupportedFocusModes.FirstOrDefault();

                        var focusSettings = new FocusSettings { Mode = focusMode, AutoFocusRange = focusRange };
                        focusControl.Configure(focusSettings);

                        await focusControl.FocusAsync();
                        await focusControl.LockAsync();

                    }

                    //var focusControl = _mediaCapture.VideoDeviceController.FocusControl;
                    ////tODO
                    //if (focusControl.Supported)
                    //{


                    //    //set focus and lock

                    //    FocusSlider.Visibility = Visibility.Visible;
                    //    ManualFocusRadioButton.Visibility = Visibility.Visible;

                    //    FocusSlider.Minimum = focusControl.Min;
                    //    FocusSlider.Maximum = focusControl.Max;
                    //    FocusSlider.StepFrequency = focusControl.Step;


                    //    FocusSlider.ValueChanged -= FocusSlider_ValueChanged;
                    //    FocusSlider.Value = focusControl.Value;
                    //    FocusSlider.ValueChanged += FocusSlider_ValueChanged;


                    //}
                    //else
                    //{
                    //    FocusSlider.Visibility = Visibility.Collapsed;
                    //    ManualFocusRadioButton.Visibility = Visibility.Collapsed;
                    //}

                    // Figure out where the camera is located
                    if (cameraDevice.EnclosureLocation == null || cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Unknown)
                    {
                        // No information on the location of the camera, assume it's an external camera, not integrated on the device.
                        externalCamera = true;
                    }
                    else
                    {
                        // Camera is fixed on the device.
                        externalCamera = false;

                        // Only mirror the preview if the camera is on the front panel.
                        mirroringPreview = (cameraDevice.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
                    }

                    //try to set stabilization mode to ON
                    if (_mediaCapture.VideoDeviceController.OpticalImageStabilizationControl.Supported)
                    {
                        var stabilizationModes = _mediaCapture.VideoDeviceController.OpticalImageStabilizationControl.SupportedModes;

                        if (stabilizationModes.Contains(OpticalImageStabilizationMode.On))
                        {
                            _mediaCapture.VideoDeviceController.OpticalImageStabilizationControl.Mode = OpticalImageStabilizationMode.On;

                        }
                    }

                    await StartPreviewAsync();
                }
            }
        }


        private async Task StartPreviewAsync()
        {
            displayRequest.RequestActive();


            MediaFlowDirection = mirroringPreview ? Windows.UI.Xaml.FlowDirection.RightToLeft : Windows.UI.Xaml.FlowDirection.LeftToRight;
            CaptureElement.Source = _mediaCapture;
            CaptureElement.FlowDirection = _mediaFlowDirection;
            CaptureElementIsVisible = true;


            // Start the preview.
            try
            {
                await _mediaCapture.StartPreviewAsync();
                _isPreviewing = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Exception: {ex.Message}";
                StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
                Log.Error(ex, "StartPreviewAsync error");
                //TODO rootPage.NotifyUser("Exception starting preview." + ex.Message, NotifyType.ErrorMessage);
            }

            // Initialize the preview to the current orientation.
            if (_isPreviewing)
            {
                await SetPreviewRotationAsync();
            }
        }
        

        private async void MediaCapture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            await CleanupCameraAsync();
            StatusMessage = "Camera error, restart app";
            StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
            Log.Error("MainViewModel - MediaCapture_Failed");

            CaptureElement = null;
            StartStopOcrEnabled = false;
            _isOcrStarted = false;

        }

        #endregion


        #region Media Focus stuff

        public async Task TapToFocus(Point position, Size size)
        {
            if (!_isPreviewing) return;

            if (!_isFocused && _mediaCapture.VideoDeviceController.FocusControl.FocusState != MediaCaptureFocusState.Searching)
            {
                _mediaCapture.VideoDeviceController.Focus.TrySetAuto(false);
                _isFocused = true;

                var previewRect = GetPreviewStreamRectInControl();
                var focusPreview = ConvertUiTapToPreviewRect(position, size, previewRect);

                // Note that this Region Of Interest could be configured to also calculate exposure 
                // and white balance within the region
                var regionOfInterest = new RegionOfInterest
                {
                    AutoFocusEnabled = true,
                    BoundsNormalized = true,
                    Bounds = focusPreview,
                    Type = RegionOfInterestType.Unknown,
                    Weight = 100
                };


                var focusControl = _mediaCapture.VideoDeviceController.FocusControl;
                var focusRange = focusControl.SupportedFocusRanges.Contains(AutoFocusRange.FullRange) ? AutoFocusRange.FullRange : focusControl.SupportedFocusRanges.FirstOrDefault();
                var focusMode = focusControl.SupportedFocusModes.Contains(FocusMode.Single) ? FocusMode.Single : focusControl.SupportedFocusModes.FirstOrDefault();
                var settings = new FocusSettings { Mode = focusMode, AutoFocusRange = focusRange };
                focusControl.Configure(settings);

                var roiControl = _mediaCapture.VideoDeviceController.RegionsOfInterestControl;
                await roiControl.SetRegionsAsync(new[] { regionOfInterest }, true);

                await focusControl.FocusAsync();
                await focusControl.LockAsync();
            }
            else
            {
                //unfocus
                _isFocused = false;
                _mediaCapture.VideoDeviceController.Focus.TrySetAuto(true);

                var roiControl = _mediaCapture.VideoDeviceController.RegionsOfInterestControl;
                await roiControl.ClearRegionsAsync();

                var focusControl = _mediaCapture.VideoDeviceController.FocusControl;
                await focusControl.FocusAsync();
            }


        }


        /// <summary>
        /// The GetPreviewStreamRectInControl helper method uses the resolution of the preview stream and the orientation of the device to determine the rectangle within the preview element that contains the preview stream, trimming off any letterboxed padding that the control may provide to maintain the stream's aspect ratio.
        /// </summary>
        /// <returns></returns>
        private Rect GetPreviewStreamRectInControl()
        {
            var result = new Rect();
            var displayOrientation = displayInformation.CurrentOrientation;

            var previewResolution = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

            // In case this function is called before everything is initialized correctly, return an empty result
            if (CaptureElement == null || CaptureElement.ActualHeight < 1 || CaptureElement.ActualWidth < 1 ||
                previewResolution == null || previewResolution.Height == 0 || previewResolution.Width == 0)
            {
                return result;
            }

            var streamWidth = previewResolution.Width;
            var streamHeight = previewResolution.Height;

            // For portrait orientations, the width and height need to be swapped
            if (displayOrientation == DisplayOrientations.Portrait || displayOrientation == DisplayOrientations.PortraitFlipped)
            {
                streamWidth = previewResolution.Height;
                streamHeight = previewResolution.Width;
            }

            // Start by assuming the preview display area in the control spans the entire width and height both (this is corrected in the next if for the necessary dimension)
            result.Width = CaptureElement.ActualWidth;
            result.Height = CaptureElement.ActualHeight;

            // If UI is "wider" than preview, letterboxing will be on the sides
            if ((CaptureElement.ActualWidth / CaptureElement.ActualHeight > streamWidth / (double)streamHeight))
            {
                var scale = CaptureElement.ActualHeight / streamHeight;
                var scaledWidth = streamWidth * scale;

                result.X = (CaptureElement.ActualWidth - scaledWidth) / 2.0;
                result.Width = scaledWidth;
            }
            else // Preview stream is "wider" than UI, so letterboxing will be on the top+bottom
            {
                var scale = CaptureElement.ActualWidth / streamWidth;
                var scaledHeight = streamHeight * scale;

                result.Y = (CaptureElement.ActualHeight - scaledHeight) / 2.0;
                result.Height = scaledHeight;
            }

            return result;
        }

        /// <summary>
        /// This method as arguments the location of the tap event, the desired size of the focus region, and the rectangle containing the preview stream obtained from the GetPreviewStreamRectInControl helper method. This method uses these values and the device's current orientation to calculate the rectangle within the preview stream that contains the desired region
        /// </summary>
        /// <param name="tap"></param>
        /// <param name="size"></param>
        /// <param name="previewRect"></param>
        /// <returns></returns>
        private Rect ConvertUiTapToPreviewRect(Point tap, Size size, Rect previewRect)
        {
            var displayOrientation = displayInformation.CurrentOrientation;

            // Adjust for the resulting focus rectangle to be centered around the position
            double left = tap.X - size.Width / 2, top = tap.Y - size.Height / 2;

            // Get the information about the active preview area within the CaptureElement (in case it's letterboxed)
            double previewWidth = previewRect.Width, previewHeight = previewRect.Height;
            double previewLeft = previewRect.Left, previewTop = previewRect.Top;

            // Transform the left and top of the tap to account for rotation
            switch (displayOrientation)
            {
                case DisplayOrientations.Portrait:
                    var tempLeft = left;

                    left = top;
                    top = previewRect.Width - tempLeft;
                    break;
                case DisplayOrientations.LandscapeFlipped:
                    left = previewRect.Width - left;
                    top = previewRect.Height - top;
                    break;
                case DisplayOrientations.PortraitFlipped:
                    var tempTop = top;

                    top = left;
                    left = previewRect.Width - tempTop;
                    break;
            }

            // For portrait orientations, the information about the active preview area needs to be rotated
            if (displayOrientation == DisplayOrientations.Portrait || displayOrientation == DisplayOrientations.PortraitFlipped)
            {
                previewWidth = previewRect.Height;
                previewHeight = previewRect.Width;
                previewLeft = previewRect.Top;
                previewTop = previewRect.Left;
            }

            // Normalize width and height of the focus rectangle
            var width = size.Width / previewWidth;
            var height = size.Height / previewHeight;

            // Shift rect left and top to be relative to just the active preview area
            left -= previewLeft;
            top -= previewTop;

            // Normalize left and top
            left /= previewWidth;
            top /= previewHeight;

            // Ensure rectangle is fully contained within the active preview area horizontally
            left = Math.Max(left, 0);
            left = Math.Min(1 - width, left);

            // Ensure rectangle is fully contained within the active preview area vertically
            top = Math.Max(top, 0);
            top = Math.Min(1 - height, top);

            // Create and return resulting rectangle
            return new Rect(left, top, width, height);
        }

        #endregion


        #region TTS and mediaplayer stuff
        private MediaElement _mediaElementTts;
        public MediaElement MediaElementTts
        {
            get { return _mediaElementTts; }
            set { Set(ref _mediaElementTts, value); }
        }



        private void LoadMediaElementTts()
        {
            if (_mediaElementTts == null)
            {
                MediaElementTts = new MediaElement();
                MediaElementTts.Loaded += _mediaElementTts_Loaded;
                MediaElementTts.MediaEnded += _mediaElementTts_MediaEnded;
            }
        }

        private async void _mediaElementTts_MediaEnded(object sender, RoutedEventArgs e)
        {
            _isSpeechPlaying = false;
            await SpeechSynthiserCall();
        }

        private async Task SpeechSynthiserCall()
        {
            if (_isSpeechPlaying)
                return;

            // Set the source and start playing the synthesized audio stream.
            try
            {
                SpeechSynthesisStream stream = await _ttsSynthesizer.GetSynthesizedTextStream();
                if (stream != null)
                {
                    MediaElementTts.AutoPlay = true;
                    MediaElementTts.SetSource(stream, stream.ContentType);
                    MediaElementTts.Play();

                    _isSpeechPlaying = true;
                }
            }

            catch (System.IndexOutOfRangeException)
            {

                _isSpeechPlaying = false;
            }
            catch (Exception e)
            {
                // If the text is unable to be synthesized, throw an error message to the user.

                MediaElementTts.AutoPlay = false;
                StatusBackground = new SolidColorBrush(Windows.UI.Colors.Red);
                StatusMessage = "Audio problem";
                Log.Error(e, "MediaElementTts error");

            }
        }

        private void _mediaElementTts_Loaded(object sender, RoutedEventArgs e)
        {
            _mediaElementTtsReady = true;
        }


        #endregion

        #region Debug functions
        //for debugging frames to disk
        private async Task SaveCurrentFrameDebug(SoftwareBitmap bitmap)
        {
            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            try
            {

                Windows.Storage.StorageFile frameFile = await storageFolder.CreateFileAsync(string.Format("OcrLog{0}.png", DateTime.Now.ToString("yyyy-MM-dd-hh-mm-ss-ff", CultureInfo.InvariantCulture)),
                        Windows.Storage.CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await frameFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    // Create an encoder with the desired format
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);

                    // Set the software bitmap
                    encoder.SetSoftwareBitmap(bitmap);

                    try
                    {
                        await encoder.FlushAsync();
                    }
                    catch (Exception err)
                    {
                        switch (err.HResult)
                        {
                            case unchecked((int)0x88982F81): //WINCODEC_ERR_UNSUPPORTEDOPERATION
                                                             // If the encoder does not support writing a thumbnail, then try again
                                                             // but disable thumbnail generation.
                                encoder.IsThumbnailGenerated = false;
                                break;
                            default:
                                throw err;
                        }
                    }


                }
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Exception trying to store file");
            }
        }
        #endregion
    }
}
