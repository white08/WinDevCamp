﻿using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PhotoFilter.Win10
{
    class ServerImageData
    {
        public string Metadata;
        public string Name;
        public string FullImage;
        public string Thumbnail;
        public int Size;
        public int Orientation;
        public double Latitude;
        public double Longitude;
    }

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        const bool ProcessImagesOnStartup = true;
        int NumberofImageCopies { get { return 1; } }
        ConcurrentBag<ImageItem> m_images;

        public MainPage()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when this page is about to be displayed in a Frame.
        /// </summary>
        /// <param name="e">Event data that describes how this page was reached.  The Parameter
        /// property is typically used to configure the page.</param>
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            ImageListSource.IsSourceGrouped = false;
            await LoadImages();
        }

        private async Task LoadImages()
        {
            progressBar.Visibility = Visibility.Visible;
            imgSelectedImage.Visibility = Visibility.Collapsed;
            m_images = new ConcurrentBag<ImageItem>();

            await GetImagesFromCloud();
            await LoadImagesFromDisk(KnownFolders.PicturesLibrary);
            ImageListSource.Source = from image in
                m_images
                                     orderby image.Folder.Name
                                     select image;

            progressBar.Visibility = Visibility.Collapsed;
            imgSelectedImage.Visibility = Visibility.Visible;
        }

        private async Task GetImagesFromCloud()
        {
            // Get images list from server
            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.GetAsync(ServerUrl + "/api/Images");
            response.EnsureSuccessStatusCode();
            string result = await response.Content.ReadAsStringAsync();
            ServerImageData[] pictureList = JsonConvert.DeserializeObject<ServerImageData[]>(result);

            var folder = KnownFolders.PicturesLibrary;
            folder = await folder.GetOrCreateFolderAsync("Cloud");

            // Download thumbnails
            var tasks = new List<Task>();
            foreach (var image in pictureList)
            {
                string fileName = image.Thumbnail;
                string imageUrl = ServerUrl + "/Images/" + fileName;
                tasks.Add(DownloadImageAsync(new Uri(imageUrl), folder, fileName));
                //await DownloadImageAsync(new Uri(imageUrl), folder, fileName);
            }
            await Task.WhenAll(tasks);
        }

        async Task LoadImagesFromDisk(StorageFolder currentFolder)
        {
            if (currentFolder.Name == "Cloud")
            {
                return;
            }
            var subFolderQuery = currentFolder.CreateFolderQuery();
            var folders = await subFolderQuery.GetFoldersAsync();

            //Get the files out of the current folder
            //await processImageFilesInParallel(currentFolder);
            await processImageFiles(currentFolder);

            //Find any sub folders
            foreach (var folder in folders)
            {
                await LoadImagesFromDisk(folder);
            }
        }

        async Task processImageFiles(StorageFolder folder)
        {
            var fileQuery = await folder.GetFilesAsync();
            foreach (var file in fileQuery)
            {
                ImageItem item = new ImageItem(file);
                await item.LoadImageFromDisk();
                m_images.Add(item);
            }
        }

        async private void pictureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ImageItem i = (ImageItem)pictureList.SelectedItem;
            WriteableBitmap bitmap = await i.GetPictureAsync();
            double TargetHeight = this.ActualHeight - 20;// - btnLocal.ActualHeight - 10;
            double TargetWidth = this.ActualWidth - 20;// - pictureList.ActualWidth - 10;
            imgSelectedImage.Height = TargetHeight; // bitmap.PixelHeight;
            imgSelectedImage.Width = TargetWidth; // bitmap.PixelWidth;
            imgSelectedImage.Source = bitmap;
        }

        //private string ServerUrl = "http://localhost:20476";
        private string ServerUrl = "http://photoimageserver.azurewebsites.net";


        public async Task<StorageFile> DownloadImageAsync(Uri fileUri, StorageFolder folder, string fileName)
        {
            //await Task.Delay(1000);
            //return null;
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            var downloader = new BackgroundDownloader();
            var download = downloader.CreateDownload(fileUri, file);
            var res = await download.StartAsync();

            ImageItem item = new ImageItem(file);
            await item.LoadImageFromDisk();
            m_images.Add(item);
            return file;
        }

        async private void buttonSync_Click(object sender, RoutedEventArgs e)
        {
            //Constants.Instrumentation.LogMessage("Sync Click Start", LoggingLevel.Information);
            var nativeObject = new PhotoFilterLib_UAP.ImageFilter();

            WriteableBitmap bitmap = (WriteableBitmap)imgSelectedImage.Source;
            IBuffer pixelBuffer = bitmap.PixelBuffer;

            byte[] rawPixelArray = new byte[bitmap.PixelHeight * bitmap.PixelWidth * 4];
            Stream tempStream = bitmap.PixelBuffer.AsStream();
            tempStream.Read(rawPixelArray, 0, rawPixelArray.Length);

            //Constants.Instrumentation.LogMessage("Antique Image Start", LoggingLevel.Information);
            rawPixelArray = nativeObject.AntiqueImage(rawPixelArray);

            //Constants.Instrumentation.LogMessage("Antique Image Start", LoggingLevel.Information);

            await updateImage(bitmap, rawPixelArray);
            //Constants.Instrumentation.LogMessage("Sync Click Stop", LoggingLevel.Information);
        }

        async private Task updateImage(WriteableBitmap bitmap, byte[] newPixels)
        {
            using (Stream stream = bitmap.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(newPixels, 0, newPixels.Length);
            }
            bitmap.Invalidate();
        }
    }
}
