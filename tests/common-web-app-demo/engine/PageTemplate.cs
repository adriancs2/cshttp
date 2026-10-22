using System;
using System.Text;
using cshttp;

namespace CommonWebAppDemo.engine
{
    public class PageTemplate
    {
        public string Title { get; set; } = "Membership App";
        public string Description { get; set; } = "High-performance C# Web Application Demo using cshttp";
        public string OgTitle { get; set; }
        public string OgDescription { get; set; }
        public string OgImage { get; set; } = "/assets/images/og-card.png";
        public string Favicon { get; set; } = "/assets/images/favicon.svg";
        public string BodyClass { get; set; } = "page-default";
        public string ExtraHeaderText { get; set; } = "";
        public string ExtraFooterText { get; set; } = "";

        public string GenerateHtmlHeader()
        {
            string effOgTitle = string.IsNullOrEmpty(OgTitle) ? Title : OgTitle;
            string effOgDesc = string.IsNullOrEmpty(OgDescription) ? Description : OgDescription;

            var sb = new StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1.0' />
  <title>{HttpUtility.HtmlEncode(Title)}</title>
  <meta name='description' content='{HttpUtility.HtmlAttributeEncode(Description)}' />
  <link rel='icon' type='image/svg+xml' href='{Favicon}' />
  <!-- Open Graph / Social Cards -->
  <meta property='og:type' content='website' />
  <meta property='og:title' content='{HttpUtility.HtmlAttributeEncode(effOgTitle)}' />
  <meta property='og:description' content='{HttpUtility.HtmlAttributeEncode(effOgDesc)}' />
  <meta property='og:image' content='{HttpUtility.HtmlAttributeEncode(OgImage)}' />
  <!-- Core Stylesheet with Cache-Buster -->
  {StaticAsset.Css("/assets/css/site.css")}
");
            if (!string.IsNullOrEmpty(ExtraHeaderText))
            {
                sb.Append("  " + ExtraHeaderText + "\n");
            }
            sb.Append("</head>\n");
            sb.Append("<body class='" + HttpUtility.HtmlAttributeEncode(BodyClass) + "'>\n");
            sb.Append(@"  <header class='navbar'>
    <div class='nav-container'>
      <a href='/' class='nav-brand'>
        <span class='brand-logo'>⚡</span>
        <span class='brand-text'>cshttp Membership</span>
      </a>
      <nav class='nav-links'>
        <a href='/' class='nav-link'>Dashboard</a>
        <a href='/about' class='nav-link'>About</a>
      </nav>
    </div>
  </header>
  <main class='main-container'>
");
            return sb.ToString();
        }

        public string GenerateHtmlFooter()
        {
            var sb = new StringBuilder();
            sb.Append(@"  </main>
  <footer class='site-footer'>
    <div class='footer-container'>
      <p>&copy; " + DateTime.UtcNow.Year + @" cshttp Demo. Pageless Web Architecture &amp; High-Performance C#.</p>
    </div>
  </footer>
  <iframe id='print-iframe' style='position:absolute;width:0;height:0;border:0;visibility:hidden;'></iframe>
  <div id='toast-container' class='toast-container'></div>
  <!-- Core Script with Cache-Buster -->
  " + StaticAsset.Script("/assets/js/site.js") + "\n");

            if (!string.IsNullOrEmpty(ExtraFooterText))
            {
                sb.Append("  " + ExtraFooterText + "\n");
            }

            sb.Append("</body>\n</html>\n");
            return sb.ToString();
        }
    }
}
