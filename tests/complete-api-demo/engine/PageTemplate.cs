using System;
using System.Text;
using cshttp;

namespace CompleteApiDemo.engine
{
    public class PageTemplate
    {
        public string Title { get; set; } = "cshttp Complete API Demo";
        public string Description { get; set; } = "Interactive showcase of all cshttp micro-framework public APIs";
        public string ActiveNav { get; set; } = "home";

        public string GenerateHtmlHeader()
        {
            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
  <title>{HttpUtility.HtmlEncode(Title)}</title>
  <meta name=""description"" content=""{HttpUtility.HtmlAttributeEncode(Description)}"" />
  <link rel=""icon"" type=""image/svg+xml"" href=""/favicon.svg"" />
  <!-- FontAwesome 6 (Free CDN) -->
  <link rel=""stylesheet"" href=""https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.1/css/all.min.css"" crossorigin=""anonymous"" referrerpolicy=""no-referrer"" />
  <!-- Core Native OS Stylesheet -->
  {StaticAsset.Css("/assets/css/site.css")}
</head>
<body>
  <header class=""navbar"">
    <div class=""nav-container"">
      <a href=""/"" class=""nav-brand"">
        <i class=""fa-solid fa-bolt logo-icon""></i>
        <span>cshttp Complete API</span>
        <span class=""nav-badge"">v2.0 Standalone</span>
      </a>
      <nav class=""nav-links"">
        <a href=""/"" class=""nav-link {(ActiveNav == "home" ? "active" : "")}""><i class=""fa-solid fa-house""></i> Dashboard</a>
        <a href=""/about"" class=""nav-link {(ActiveNav == "about" ? "active" : "")}""><i class=""fa-solid fa-circle-info""></i> Architecture</a>
        <a href=""https://github.com/adriancs2/cshttp"" target=""_blank"" class=""nav-link""><i class=""fa-brands fa-github""></i> GitHub</a>
      </nav>
    </div>
  </header>
  <main class=""main-container"">
");
            return sb.ToString();
        }

        public string GenerateHtmlFooter()
        {
            var sb = new StringBuilder();
            sb.Append($@"  </main>
  <footer class=""site-footer"">
    <div class=""footer-container"">
      <p>&copy; {DateTime.UtcNow.Year} cshttp. Zero dependencies. High-performance C# micro web framework.</p>
    </div>
  </footer>
  {StaticAsset.Script("/assets/js/site.js")}
</body>
</html>
");
            return sb.ToString();
        }
    }
}
