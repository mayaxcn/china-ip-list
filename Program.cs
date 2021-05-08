using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace china_ip_list
{
    class Program
    {
        public static string chn_ip = "", chnroute = "";
        static void Main(string[] args)
        {
            string apnic_ip = GetResponse("http://ftp.apnic.net/apnic/stats/apnic/delegated-apnic-latest");
            //string apnic_ip = "apnic|IN|ipv4|103.16.104.0|1024|20130205|allocated\napnic|CN|ipv4|103.16.108.0|65536|20130205|allocated\napnic|ID|ipv4|103.16.112.0|1024|20130205|assigned\napnic|BN|ipv4|103.16.120.0|1024|20130206|assigned\napnic|CN|ipv4|103.16.124.0|1024|20130206|allocated\napnic|AU|ipv4|103.16.128.0|1024|20130206|allocated\napnic|ID|ipv4|103.16.132.0|512|20130206|assigned\n";
            string[] ip_list = apnic_ip.Split(new string[] { "\n" }, StringSplitOptions.None);
            int i = 1;
            string save_txt_path = AppContext.BaseDirectory;
            foreach (string per_ip in ip_list)
            {
                if (per_ip.Contains("CN|ipv4|"))
                {
                    string[] ip_information = per_ip.Split('|');
                    string ip = ip_information[3];
                    string ip_mask = Convert.ToString(32 - (Math.Log(Convert.ToInt32(ip_information[4])) / Math.Log(2)));
                    string end_ip = IntToIp(IpToInt(ip) + Convert.ToUInt32(ip_information[4]) - 1); //减掉广播地址
                    chnroute += ip + "/" + ip_mask + "\n";
                    chn_ip += ip + " " + end_ip + "\n";
                    i++;
                }
            }
            ////Console.Write(chnroute);
            ////Console.Write(chn_ip);
            File.WriteAllText(save_txt_path + "chnroute.txt", chnroute);
            File.WriteAllText(save_txt_path + "chn_ip.txt", chn_ip);
            Console.Write("本次共获取" + i + "条CN IPv4的记录，文件保存于" + save_txt_path + "chn_ip.txt");
        }

        private static string GetResponse(string url)
        {
            if (url.StartsWith("https"))
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            }
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            HttpResponseMessage response = httpClient.GetAsync(url).Result;
            if (response.IsSuccessStatusCode)
            {
                string result = response.Content.ReadAsStringAsync().Result;
                return result;
            }
            return null;
        }

        private static uint IpToInt(string ipStr)
        {
            string[] ip = ipStr.Split('.');
            uint ipcode = 0xFFFFFF00 | byte.Parse(ip[3]);
            ipcode = ipcode & 0xFFFF00FF | (uint.Parse(ip[2]) << 0x08);
            ipcode = ipcode & 0xFF00FFFF | (uint.Parse(ip[1]) << 0x10);
            ipcode = ipcode & 0x00FFFFFF | (uint.Parse(ip[0]) << 0x18);
            return ipcode;
        }
        private static string IntToIp(uint ipcode)
        {
            byte addr1 = (byte)((ipcode & 0xFF000000) >> 0x18);
            byte addr2 = (byte)((ipcode & 0x00FF0000) >> 0x10);
            byte addr3 = (byte)((ipcode & 0x0000FF00) >> 0x08);
            byte addr4 = (byte)(ipcode & 0x000000FF);
            return string.Format("{0}.{1}.{2}.{3}", addr1, addr2, addr3, addr4);
        }

    }
}
