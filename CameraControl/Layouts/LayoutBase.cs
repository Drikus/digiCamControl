﻿#region Licence

// Distributed under MIT License
// ===========================================================
// 
// digiCamControl - DSLR camera remote control open source software
// Copyright (C) 2014 Duka Istvan
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY,FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

#region

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Devices;
using GalaSoft.MvvmLight.Command;
using Microsoft.VisualBasic.FileIO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameraControl.Classes;
using CameraControl.Controls.ZoomAndPan;
using CameraControl.ViewModel;
using Clipboard = System.Windows.Clipboard;
using ListBox = System.Windows.Controls.ListBox;
using MessageBox = System.Windows.MessageBox;
using UserControl = System.Windows.Controls.UserControl;

#endregion

namespace CameraControl.Layouts
{
    public class LayoutBase : UserControl
    {
        /// <summary>
        /// Specifies the current state of the mouse handling logic.
        /// </summary>
        protected MouseHandlingMode mouseHandlingMode = MouseHandlingMode.None;

        /// <summary>
        /// The point that was clicked relative to the ZoomAndPanControl.
        /// </summary>
        protected Point origZoomAndPanControlMouseDownPoint;

        /// <summary>
        /// The point that was clicked relative to the content that is contained within the ZoomAndPanControl.
        /// </summary>
        protected Point origContentMouseDownPoint;

        /// <summary>
        /// Records which mouse button clicked during mouse dragging.
        /// </summary>
        protected MouseButton mouseButtonDown;

        /// <summary>
        /// Saves the previous zoom rectangle, pressing the backspace key jumps back to this zoom rectangle.
        /// </summary>
        protected Rect prevZoomRect;

        /// <summary>
        /// Save the previous content scale, pressing the backspace key jumps back to this scale.
        /// </summary>
        protected double prevZoomScale;

        /// <summary>
        /// Set to 'true' when the previous zoom rect is saved.
        /// </summary>
        protected bool prevZoomRectSet = false;

        public ListBox ImageLIst { get; set; }
        private readonly BackgroundWorker _worker = new BackgroundWorker();
        private FileItem _selectedItem = null;
        public ZoomAndPanControl ZoomAndPanControl { get; set; }
        public  UIElement content { get; set; }
        public MediaElement MediaElement { get; set; }

        public LayoutViewModel LayoutViewModel { get; set; }

        public LayoutBase()
        {
            LayoutViewModel = new LayoutViewModel();
            _worker.DoWork += worker_DoWork;
            _worker.RunWorkerCompleted += _worker_RunWorkerCompleted;
        }

        public void UnInit()
        {
            if (ZoomAndPanControl != null)
            {
                ZoomAndPanControl.ContentScaleChanged -= ZoomAndPanControl_ContentScaleChanged;
                ZoomAndPanControl.ContentOffsetXChanged -= ZoomAndPanControl_ContentScaleChanged;
                ZoomAndPanControl.ContentOffsetYChanged -= ZoomAndPanControl_ContentScaleChanged;
            }
            _worker.DoWork -= worker_DoWork;
            _worker.RunWorkerCompleted -= _worker_RunWorkerCompleted;
            ServiceProvider.Settings.PropertyChanged -= Settings_PropertyChanged;
            ServiceProvider.WindowsManager.Event -= Trigger_Event;
            ImageLIst.SelectionChanged -= ImageLIst_SelectionChanged;
        }


        private void _worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (_selectedItem != ServiceProvider.Settings.SelectedBitmap.FileItem)
            {
                ServiceProvider.Settings.SelectedBitmap.FileItem = _selectedItem;
                _worker.RunWorkerAsync(_selectedItem);
            }
            else
            {
                if (LayoutViewModel.FreeZoom)
                {
                    LoadFullRes();
                }
            }
        }

