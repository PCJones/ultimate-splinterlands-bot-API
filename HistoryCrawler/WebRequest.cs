using HistoryCrawler.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HistoryCrawler
{
    internal class WebRequest
    {
        const string PROXY_USER = "";
        const string PROXY_PASS = "";
        const string PROXY_PORT = "1080";
        //readonly string[] PROXY_LIST = { "socks5.server1.com", "socks5.server2.com" };
        readonly string[] PROXY_LIST = Array.Empty<string>();
        const int MAX_REQUESTS_PER_PROXY = 950;
        const int MAX_CONCURRENT_REQUESTS = 20;
        private int MaxConcurrentRequestsReal = 5;
        readonly object ProxyChangeLock = new object();
        //readonly HttpClient NormalHttpClient;
        readonly HttpClient ProxyHttpClient;
        readonly WebProxy Proxy;
        private int CurrentProxy = 0;
        private int CurrentProxyRequestCount = 0;
        public int CurrentConnections = 0;
        private DateTime LastProxyChange;

        public async Task<string> GetPageAsync(string url, bool useProxy = true)
        {
            while (CurrentConnections >= MaxConcurrentRequestsReal)
            {
                await Task.Delay(500);
            }
            if (++CurrentProxyRequestCount > MAX_REQUESTS_PER_PROXY)
            {
                SetNextProxy();
                await Task.Delay(4000);
            }
            try
            {
                CurrentConnections++;
                int delay = Math.Max(100, MAX_CONCURRENT_REQUESTS * 25 - (MAX_CONCURRENT_REQUESTS - CurrentConnections) * 25);
                //Helper.WriteLogLine("Delay: " + delay.ToString());
                await Task.Delay(delay);
                //var result = useProxy ? await ProxyHttpClient.GetAsync(url) : await NormalHttpClient.GetAsync(url);
                var result = await ProxyHttpClient.GetAsync(url);
                if (result.StatusCode == HttpStatusCode.OK)
                {
                    var response = await result.Content.ReadAsStringAsync();
                    return response;
                }
                else if (result.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    CurrentProxyRequestCount += 5000;
                    return "";
                }
                else if (result.StatusCode == HttpStatusCode.BadGateway)
                {
                    return "";
                }
                else if (result.StatusCode == HttpStatusCode.UnavailableForLegalReasons)
                {
                    CurrentProxyRequestCount += 1000;
                    return "";
                }
                else
                {
                    Helper.WriteErrorLogLine("Unknown status code: " + result.StatusCode);
                    return "";
                }
            }
            catch (TaskCanceledException ex)
            {
                Helper.WriteLogLine("TimeOut!");
                CurrentProxyRequestCount += 50;
                await Task.Delay(5000);
                CurrentConnections--;
                var response = await GetPageAsync(url, useProxy);
                CurrentConnections++; // workaround
                return response;
            }
            catch (HttpRequestException ex)
            {
                Helper.WriteLogLine(ex.ToString());
                CurrentProxyRequestCount += 100;
                await Task.Delay(5000);
                CurrentConnections--;
                var response = await GetPageAsync(url, useProxy);
                CurrentConnections++; // workaround
                return response;
            }
            catch (Exception ex)
            {
                Helper.WriteLogLine("DownloadPageAsync: " + ex.ToString());
                return "";
            }
            finally
            {
                CurrentConnections--;
            }
        }

        public WebRequest()
        {
            //NormalHttpClient = new();
            Proxy = new WebProxy();
            SetNextProxy(true);

            // workaround if no proxy is set
            var clientHandler = new HttpClientHandler
            {
                Proxy = PROXY_LIST.Length > 0 ? Proxy : null,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            ProxyHttpClient = new HttpClient(clientHandler)
            {
                Timeout = new TimeSpan(0, 1, 5)
            };
            ProxyHttpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
        }

        public void SetNextProxy(bool startup = false, bool error = false)
        {
            try
            {
                lock (ProxyChangeLock)
                {
                    if (LastProxyChange.AddSeconds(30) < DateTime.Now || error) // mind. 30 sek vergangen
                    {
                        int maxWaitInSeconds = 7;
                        int count = 0;
                        while (CurrentConnections > 0 && count++ < maxWaitInSeconds)
                        {
                            MaxConcurrentRequestsReal = 0;
                            Helper.WriteLogLine("Waiting for all connections to close: " + CurrentConnections.ToString());
                            Task.Delay(1000).Wait();
                        }
                        CurrentConnections = 0;

                        // workaround if no proxy is set
                        if (PROXY_LIST.Length > 0)
                        {
                            Proxy.Address = new Uri("socks5://" + PROXY_LIST[CurrentProxy] + ":" + PROXY_PORT);
                            Proxy.Credentials = new NetworkCredential(PROXY_USER, PROXY_PASS, PROXY_LIST[CurrentProxy]);
                        }
                        if (++CurrentProxy >= PROXY_LIST.Length)
                        {
                            CurrentProxy = 0;
                        }
                        if (!startup) Helper.WriteLogLine("Changed Proxy: " + ProxyHttpClient.GetStringAsync("http://api.ipify.org/").Result);
                        MaxConcurrentRequestsReal = MAX_CONCURRENT_REQUESTS;
                        CurrentProxyRequestCount = 0;
                        LastProxyChange = DateTime.Now;
                    }
                    else
                    {
                        //Helper.WriteLogLine("Ignoring proxy change");
                    }
                }

            }
            catch (Exception ex)
            {
                Helper.WriteLogLine("Error at changing proxy: " + ex.ToString());
                System.Threading.Thread.Sleep(3000);
                SetNextProxy(startup, true);
            }
        }
    }
}
