﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WPFGeoServer
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string _htmlPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "map.html");

        private static readonly HttpClient client = new HttpClient();

        private int[] mapScaleArray = { 1000, 2500, 5000, 10000, 25000, 50000 };
        private readonly double[] gpsSizeArray = { 0.005, 0.0125, 0.025, 0.05, 0.125, 0.25 };
        private double tileSizeGps;
        private string[] lenArray = { "F3", "F4", "F3", "F2", "F3", "F2" };

        private int zoomLevel = 2;
        private readonly Size tileSize = new Size(1024, 1024);

        private Point referenceTileGPS;
        private Point currentCenterGPS;
        private bool isPanning = false;
        private Point panStart;

        private readonly Dictionary<int, Dictionary<string, Image>> tileCache = new Dictionary<int, Dictionary<string, Image>>();

        private TranslateTransform PanTransform = new TranslateTransform(0, 0);

        public MainWindow()
        {
            InitializeComponent();

            var group = new TransformGroup();
            group.Children.Add(PanTransform);
            MapCanvas.RenderTransform = group;

            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            Loaded += async (s, e) =>
            {
                currentCenterGPS = new Point(127.36, 36.34);
                await LoadTilesAsync(currentCenterGPS);
            };
        }

        private Point GPSToCanvas(Point gps)
        {
            double tileSizeGps = gpsSizeArray[zoomLevel];

            double dx = (gps.X - currentCenterGPS.X) / tileSizeGps * tileSize.Width;
            double dy = (currentCenterGPS.Y - gps.Y) / tileSizeGps * tileSize.Height;

            return new Point(
                MapCanvas.ActualWidth / 2 + dx,
                MapCanvas.ActualHeight / 2 + dy);
        }

        private Point CanvasToGPS(Point canvasPos)
        {
            GeneralTransform transform = MapCanvas.RenderTransform.Inverse;
            Point logical = transform.Transform(canvasPos);

            double tileSizeGps = gpsSizeArray[zoomLevel];

            double dx = logical.X - MapCanvas.ActualWidth / 2;
            double dy = logical.Y - MapCanvas.ActualHeight / 2;

            return new Point(
                currentCenterGPS.X + dx / tileSize.Width * tileSizeGps,
                currentCenterGPS.Y - dy / tileSize.Height * tileSizeGps);
        }

        private double Normalize(double value) => Math.Round(value, 5);


        public async Task LoadTilesAsync(Point centerGPS)
        {
            MapCanvas.Children.Clear();

            if (!tileCache.ContainsKey(zoomLevel))
                tileCache[zoomLevel] = new Dictionary<string, Image>();

            var currentCache = tileCache[zoomLevel];
            tileSizeGps = gpsSizeArray[zoomLevel];

            int tilesX = (int)Math.Ceiling(MapCanvas.ActualWidth / tileSize.Width) + 2;
            int tilesY = (int)Math.Ceiling(MapCanvas.ActualHeight / tileSize.Height) + 2;

            int centerTileX = (int)Math.Floor(centerGPS.X / tileSizeGps);
            int centerTileY = (int)Math.Floor(centerGPS.Y / tileSizeGps);

            tilesX = 3; tilesY = 3; // 테스트용

            double offsetX = tilesX / 2;
            double offsetY = tilesY / 2;

            Point centerTileBottomLeft = new Point(
                centerGPS.X - tileSizeGps / 2.0,
                centerGPS.Y - tileSizeGps / 2.0);

            Point centerTileCenterGPS = new Point(
                centerTileBottomLeft.X + tileSizeGps / 2,
                centerTileBottomLeft.Y + tileSizeGps / 2);

            referenceTileGPS = new Point(
                centerTileBottomLeft.X - offsetX * tileSizeGps,
                centerTileBottomLeft.Y - offsetY * tileSizeGps);

            for (int y = 0; y < tilesY; y++)
            {
                for (int x = 0; x < tilesX; x++)
                {
                    Point tileStart = new Point(
                        Normalize(referenceTileGPS.X + x * tileSizeGps),
                        Normalize(referenceTileGPS.Y + y * tileSizeGps));
                    Point tileEnd = new Point(
                        Normalize(tileStart.X + tileSizeGps),
                        Normalize(tileStart.Y + tileSizeGps));

                    string key = $"{tileStart.X:F5}_{tileStart.Y:F5}";

                    Point center = new Point(
                          Normalize(tileStart.X + tileSizeGps / 2),
                          Normalize(tileStart.Y + tileSizeGps / 2));
                    Point pos = GPSToCanvas(center);

                    if (currentCache.TryGetValue(key, out var existing))
                    {
                        Canvas.SetLeft(existing, Math.Floor(pos.X - tileSize.Width / 2));
                        Canvas.SetTop(existing, Math.Floor(pos.Y - tileSize.Height / 2));
                        if (!MapCanvas.Children.Contains(existing))
                            MapCanvas.Children.Add(existing);
                        continue;
                    }

                    var image = await LoadTileImageAsync(tileStart, tileEnd);
                    if (image == null) continue;

                    var img = new Image
                    {
                        Source = image,
                        Width = tileSize.Width,
                        Height = tileSize.Height
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

                    Point canvasPos = GPSToCanvas(new Point(
                        Normalize(tileStart.X + tileSizeGps / 2),
                        Normalize(tileStart.Y + tileSizeGps / 2)));

                    Canvas.SetLeft(img, Math.Floor(canvasPos.X - tileSize.Width / 2));
                    Canvas.SetTop(img, Math.Floor(canvasPos.Y - tileSize.Height / 2));

                    MapCanvas.Children.Add(img);

                    currentCache[key] = img;
                }
            }

            Debug.WriteLine($"Zoom Level: {zoomLevel}, Center GPS: {centerGPS}, Center Tile Center GPS: {centerTileCenterGPS}");

            currentCenterGPS = centerGPS;
        }


        private async Task<BitmapImage> LoadTileImageAsync(Point startGPS, Point endGPS)
        {
            string bbox = $"{Math.Min(startGPS.X, endGPS.X)},{Math.Min(startGPS.Y, endGPS.Y)}," +
              $"{Math.Max(startGPS.X, endGPS.X)},{Math.Max(startGPS.Y, endGPS.Y)}";

            string ip = "localhost:8080";
            string layerNames = "HJ:DJ_SJ";
            string url = $"http://{ip}/geoserver/HJ/wms";
            string wmsUrl = $"{url}?service=WMS&version=1.1.1&request=GetMap&" +
                            $"layers={layerNames}&bbox={bbox}&" +
                            $"width={(int)tileSize.Width}&height={(int)tileSize.Height}&srs=EPSG:4326&format=image/png&transparent=true";

            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WPFClient/1.0");

                var response = await client.GetAsync(wmsUrl);
                if (!response.IsSuccessStatusCode) return null;

                var imageData = await response.Content.ReadAsByteArrayAsync();
                var ms = new MemoryStream(imageData.ToArray());

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                return bmp;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[예외 발생] " + ex.Message);
                return null;
            }
        }

        private async Task UpdateCurrentCenterGPS()
        {
            Point canvasCenter = new Point(MapCanvas.ActualWidth / 2, MapCanvas.ActualHeight / 2);
            Point newCenter = CanvasToGPS(canvasCenter);

            PanTransform.X = 0;
            PanTransform.Y = 0;

            currentCenterGPS = newCenter;

            Debug.WriteLine($"Updated CenterGPS: {currentCenterGPS}");

            await LoadTilesAsync(currentCenterGPS);
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                isPanning = true;
                panStart = e.GetPosition(this);
                MapCanvas.CaptureMouse();
                Cursor = Cursors.Hand;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                Point current = e.GetPosition(this);
                Vector delta = current - panStart;
                panStart = current;

                PanTransform.X += delta.X;
                PanTransform.Y += delta.Y;
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isPanning = false;
            MapCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;

            UpdateCurrentCenterGPS();

            Point canvasPoint = e.GetPosition(MapCanvas);
            Point gps = CanvasToGPS(canvasPoint);
            //MessageBox.Show($"GPS: 경도 {gps.X:F6}, 위도 {gps.Y:F6}");
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            isPanning = false;
            MapCanvas.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;

            UpdateCurrentCenterGPS();
            Debug.WriteLine($"CurrentCenterGPS: {currentCenterGPS}");
        }

        private async void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            int prevZoom = zoomLevel;
            if (e.Delta > 0 && zoomLevel > 0) zoomLevel--;
            else if (e.Delta < 0 && zoomLevel < gpsSizeArray.Length - 1) zoomLevel++;
            else return;

            Point canvasCenter = new Point(MapCanvas.ActualWidth / 2, MapCanvas.ActualHeight / 2);
            Point gpsBefore = CanvasToGPS(canvasCenter);

            Debug.WriteLine($"ZoomLevel: {zoomLevel}, canvasCenter: {canvasCenter}, GPS before zoom: {gpsBefore}");

            PanTransform.X = 0;
            PanTransform.Y = 0;

            await LoadTilesAsync(gpsBefore);
        }
    }
}