        private void DeleteItem()
        {
            List<FileItem> filestodelete = new List<FileItem>();
            try
            {
                filestodelete.AddRange(
                    ServiceProvider.Settings.DefaultSession.Files.Where(fileItem => fileItem.IsChecked));

                if (ServiceProvider.Settings.SelectedBitmap != null &&
                    ServiceProvider.Settings.SelectedBitmap.FileItem != null &&
                    filestodelete.Count == 0)
                    filestodelete.Add(ServiceProvider.Settings.SelectedBitmap.FileItem);

                if (filestodelete.Count == 0)
                    return;
                int selectedindex = ImageLIst.Items.IndexOf(filestodelete[0]);

                bool delete = false;
                if (filestodelete.Count > 1)
                {
                    delete = MessageBox.Show("Multile files are selected !! Do you really want to delete selected files ?", "Delete files",
                        MessageBoxButton.YesNo) == MessageBoxResult.Yes;
                }
                else
                {
                    delete = MessageBox.Show("Do you really want to delete selected file ?", "Delete file",
                        MessageBoxButton.YesNo) == MessageBoxResult.Yes;

                }
                if (delete)
                                    {
                    foreach (FileItem fileItem in filestodelete)
                    {
                        if ((ServiceProvider.Settings.SelectedBitmap != null &&
                             ServiceProvider.Settings.SelectedBitmap.FileItem != null &&
                             fileItem.FileName == ServiceProvider.Settings.SelectedBitmap.FileItem.FileName))
                        {
                            ServiceProvider.Settings.SelectedBitmap.DisplayImage = null;
                        }
                        if (File.Exists(fileItem.FileName))
                            FileSystem.DeleteFile(fileItem.FileName, UIOption.OnlyErrorDialogs,
                                                  RecycleOption.SendToRecycleBin);
                        fileItem.RemoveThumbs();
                        ServiceProvider.Settings.DefaultSession.Files.Remove(fileItem);
                    }
                    if (selectedindex < ImageLIst.Items.Count)
                    {
                        ImageLIst.SelectedIndex = selectedindex + 1;
                        ImageLIst.SelectedIndex = selectedindex - 1;
                        FileItem item = ImageLIst.SelectedItem as FileItem;

                        if (item != null)
                            ImageLIst.ScrollIntoView(item);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("Error to delete file", exception);
            }
        }

        public void InitServices()
        {
            ServiceProvider.Settings.PropertyChanged += Settings_PropertyChanged;
            ServiceProvider.WindowsManager.Event += Trigger_Event;
            ImageLIst.SelectionChanged += ImageLIst_SelectionChanged;
            if (ServiceProvider.Settings.SelectedBitmap != null &&
                ServiceProvider.Settings.SelectedBitmap.FileItem != null)
            {
                ImageLIst.SelectedItem = ServiceProvider.Settings.SelectedBitmap.FileItem;
                ImageLIst.ScrollIntoView(ImageLIst.SelectedItem);
            }
            else
            {
                if (ServiceProvider.Settings.DefaultSession.Files.Count > 0)
                    ImageLIst.SelectedIndex = 0;
            }
            if (ZoomAndPanControl != null)
            {
                ZoomAndPanControl.ContentScaleChanged += ZoomAndPanControl_ContentScaleChanged;
                ZoomAndPanControl.ContentOffsetXChanged += ZoomAndPanControl_ContentScaleChanged;
                ZoomAndPanControl.ContentOffsetYChanged += ZoomAndPanControl_ContentScaleChanged;
            }
        }

        private void ZoomAndPanControl_ContentScaleChanged(object sender, EventArgs e)
        {
            GeneratePreview();
            LayoutViewModel.FreeZoom = ZoomAndPanControl.ContentScale > ZoomAndPanControl.FitScale();
        }

        private void GeneratePreview()
        {
            try
            {
                var bitmap = BitmapLoader.Instance.LoadSmallImage(ServiceProvider.Settings.SelectedBitmap.FileItem);


                if (bitmap != null)
                {
                    if (ZoomAndPanControl == null)
                    {
                        bitmap.Freeze();
                        ServiceProvider.Settings.SelectedBitmap.Preview = bitmap;
                    }
                    else
                    {
                        if (ServiceProvider.Settings.SelectedBitmap.FileItem.IsMovie)
                        {
                            ServiceProvider.Settings.SelectedBitmap.Preview = bitmap;
                            return;
                        }
                        int dw = (int) (ZoomAndPanControl.ContentViewportWidthRation*bitmap.PixelWidth);
                        int dh = (int) (ZoomAndPanControl.ContentViewportHeightRation*bitmap.PixelHeight);
                        int fw = (int) (ZoomAndPanControl.ContentZoomFocusXRation*bitmap.PixelWidth);
                        int fh = (int) (ZoomAndPanControl.ContentZoomFocusYRation*bitmap.PixelHeight);

                        bitmap.FillRectangle2(0, 0, bitmap.PixelWidth, bitmap.PixelHeight,
                            Color.FromArgb(128, 128, 128, 128));
                        bitmap.FillRectangleDeBlend(fw - (dw/2), fh - (dh/2), fw + (dw/2), fh + (dh/2),
                            Color.FromArgb(128, 128, 128, 128));

                        bitmap.Freeze();
                        ServiceProvider.Settings.SelectedBitmap.Preview = bitmap;
                    }
                }
            }
            catch (Exception)
            {

            }

        }

        private void ImageLIst_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                _selectedItem = e.AddedItems[0] as FileItem;
                if (_worker.IsBusy)
                    return;
                FileItem item = e.AddedItems[0] as FileItem;
                if (item != null)
                {
                    ServiceProvider.Settings.SelectedBitmap.SetFileItem(item);
                    _worker.RunWorkerAsync(false);
                }
            }
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (ServiceProvider.Settings.SelectedBitmap.FileItem == null)
                return;
            //bool fullres = e.Argument is bool && (bool) e.Argument ||LayoutViewModel.ZoomFit
            //bool fullres = !LayoutViewModel.ZoomFit;
            bool fullres = e.Argument is bool && (bool) e.Argument;

            ServiceProvider.Settings.ImageLoading = fullres ||
                                                    !ServiceProvider.Settings.SelectedBitmap.FileItem.IsLoaded;
            BitmapLoader.Instance.GenerateCache(ServiceProvider.Settings.SelectedBitmap.FileItem);
            ServiceProvider.Settings.SelectedBitmap.DisplayImage =
                BitmapLoader.Instance.LoadImage(ServiceProvider.Settings.SelectedBitmap.FileItem, fullres);
            ServiceProvider.Settings.SelectedBitmap.Notify();
            Console.WriteLine(fullres);
            BitmapLoader.Instance.SetData(ServiceProvider.Settings.SelectedBitmap,
                              ServiceProvider.Settings.SelectedBitmap.FileItem);
            BitmapLoader.Instance.Highlight(ServiceProvider.Settings.SelectedBitmap,
                                            ServiceProvider.Settings.HighlightUnderExp,
                                            ServiceProvider.Settings.HighlightOverExp);
            ServiceProvider.Settings.SelectedBitmap.FullResLoaded = fullres;
            ServiceProvider.Settings.ImageLoading = false;
            GC.Collect();
            Dispatcher.BeginInvoke(new Action(OnImageLoaded));
        }

