using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace china_ip_list
{
    class Program
    {
        public static string chn_ip = "", chnroute = "", chn_ip_v6 = "", chnroute_v6 = "";
        static void Main(string[] args)
        {
            string apnic_ip = GetResponse("http://ftp.apnic.net/apnic/stats/apnic/delegated-apnic-latest");
            
            //string apnic_ip = "apnic|IN|ipv4|103.16.104.0|1024|20130205|allocated\napnic|CN|ipv4|103.16.108.0|65536|20130205|allocated\napnic|ID|ipv4|103.16.112.0|1024|20130205|assigned\napnic|BN|ipv4|103.16.120.0|1024|20130206|assigned\napnic|CN|ipv4|103.16.124.0|1024|20130206|allocated\napnic|AU|ipv4|103.16.128.0|1024|20130206|allocated\napnic|ID|ipv4|103.16.132.0|512|20130206|assigned\n";
            string[] ip_list = apnic_ip.Split(new string[] { "\n" }, StringSplitOptions.None);
            int i = 1;
            int i_ip6 = 1;
            string save_txt_path = AppContext.BaseDirectory;
            foreach (string per_ip in ip_list)
            {
                //处理IPV4部分
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
                //处理IPV6部分
                if (per_ip.Contains("CN|ipv6|"))
                {
                    string[] ip_information_v6 = per_ip.Split('|');
                    string ip_v6 = ip_information_v6[3];
                    string ip_mask_v6 = Convert.ToString(Convert.ToInt32(ip_information_v6[4]));
                    string end_ip_v6 = DecimalToIpv6(BigInteger.Parse(IpV6ToInt(ip_v6)) + Convert.ToUInt32(ip_information_v6[4]) - 1); //减掉广播地址
                    chnroute_v6 += ip_v6 + "/" + ip_mask_v6 + "\n";
                    chn_ip_v6 += ip_v6 + " " + end_ip_v6 + "\n";
                    i_ip6++;
                }
            }
            ////Console.Write(chnroute);
            ////Console.Write(chn_ip);
            File.WriteAllText(save_txt_path + "chnroute.txt", chnroute);
            File.WriteAllText(save_txt_path + "chn_ip.txt", chn_ip);
            Console.WriteLine("本次共获取" + i + "条CN IPv4的记录，文件保存于" + save_txt_path + "chn_ip.txt");

            File.WriteAllText(save_txt_path + "chnroute_v6.txt", chnroute_v6);
            File.WriteAllText(save_txt_path + "chn_ip_v6.txt", chn_ip_v6);
            Console.WriteLine("本次共获取" + i_ip6 + "条CN IPv6的记录，文件保存于" + save_txt_path + "chn_ip_v6.txt");
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

        private static string IpV6ToInt(string ipStr)
        {
            IPAddress ip = IPAddress.Parse(ipStr);
            List<Byte> ipFormat = ip.GetAddressBytes().ToList();
            ipFormat.Reverse();
            ipFormat.Add(0);
            BigInteger ipv6AsInt = new BigInteger(ipFormat.ToArray());
            return ipv6AsInt.ToString();
        }

        private static string DecimalToIpv6(BigInteger decimalValue)
        {
            string hexString = decimalValue.ToString("X");
            string paddedHexString = hexString.PadLeft(32, '0');

            string ipv6 = "";
            for (int i = 0; i < paddedHexString.Length; i += 4)
            {
                ipv6 += paddedHexString.Substring(i, 4) + ":";
            }

            // 移除最后一个冒号
            ipv6 = ipv6.TrimEnd(':');

            return SimplifyIpv6Address(ipv6.ToLower());
        }

        private static string SimplifyIpv6Address(string ipv6)
        {
            string simplifiedIpv6 = Regex.Replace(ipv6, @"(:[0]{1,4}){2,}", "::");
            return simplifiedIpv6.Replace(":0",":");
        }

        public static int CalculateMaskBits(string ipv6Address, int subnetPrefixLength)
        {
            int totalSegments = ipv6Address.Split(':').Length;
            int maskBits = totalSegments * 16 - subnetPrefixLength;

            return maskBits;
        }

    }
}
