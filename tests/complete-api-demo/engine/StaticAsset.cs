using System;
using System.IO;

namespace CompleteApiDemo.engine
{
    public static class StaticAsset
    {
        public static string Css(string virtualPath)
        {
            string hash = GetFileHash(virtualPath);
            return $"<link rel=\"stylesheet\" href=\"{virtualPath}?v={hash}\" />";
        }

        public static string Script(string virtualPath)
        {
            string hash = GetFileHash(virtualPath);
            return $"<script src=\"{virtualPath}?v={hash}\"></script>";
        }

        static string GetFileHash(string virtualPath)
        {
            try
            {
                string path = cshttp.WebApp.MapPath(virtualPath);
                if (File.Exists(path))
                {
                    return File.GetLastWriteTimeUtc(path).Ticks.ToString("x");
                }
            }
            catch { }
            return "1";
        }
    }
}