        public virtual void OnImageLoaded()
        {
            if (LayoutViewModel.ZoomFit && ZoomAndPanControl != null)
            {
                ZoomAndPanControl.AnimatedScaleToFit();
            }
            else
            {
                ZoomToFocus();
            }
            GeneratePreview();
        }

        public void LoadFullRes()
        {
            if (_worker.IsBusy)
                return;
            if (ServiceProvider.Settings.SelectedBitmap.FullResLoaded)
                return;
            _worker.RunWorkerAsync(true);
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "DefaultSession")
            {
                Thread.Sleep(1000);
                Dispatcher.Invoke(new Action(delegate
                                                 {
                                                     ImageLIst.SelectedIndex = 0;
                                                     if (ImageLIst.Items.Count == 0)
                                                         ServiceProvider.Settings.SelectedBitmap.DisplayImage = null;
                                                 }));
            }
            if (e.PropertyName == "HighlightOverExp")
            {
                if (!_worker.IsBusy)
                {
                    _worker.RunWorkerAsync(false);
                }
            }
            if (e.PropertyName == "HighlightUnderExp")
            {
                if (!_worker.IsBusy)
                {
                    _worker.RunWorkerAsync(false);
                }
            }
            if (e.PropertyName == "ShowFocusPoints")
            {
                if (!_worker.IsBusy)
                {
                    _worker.RunWorkerAsync(false);
                }
            }
            if (e.PropertyName == "FlipPreview")
            {
                if (!_worker.IsBusy)
                {
                    _worker.RunWorkerAsync(false);
                }
            }
            if (e.PropertyName == "LowMemoryUsage")
            {
                if (!_worker.IsBusy)
                {
                    _worker.RunWorkerAsync(false);
                }
            }
        }

        private void Trigger_Event(string cmd, object o)
        {
            ImageLIst.Dispatcher.Invoke(new Action(delegate
                                                       {
                                                           switch (cmd)
                                                           {
                                                               case WindowsCmdConsts.Next_Image:
                                                                   if (ImageLIst.SelectedIndex <
                                                                       ImageLIst.Items.Count - 1)
                                                                   {
                                                                       FileItem item =
                                                                           ImageLIst.SelectedItem as FileItem;
                                                                       if (item != null)
                                                                       {
                                                                           int ind = ImageLIst.Items.IndexOf(item);
                                                                           ImageLIst.SelectedIndex = ind + 1;
                                                                       }
                                                                       item = ImageLIst.SelectedItem as FileItem;
                                                                       if (item != null)
                                                                           ImageLIst.ScrollIntoView(item);
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Prev_Image:
                                                                   if (ImageLIst.SelectedIndex > 0)
                                                                   {
                                                                       FileItem item =
                                                                           ImageLIst.SelectedItem as FileItem;
                                                                       if (item != null)
                                                                       {
                                                                           int ind = ImageLIst.Items.IndexOf(item);
                                                                           ImageLIst.SelectedIndex = ind - 1;
                                                                       }
                                                                       item = ImageLIst.SelectedItem as FileItem;
                                                                       if (item != null)
                                                                           ImageLIst.ScrollIntoView(item);
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Like_Image:
                                                                   if (ImageLIst.SelectedItem != null)
                                                                   {
                                                                       FileItem item = null;
                                                                       if (o != null)
                                                                       {
                                                                           item = ServiceProvider.Settings.DefaultSession.GetByName(o as string);
                                                                       }
                                                                       else
                                                                       {
                                                                           item = ImageLIst.SelectedItem as FileItem;
                                                                       }
                                                                       if (item != null)
                                                                       {
                                                                           item.IsLiked = !item.IsLiked;
                                                                       }
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Unlike_Image:
                                                                   if (ImageLIst.SelectedItem != null)
                                                                   {
                                                                       FileItem item = null;
                                                                       if (o != null)
                                                                       {
                                                                           item =
                                                                               ServiceProvider.Settings.DefaultSession
                                                                                   .GetByName(o as string);
                                                                       }
                                                                       else
                                                                       {
                                                                           item = ImageLIst.SelectedItem as FileItem;
                                                                       }
                                                                       if (item != null)
                                                                       {
                                                                           item.IsUnLiked = !item.IsUnLiked;
                                                                       }
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Del_Image:
                                                                   {
                                                                       DeleteItem();
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Select_Image:
                                                                   FileItem fileItem = o as FileItem;
                                                                   if (fileItem != null)
                                                                   {
                                                                       ImageLIst.SelectedValue = fileItem;
                                                                       ImageLIst.ScrollIntoView(fileItem);
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Refresh_Image:
                                                                   if (!_worker.IsBusy)
                                                                   {
                                                                       _worker.RunWorkerAsync(false);
                                                                   }
                                                                   break;
                                                               case WindowsCmdConsts.Zoom_Image_Fit:
                                                                   ZoomAndPanControl.AnimatedScaleToFit();
                                                                   break;
                                                               case WindowsCmdConsts.Zoom_Image_100:
                                                                   LoadFullRes();
                                                                   ZoomAndPanControl.AnimatedZoomTo(1.0);
                                                                   ZoomToFocus();
                                                                   break;
                                                               case WindowsCmdConsts.Zoom_Image_200:
                                                                   LoadFullRes();
                                                                   ZoomAndPanControl.AnimatedZoomTo(2.0);
                                                                   ZoomToFocus();
                                                                   break;
                                                           }
                                                           if (cmd.StartsWith(WindowsCmdConsts.ZoomPoint))
                                                           {
                                                               if (ZoomAndPanControl!=null &&  cmd.Contains("_"))
                                                               {
                                                                   var vals = cmd.Split('_');
                                                                   if (vals.Count() > 2)
                                                                   {
                                                                       double x;
                                                                       double y;
                                                                       double.TryParse(vals[1], out x);
                                                                       double.TryParse(vals[2], out y);
                                                                       if (cmd.EndsWith("!"))
                                                                           ZoomAndPanControl.SnapToRation(x, y);
                                                                       else
                                                                       {
                                                                           ZoomAndPanControl.AnimatedSnapToRation(x, y);
                                                                       }

                                                                   }
                                                               }
                                                           }
                                                       }));
        }

        private void ZoomToFocus()
        {
            if (!LayoutViewModel.ZoomToFocus)
                return;
            if (
                ServiceProvider.Settings.SelectedBitmap.FileItem
                    .FileInfo != null &&
                ServiceProvider.Settings.SelectedBitmap.FileItem
                    .FileInfo.FocusPoints.Count > 0)
            {
                ZoomAndPanControl.SnapTo(new Point(ServiceProvider.Settings.SelectedBitmap.FileItem.FileInfo.FocusPoints[0].X + ServiceProvider.Settings.SelectedBitmap.FileItem.FileInfo.FocusPoints[0].Width / 2,
                    ServiceProvider.Settings.SelectedBitmap.FileItem.FileInfo.FocusPoints[0].Y + ServiceProvider.Settings.SelectedBitmap.FileItem.FileInfo.FocusPoints[0].Height / 2));
            }
        }

        protected void zoomAndPanControl_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (e.Delta > 0)
            {
                Point curContentMousePoint = e.GetPosition(content);
                ZoomIn(curContentMousePoint);
            }
            else if (e.Delta < 0)
            {
                Point curContentMousePoint = e.GetPosition(content);
                ZoomOut(curContentMousePoint);
            }
            if (ZoomAndPanControl.ContentScale > ZoomAndPanControl.FitScale())
            {
                LoadFullRes();
                LayoutViewModel.FreeZoom = true;
            }
            
        }

        /// <summary>
        /// Event raised on mouse down in the ZoomAndPanControl.
        /// </summary>
        protected void zoomAndPanControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            content.Focus();
            Keyboard.Focus(content);

            mouseButtonDown = e.ChangedButton;
            origZoomAndPanControlMouseDownPoint = e.GetPosition(ZoomAndPanControl);
            origContentMouseDownPoint = e.GetPosition(content);

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
                (e.ChangedButton == MouseButton.Left ||
                 e.ChangedButton == MouseButton.Right))
            {
                // Shift + left- or right-down initiates zooming mode.
                mouseHandlingMode = MouseHandlingMode.Zooming;
            }
            else if (mouseButtonDown == MouseButton.Left)
            {
                // Just a plain old left-down initiates panning mode.
                mouseHandlingMode = MouseHandlingMode.Panning;
            }

            if (mouseHandlingMode != MouseHandlingMode.None)
            {
                // Capture the mouse so that we eventually receive the mouse up event.
                ZoomAndPanControl.CaptureMouse();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised on mouse up in the ZoomAndPanControl.
        /// </summary>
        protected void zoomAndPanControl_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (mouseHandlingMode != MouseHandlingMode.None)
            {
                if (mouseHandlingMode == MouseHandlingMode.Zooming)
                {
                    if (mouseButtonDown == MouseButton.Left)
                    {
                        // Shift + left-click zooms in on the content.
                        ZoomIn(origContentMouseDownPoint);
                    }
                    else if (mouseButtonDown == MouseButton.Right)
                    {
                        // Shift + left-click zooms out from the content.
                        ZoomOut(origContentMouseDownPoint);
                    }
                }
                else if (mouseHandlingMode == MouseHandlingMode.DragZooming)
                {
                }

                ZoomAndPanControl.ReleaseMouseCapture();
                mouseHandlingMode = MouseHandlingMode.None;
                e.Handled = true;
            }
        }

        /// <summary>
        /// Event raised on mouse move in the ZoomAndPanControl.
        /// </summary>
        protected void zoomAndPanControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseHandlingMode == MouseHandlingMode.Panning)
            {
                //
                // The user is left-dragging the mouse.
                // Pan the viewport by the appropriate amount.
                //
                Point curContentMousePoint = e.GetPosition(content);
                Vector dragOffset = curContentMousePoint - origContentMouseDownPoint;

                ZoomAndPanControl.ContentOffsetX -= dragOffset.X;
                ZoomAndPanControl.ContentOffsetY -= dragOffset.Y;

                e.Handled = true;
            }
            else if (mouseHandlingMode == MouseHandlingMode.Zooming)
            {
                Point curZoomAndPanControlMousePoint = e.GetPosition(ZoomAndPanControl);
                Vector dragOffset = curZoomAndPanControlMousePoint - origZoomAndPanControlMouseDownPoint;
                double dragThreshold = 10;
                if (mouseButtonDown == MouseButton.Left &&
                    (Math.Abs(dragOffset.X) > dragThreshold ||
                     Math.Abs(dragOffset.Y) > dragThreshold))
                {
                    //
                    // When Shift + left-down zooming mode and the user drags beyond the drag threshold,
                    // initiate drag zooming mode where the user can drag out a rectangle to select the area
                    // to zoom in on.
                    //
                    mouseHandlingMode = MouseHandlingMode.DragZooming;

                }

                e.Handled = true;
            }
            else if (mouseHandlingMode == MouseHandlingMode.DragZooming)
            {

            }
        }


        /// <summary>
        /// Zoom the viewport out, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomOut(Point contentZoomCenter)
        {
            ZoomAndPanControl.ZoomAboutPoint(ZoomAndPanControl.ContentScale - 0.1, contentZoomCenter);
        }

        /// <summary>
        /// Zoom the viewport in, centering on the specified point (in content coordinates).
        /// </summary>
        private void ZoomIn(Point contentZoomCenter)
        {
            ZoomAndPanControl.ZoomAboutPoint(ZoomAndPanControl.ContentScale + 0.1, contentZoomCenter);
        }

        public void Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            MediaElement.Play();
        }

        public void Button_Click_1(object sender, System.Windows.RoutedEventArgs e)
        {
            MediaElement.Pause();
        }

        public void Button_Click_2(object sender, System.Windows.RoutedEventArgs e)
        {
            MediaElement.Stop();
        }
    }
}