using Lumia.Imaging;
using Lumia.Imaging.Adjustments;
using Lumia.InteropServices.WindowsRuntime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;

namespace OndertitelLezerUwp.Helpers
{
    public static class ImageProcessor
    {
        /// <summary>
        /// Crops a bitmap to a pre-defined area of the Rectangle
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static WriteableBitmap CropToRectangle(WriteableBitmap bitmap, Rect rect)
        {

            WriteableBitmap croppedBitmap = Windows.UI.Xaml.Media.Imaging.WriteableBitmapExtensions.Crop(bitmap, rect);

            return croppedBitmap;

        }




        public static async Task<WriteableBitmap> AdjustCurvesEffect(WriteableBitmap imgSource)
        {
            //var imgSource = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            //bitmap.CopyToBuffer(imgSource.PixelBuffer);

            var source = new BitmapImageSource(imgSource.AsBitmap());

            var curvesEffect = new CurvesEffect
            {
                Source = source
            };

            //allow for curve values to be set via settings pane with 
            var globalCurve = new Curve(CurveInterpolation.NaturalCubicSpline, new[]
            {
                new Point(200, 62)
            });
            //156, 78
            //new Point(110, 34)

            curvesEffect.Red = globalCurve;
            curvesEffect.Green = globalCurve;
            curvesEffect.Blue = globalCurve;

            var adjustedImg = new WriteableBitmap(imgSource.PixelWidth, imgSource.PixelHeight);

            using (var renderer = new WriteableBitmapRenderer(curvesEffect, adjustedImg))
            {
                // Generate the gray image
                await renderer.RenderAsync();
            }

            return adjustedImg;
        }



        public static async Task<WriteableBitmap> ApplyGaussianBlur(WriteableBitmap imgSource, int kernelS)
        {

            BitmapImageSource source = new BitmapImageSource(imgSource.AsBitmap());
            BlurEffect effect = new BlurEffect(source, kernelS);

            WriteableBitmap blurredImage = new WriteableBitmap(imgSource.PixelWidth, imgSource.PixelHeight);

            using (var renderer = new WriteableBitmapRenderer(effect, blurredImage))
            {
                // Generate the gray image
                await renderer.RenderAsync();
            }

            return blurredImage;
        }



        public static async Task<WriteableBitmap> ApplyStampThreshold(WriteableBitmap imgSource, double threshold)
        {

            var source = new BitmapImageSource(imgSource.AsBitmap());
            var effect = new Lumia.Imaging.Artistic.StampEffect(source, 0, threshold);

            var stampImage = new WriteableBitmap(imgSource.PixelWidth, imgSource.PixelHeight);

            using (var renderer = new WriteableBitmapRenderer(effect, stampImage))
            {
                // Generate the gray image
                await renderer.RenderAsync();
            }

            return stampImage;
        }

    }
}
