using System.Text;
using cshttp;
using CommonWebAppDemo.engine;

namespace CommonWebAppDemo.RH
{
    public static class AboutPage
    {
        public static void HandleRequest()
        {
            var pt = new PageTemplate
            {
                Title = "About - cshttp High Performance",
                Description = "Technical details of cshttp zero-dependency ASP.NET Web Forms style engine.",
                BodyClass = "page-about"
            };

            var sb = new StringBuilder();
            sb.Append(pt.GenerateHtmlHeader());

            sb.Append(@"
<div class='hero-section'>
  <div class='hero-content'>
    <h1>Architecture &amp; Features</h1>
    <p>A breakdown of the architecture, high performance characteristics, and conventions employed in this demo.</p>
  </div>
</div>

<div class='card about-card'>
  <h2>Key Architectural Pillars</h2>
  <ul class='features-list'>
    <li>
      <strong>Zero Dependencies:</strong> <code>cshttp</code> runs a bare-metal HTTP engine directly over System.Net.Sockets without Kestrel, Katana, or System.Web runtime overhead.
    </li>
    <li>
      <strong>Zero-Ceremony Static Files:</strong> Static assets are handled with automatic MIME classification, Range requests (audio/video), ETags, Last-Modified, and 304 Not Modified HTTP cache validation.
    </li>
    <li>
      <strong>Cache Busting:</strong> Every CSS and JS include uses <code>StaticAsset.Css()</code> and <code>StaticAsset.Script()</code> appending <code>?v=yyyyMMddHHmmss</code> based on file write timestamps.
    </li>
    <li>
      <strong>Pageless C# Markup Composition:</strong> Complete separation between dynamic server rendering and front-end client DOM via verbatim multiline strings (<code>$@""...""</code>) with single-quoted attributes.
    </li>
    <li>
      <strong>Multipart File Uploads:</strong> In-stream multipart parsing without temp file clutter, storing uploaded avatars safely into <code>App_Data/uploads/</code>.
    </li>
    <li>
      <strong>Semi-Silent Iframe Printing:</strong> Seamless printing triggered via hidden iframe pointing to <code>/member/print?id=...&amp;print=1</code> without leaving the dashboard.
    </li>
  </ul>
</div>
");

            sb.Append(pt.GenerateHtmlFooter());

            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(sb.ToString());
        }
    }
}
