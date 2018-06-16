﻿using Jacksonsoft;
using Microsoft.Win32;
//using mshtml;
using OSIRT.Browser.DownloadManager;
using OSIRT.Helpers;
using OSIRT.Loggers;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Net;
using OSIRT.Extensions;
using OSIRT.UI.DownloadClient;
using OSIRT.UI.ViewSource;
using OSIRT.UI;
using CefSharp.WinForms;
using System.Threading;
using System.Drawing.Imaging;
using CefSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using HtmlAgilityPack;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace OSIRT.Browser
{
    public class ExtendedBrowser : ChromiumWebBrowser
    {
        public event EventHandler ScreenshotCompleted = delegate { };
        public event EventHandler DownloadingProgress = delegate { };
        public event EventHandler DownloadComplete = delegate { };
        public event EventHandler NewTab = delegate { };
        public event EventHandler ViewPageSource = delegate { };
        public event EventHandler SavePageSource = delegate { };
        public event EventHandler OpenNewTabContextMenu = delegate { };
        public event EventHandler YouTubeDownloadProgress = delegate { };
        public event EventHandler YouTubeDownloadComplete = delegate { };
        public event EventHandler OnLoadingStateChanged = delegate { };
        public event EventHandler OpenTinEye = delegate { };
        public event EventHandler DownloadStatusChanged = delegate { };
        public event EventHandler DownloadCompleted = delegate { };
        public event EventHandler AddBookmark = delegate { };
        public event EventHandler SearchText = delegate { };

        private int MaxScrollHeight => 15000;
        private readonly int MaxWait = 600;
        private RequestHandler requestHandler;

        public string Title { get; private set; }

        private PictureBox mouseTrail = new PictureBox();
        private static System.Timers.Timer cursorTimer;

        public ExtendedBrowser() : base(UserSettings.Load().Homepage)
        {

            //InitialiseMouseTrail();
            var handler = new MenuHandler();
            handler.DownloadImage += Handler_DownloadImage;
            handler.ViewPageSource += Handler_ViewPageSource;
            handler.DownloadYouTubeVideo += Handler_DownloadYouTubeVideo;
            handler.ViewImageExif += Handler_ViewImageExif;
            handler.ViewFacebookIdNum += Handler_ViewFacebookIdNum;
            handler.ViewTwitterIdNum += Handler_ViewTwitterIdNum;
            handler.CopyImageLocation += Handler_CopyImageLocation; ;
            handler.OpenInNewTabContextMenu += Handler_OpenInNewTabContextMenu;
            handler.ReverseImgSearch += Handler_ReverseImgSearch;
            handler.ExtractLinks += Handler_ExtractLinks;
            handler.AddPageToBookmarks += Handler_AddPageToBookmarks;
            handler.SearchText += Handler_SearchText;
            handler.SaveSelectedText += Handler_SaveSelectedText;
            handler.UrlInToDoList += Handler_UrlInToDoList;
           
            MenuHandler = handler;
            LoadingStateChanged += ExtendedBrowser_LoadingStateChanged;

            var downloadHandler = new DownloadHandler();

            DownloadHandler = downloadHandler;
            downloadHandler.DownloadUpdated += DownloadHandler_DownloadUpdated;
            downloadHandler.DownloadCompleted += DownloadHandler_DownloadCompleted;

            requestHandler = new RequestHandler();
            RequestHandler = requestHandler;
            KeyboardHandler = new KeyboardHandler();
            TitleChanged += ExtendedBrowser_TitleChanged;
            if (OsirtHelper.DisableWebRtc)
            {
                IsBrowserInitializedChanged += ExtendedBrowser_IsBrowserInitializedChanged;
            }
            
        }

        private void Handler_UrlInToDoList(object sender, EventArgs e)
        {
            string url = ((NewTabEventArgs)e).Url;
            OsirtHelper.toDo.Add(url);
        }

        public HashSet<RequestWrapper> ResourcesSet()
        {
            return requestHandler.Resources;
        }

        public List<HeaderWrapper> ResponseHeaders()
        {
            return requestHandler.ResponseHeaders;
        }

        public List<HeaderWrapper> RequestHeaders()
        {
            return requestHandler.RequestHeaders;
        }


        private void Handler_SaveSelectedText(object sender, EventArgs e)
        {
            try
            {
                File.WriteAllText(Constants.TempTextFile, ((TextEventArgs)e).Result);
                this.InvokeIfRequired(() => new TextPreviewer(Enums.Actions.Text, URL).Show());
            }
            catch { MessageBox.Show("Error saving selected text.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void Handler_SearchText(object sender, EventArgs e)
        {
            SearchText?.Invoke(this, (TextEventArgs)e);
        }

        private void ExtendedBrowser_IsBrowserInitializedChanged(object sender, IsBrowserInitializedChangedEventArgs e)
        {
            var browserHost =   GetBrowser().GetHost();
            var requestContext = browserHost.RequestContext;
            string errorMessage = "";
            
            requestContext.SetPreference("enable_do_not_track", true, out errorMessage);
            //see: http://magpcss.org/ceforum/viewtopic.php?f=14&t=15194 post by fddima for webrtc stuff
            requestContext.SetPreference("disable-webrtc", true, out errorMessage);
            requestContext.SetPreference("enable-webrtc", false, out errorMessage);
            requestContext.SetPreference("webrtc.multiple_routes_enabled", false, out errorMessage);
            requestContext.SetPreference("webrtc.nonproxied_udp_enabled", false, out errorMessage);
            requestContext.SetPreference("webrtc.ip_handling_policy", "disable_non_proxied_udp", out errorMessage);

            //other useful webrtc links
            //http://magpcss.org/ceforum/viewtopic.php?f=14&t=13350
            //https://github.com/cefsharp/CefSharp/issues/2059

            //set prefs on init: https://github.com/cefsharp/CefSharp/blob/master/CefSharp.WinForms.Example/BrowserTabUserControl.cs
        }

        private void ExtendedBrowser_TitleChanged(object sender, TitleChangedEventArgs e)
        {
            Title = e.Title;
        }

        private void Handler_AddPageToBookmarks(object sender, EventArgs e)
        {
            AddBookmark?.Invoke(this, EventArgs.Empty);
        }

        private void ExtendedBrowser_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (!cursorTimer.Enabled)
                    return;
                Debug.WriteLine(Cursor.Position.X + " " + Cursor.Position.X);
                this.InvokeIfRequired(() => mouseTrail.Location = new Point(PointToClient(Cursor.Position).X + 5, PointToClient(Cursor.Position).Y + 5)); //new Point(Cursor.Position.X, Cursor.Position.Y - 95));
            }
                //This works ok, but only need to create one per application, no per tab.
            //make the timer static?
           
        }

        private void DownloadHandler_DownloadCompleted(object sender, EventArgs e)
        {
            DownloadCompleted?.Invoke(this, e);
        }

        private void DownloadHandler_DownloadUpdated(object sender, EventArgs e)
        {
            DownloadStatusChanged?.Invoke(this, e);
        }

        private async void Handler_ExtractLinks(object sender, EventArgs e)
        {
            
            try
            {
                string source = await GetBrowser().MainFrame.GetSourceAsync();
                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(source);
                string links = "";
                foreach (HtmlNode link in doc.DocumentNode.SelectNodes("//a[@href]"))
                {
                    string value = link.Attributes["href"].Value;
                    if (value == "#") continue;
                    links += value + "\n";
                }
                File.WriteAllText(Constants.TempTextFile, links);
                this.InvokeIfRequired(() => new TextPreviewer(Enums.Actions.Links, URL).Show());
            }
            catch
            {
                MessageBox.Show("Unable to extract any links from this page.", "No links to extract", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } 
        }

        private void Handler_ReverseImgSearch(object sender, EventArgs e)
        {
            OpenTinEye?.Invoke(this, e);
        }

        private void Handler_CopyImageLocation(object sender, EventArgs e)
        {
            string link = ((TextEventArgs)e).Result;
            Clipboard.SetText(link);
        }

        private void ExtendedBrowser_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e)
        {
            OnLoadingStateChanged?.Invoke(this, e);
        }

        private void Handler_OpenInNewTabContextMenu(object sender, EventArgs e)
        {
            OpenNewTabContextMenu?.Invoke(this, (NewTabEventArgs)e);
        }

        private  void Handler_ViewTwitterIdNum(object sender, EventArgs e)
        {
            GetAndDisplayId(new TwitterIdFinder(), "Twitter");
        }

        private  void Handler_ViewFacebookIdNum(object sender, EventArgs e)
        {

            GetAndDisplayId(new FacebookIdFinder(), "Facebook");
        }

        private async void GetAndDisplayId(IIdFinder finder, string title)
        {
            string source = await GetBrowser().MainFrame.GetSourceAsync();
            string id =  finder.FindId(source);
            this.InvokeIfRequired(() => new IdDetailsForm(id, title).Show());
        }

        private void Handler_ViewImageExif(object sender, EventArgs e)
        {
            string path = ((TextEventArgs)e).Result;
            WebClient webClientexif = new WebClient();
            string file = Path.Combine(Constants.CacheLocation, Path.GetFileName(OsirtHelper.StripQueryFromPath(path)));
            webClientexif.DownloadFileAsync(new Uri(path), file, file);
            webClientexif.DownloadFileCompleted += (snd, evt) =>
            {
               this.InvokeIfRequired(() =>  new ExifViewer(evt.UserState.ToString(), path).Show());
            };
            
        }

        private void ExtendedBrowser_MouseMove(object sender, MouseEventArgs e)
        {
            if (UserSettings.Load().ShowMouseTrail)
            {
                mouseTrail.Location = new Point(e.X + 5, e.Y + 5);
                Debug.WriteLine($"{e.X + 5} {e.Y + 5}");
            }
        }


        private async void Handler_ViewPageSource(object sender, EventArgs e)
        {
            string source = await GetBrowser().MainFrame.GetSourceAsync();
            File.WriteAllText(Constants.TempTextFile, source);
            this.InvokeIfRequired(() => new TextPreviewer(Enums.Actions.Source, URL).Show());
        }

        private void Handler_DownloadImage(object sender, EventArgs e)
        {
            DownloadFile(((DownloadImageViaContextMenuEventArgs)e).Url);
        }

        public bool MouseTrailVisible
        {
            get
            {
                return mouseTrail.Visible;
            }
            set
            {
                mouseTrail.Visible = value;
            }

        }


        readonly object _lock = new object();
        public void DisableMouseTrail()
        {
            //deadlocking... investigate later.
            lock (_lock)
            {
                cursorTimer.Enabled = false;
            }
          
                //cursorTimer.Elapsed -= ExtendedBrowser_Elapsed;
                MouseTrailVisible = false;
                Controls.Remove(mouseTrail);
        
        }

        public void InitialiseMouseTrail()
        {
            mouseTrail.BackColor = Color.Green;
            mouseTrail.Size = new Size(12, 12);
            Controls.Add(mouseTrail);
            MouseTrailVisible = true;
        }

        public void StartMouseTrailTimer()
        {
            cursorTimer = new System.Timers.Timer();
            cursorTimer.Elapsed += ExtendedBrowser_Elapsed;
            cursorTimer.Interval = 25;
            cursorTimer.Enabled = true;


            /*
                this does inject a square into the document to follow the cursor when recording the screen.
                HOWEVER: this works on a _per document_ basis, meaning every time a document is loaded,
                this JS has to be injected into it again.

                Obviously, there can be many tabs open, all loading different documents so this method
                will have to be called every single time.
                Not ideal, so it's not used anywhere. 
                
                Here for future reference, when a good solution can be found.
            */
            //var task =  GetBrowser().MainFrame.EvaluateScriptAsync(

            //    @"var followCursor = (function() { 
            //        var s = document.createElement('div');
            //        s.style.position = 'absolute';
            //        s.style.margin = '0';
            //        s.style.padding = '5px';
            //        s.style.border = '1px solid red';
            //        s.style.backgroundColor = 'red';

            //        return {
            //            init: function() {
            //                document.body.appendChild(s);
            //            },

            //        run: function(e) {
            //                            var e = e || window.event;
            //            s.style.left  = (e.clientX + 5) + 'px';
            //            s.style.top = (e.clientY + 5) + 'px';
            //            }
            //        };
            //    }());

            //    (function()
            //    {
            //        followCursor.init();
            //        document.body.onmousemove = followCursor.run;
            //    })();"
            //);
            //task.Wait();
        }

        /// <summary>
        /// Gets the current viewport of the browser
        /// </summary>
        /// <returns>A Bitmap of the current browser viewport</returns>
        private Bitmap GetCurrentViewScreenshot()
        {
            int width, height;
            //ORIGINAL:
            //width = ClientRectangle.Width;
            //height = ClientRectangle.Height;

            //try DisplayRectangle instead... Issue with cutting off some of the image
            //width = DisplayRectangle.Width;
            //height = DisplayRectangle.Height;

            width = ClientSize.Width;
            height = ClientSize.Height;

            Debug.WriteLine("Client rect width: " + width + " height: " + height);
            using (Bitmap image = new Bitmap(width, height))
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    Point p, upperLeftDestination;
                    Point upperLeftSource = new Point(0, 0);
                    p = new Point(0, 0);
                    upperLeftSource = PointToScreen(p);
                    upperLeftDestination = new Point(0, 0);
                    Size blockRegionSize = ClientRectangle.Size;
                    graphics.CopyFromScreen(upperLeftSource, upperLeftDestination, blockRegionSize);

                }
                return new Bitmap(image);
            }
        }

        public void GetCurrentViewportScreenshot()
        {
            ScreenshotHelper.SaveScreenshotToCache(GetCurrentViewScreenshot());
            FireScreenshotCompleteEvent(true);
        }

        public async Task PutTaskDelay()
        {
            await Task.Delay(MaxWait);
        }

       private int GetDocHeight()
        {
            try
            {
                int scrollHeight = 0;
                var task = GetBrowser()?.MainFrame?.EvaluateScriptAsync("(function() { var body = document.body, html = document.documentElement; return  Math.max( body.scrollHeight, body.offsetHeight, html.clientHeight, html.scrollHeight, html.offsetHeight ); })();", null);
                task.Wait();
                var response = task.Result;
                scrollHeight = (int)response.Result;
                return scrollHeight;
            }
            catch
            {
                MessageBox.Show("Unable to take full page capture. Consider using video capture, snippet or current view capture tools.", "Unable to take full page screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
        }

        private /*async*/ void FullpageScreenshotByScrolling()
        {
            
            int scrollHeight = GetDocHeight();
            if (scrollHeight == 0)
            {
                FireScreenshotCompleteEvent(false);
                return;
            }

            Console.WriteLine("-------------SCROLL HEIGHT:------------" + scrollHeight);
            Console.WriteLine("Client rect height: " + ClientRectangle.Size.Height);
            Console.WriteLine("Control height: " + Height);
            Console.WriteLine("Display Rect height: " +  DisplayRectangle.Height);
            Console.WriteLine("Client size height: " + ClientSize.Height);

          

            Enabled = false;
            int viewportHeight = ClientRectangle.Size.Height; 
            int viewportWidth = ClientRectangle.Size.Width;

            //GetBrowser().MainFrame.ExecuteJavaScriptAsync("(function() { document.documentElement.style.overflow = 'hidden'; })();");
            int count = 0;
            int pageLeft = scrollHeight;
            bool atBottom = false;
            Console.WriteLine($"OUTSIDE --- PAGE LEFT: {pageLeft}. VIEWPORT HEIGHT: {viewportHeight}");
            ImageDiskCache cache = new ImageDiskCache();

            while (!atBottom)
            {
                if (pageLeft > viewportHeight)
                {
                    string js = "(function() { window.scroll(0," + (count * viewportHeight) + "); })();";
                    //await GetBrowser().MainFrame.EvaluateScriptAsync("(function() { window.scroll(0," + (count * viewportHeight) + "); })();");
                    GetBrowser().MainFrame.ExecuteJavaScriptAsync(js);
                    count++;
                    //await PutTaskDelay();  //we do need these delays. Some pages, like facebook, may need to load viewport content.
                    Thread.Sleep(500);
                    using (Bitmap image = GetCurrentViewScreenshot())
                    {
                        cache.AddImage(count, image);
                    }

                    GetBrowser().MainFrame.ExecuteJavaScriptAsync("(function() { var elements = document.querySelectorAll('*'); for (var i = 0; i < elements.length; i++) { var position = window.getComputedStyle(elements[i]).position; if (position === 'fixed') { elements[i].style.visibility = 'hidden'; } } })(); ");

                    //if (!OsirtHelper.IsOnGoogle(URL))
                    //    await GetBrowser().MainFrame.EvaluateScriptAsync("(function() { var elements = document.querySelectorAll('*'); for (var i = 0; i < elements.length; i++) { var position = window.getComputedStyle(elements[i]).position; if (position === 'fixed') { elements[i].style.visibility = 'hidden'; } } })(); ");
                }
                else 
                {
                    //find out what's left of the page to scroll, then take screenshot
                    //if it's the last image, we're going to need to crop what we need, as it'll take
                    //a capture of the entire viewport.


                    // await GetBrowser().MainFrame.EvaluateScriptAsync("(function() { window.scrollBy(0," + pageLeft + "); })();");
                    GetBrowser().MainFrame.ExecuteJavaScriptAsync("(function() { window.scrollBy(0," + pageLeft + "); })();");
                    atBottom = true;
                    count++;

                    //await PutTaskDelay();
                    Thread.Sleep(500);
                    Rectangle cropRect = new Rectangle(new Point(0, viewportHeight - pageLeft), new Size(viewportWidth, pageLeft));

                    using (Bitmap src = GetCurrentViewScreenshot())
                    using (Bitmap target = new Bitmap(cropRect.Width, cropRect.Height))
                    using (Graphics g = Graphics.FromImage(target))
                    {
                        g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height), cropRect, GraphicsUnit.Pixel);
                        cache.AddImage(count, target);
                    }
                  
                }

                pageLeft = pageLeft - viewportHeight;
                Debug.WriteLine($"IN WHILE --- PAGE LEFT: {pageLeft}. VIEWPORT HEIGHT: {viewportHeight}");
            }//end while
            GetBrowser().MainFrame.ExecuteJavaScriptAsync("(function() { document.documentElement.style.overflow = 'auto'; })();");
            GetBrowser().MainFrame.ExecuteJavaScriptAsync("javascript:var s = function() { document.body.scrollTop = document.documentElement.scrollTop = 0;}; s();");
            if (!OsirtHelper.IsOnGoogle(URL))
                GetBrowser().MainFrame.ExecuteJavaScriptAsync("(function() { var elements = document.querySelectorAll('*'); for (var i = 0; i < elements.length; i++) { var position = window.getComputedStyle(elements[i]).position; if (position === 'fixed') { elements[i].style.visibility = 'visible'; } } })(); ");
            Enabled = true;
            WaitWindow.Show(GetScreenshot, Resources.strings.CombineScreenshots);
            FireScreenshotCompleteEvent(true);
        }

        private void GetScreenshot(object sender, WaitWindowEventArgs e)
        {
            DirectoryInfo directory = new DirectoryInfo(Constants.CacheLocation);
            FileSystemInfo[] files = directory.GetFileSystemInfos();
            ScreenshotHelper.CombineScreenshot(files, e);
        }

        public string URL => Address;

        public void GenerateFullpageScreenshot()
        {
            try
            {
                FullpageScreenshotByScrolling();
            }
            catch
            {
                MessageBox.Show("Unable to take a full page capture. Please use video capture, snippet or current view screenshot.", "Cannot take fullpage screenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Dock = DockStyle.Fill;
            }

        }

        private void FireScreenshotCompleteEvent(bool successful)
        {
            ScreenshotCompleted(this, new ScreenshotCompletedEventArgs(successful));
        }


        private void DownloadAllImages_Click(object sender, EventArgs e)
        {
            var files = new WebImageDownloader(URL).GetSafeUrls();

            if (files.Count == 0)
            {
                MessageBox.Show("There are either no images to download from this page, or OSIRT is unable to download them.", "No Images to Download", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            using (var downloader = new DownloadForm(files, Enums.Actions.Scraped))
            {
                downloader.ShowDialog();
            }
        }



        #region YouTube downloading

        private async void Handler_DownloadYouTubeVideo(object sender, EventArgs e)
        {
            var downloader = new YouTubeVideoDownloader(URL);
            downloader.DownloadProgress += YouTubeDownloader_DownloadProgress;
            downloader.DownloadComplete += YouTubeDownloader_DownloadComplete;

            try
            {
                await Task.Run(() => downloader.Download()); //Download() is synchronous, need to wrap it like this as not to block UI 
            }
            catch //all sorts of things can go wrong with this, just catch all the exceptions.
            {
                MessageBox.Show("Unable to download this video. Try using the built-in screen capture.", "Unable to Download Video", MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }

        private void YouTubeDownloader_DownloadComplete(object sender, EventArgs e)
        {
            YouTubeDownloadComplete?.Invoke(this, e);
        }

        private void YouTubeDownloader_DownloadProgress(object sender, EventArgs e)
        {
            YouTubeDownloadProgress?.Invoke(this, e);
        }
        #endregion



        private void DownloadFile(string path)
        {
            try
            {

                WebClient webClient = new WebClient();
                webClient.DownloadProgressChanged += webClient_DownloadProgressChanged;
                webClient.DownloadFileCompleted += webClient_DownloadFileCompleted;

                string file = Path.Combine(Constants.CacheLocation, Path.GetFileName(OsirtHelper.StripQueryFromPath(path)));
                webClient.DownloadFileAsync(new Uri(path), file, file);
            }
            catch
            {
                MessageBox.Show("Unable to download this file.", "Unable to download file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void webClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            DownloadComplete?.Invoke(this, e);
        }

        private void webClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadingProgress?.Invoke(this, e);
        }

    }
}
