using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Net.Http;
using System.Diagnostics;

namespace WPFGeoServer.Classes
{
    public class WMSManager
    {
        public static WMSManager Instance { get; } = new WMSManager();

        private readonly HttpClient client = new HttpClient();

        private readonly string tileSaveDirectory = Path.Combine(Environment.CurrentDirectory, "SavedTiles");

        public async Task<BitmapImage> LoadTileImageAsync(Point startGPS, Point endGPS, Size tileSize, bool isDownload = false)
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

                if (imageData == null || imageData.Length == 0)
                {
                    Console.WriteLine("[이미지 데이터 없음]");
                    return null;
                }

                if (isDownload)
                    SaveTileImage(imageData, $"tile_{startGPS.X:F5}_{startGPS.Y:F5}.png");

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

        private void SaveTileImage(byte[] imageData, string fileName)
        {
            try
            {
                if (!Directory.Exists(tileSaveDirectory))
                    Directory.CreateDirectory(tileSaveDirectory);

                string path = Path.Combine(tileSaveDirectory, fileName);
                File.WriteAllBytes(path, imageData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[타일 저장 실패] {fileName}: {ex.Message}");
            }
        }
    }
}
