using System;
using System.IO;

namespace CommonWebAppDemo.engine
{
    public static class StaticAsset
    {
        public static string Script(string path)
        {
            return "<script src='" + path + "?v=" + Version(path) + "'></script>";
        }

        public static string Css(string path)
        {
            return "<link rel='stylesheet' href='" + path + "?v=" + Version(path) + "' />";
        }

        public static string Version(string path)
        {
            string phys = cshttp.WebApp.MapPath(path);
            if (!string.IsNullOrEmpty(phys) && File.Exists(phys))
            {
                return File.GetLastWriteTimeUtc(phys).ToString("yyyyMMddHHmmss");
            }
            return "0";
        }
    }
}
