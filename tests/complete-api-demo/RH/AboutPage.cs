using System;
using System.Text;
using cshttp;
using CompleteApiDemo.engine;

namespace CompleteApiDemo.RH
{
    public static class AboutPage
    {
        public static void HandleRequest()
        {
            var tpl = new PageTemplate { Title = "Architecture — cshttp API Showcase", ActiveNav = "about" };
            var sb = new StringBuilder();

            sb.Append(tpl.GenerateHtmlHeader());

            sb.Append(@"
    <div class=""hero"">
      <h1><i class=""fa-solid fa-cubes""></i> Architecture &amp; Foundations</h1>
      <p>Understanding how cshttp achieves extreme performance with zero dependencies.</p>
    </div>

    <div class=""card"" style=""margin-bottom: 25px;"">
      <div class=""card-header"">
        <span class=""card-title""><i class=""fa-solid fa-microchip""></i> 3-in-1 Unified Design</span>
      </div>
      <div class=""card-body"">
        <p><strong>cshttp</strong> is organized internally into 3 clean domains under a single assembly:</p>
        <ul style=""padding-left: 20px; line-height: 1.8; color: #334155;"">
          <li><strong>Core (<code>cshttp/Core/</code>):</strong> Zero-allocation HTTP/1.1 wire parser, chunked transfer decoder, and byte-level scanner.</li>
          <li><strong>Web (<code>cshttp/Web/</code>):</strong> Classic ASP.NET-style <code>HttpContext</code>, <code>HttpRequest</code>, <code>HttpResponse</code>, cookies, multipart uploads, and static file engines.</li>
          <li><strong>Server (<code>cshttp/Server/</code>):</strong> High-concurrency IOCP socket listener, synchronous thread pool dispatch, and graceful shutdown coordination.</li>
        </ul>
      </div>
    </div>

    <div class=""card"">
      <div class=""card-header"">
        <span class=""card-title""><i class=""fa-solid fa-server""></i> Runtime &amp; Deployment</span>
      </div>
      <div class=""card-body"">
        <p><strong>cshttp</strong> multi-targets both <code>.NET Framework 4.8</code> and <code>.NET 8/10+</code>, allowing existing legacy systems and modern cloud microservices to share the exact same battle-tested HTTP engine.</p>
        <div style=""margin-top: 15px;"">
          <a href=""/"" class=""btn btn-primary""><i class=""fa-solid fa-arrow-left""></i> Back to Dashboard</a>
        </div>
      </div>
    </div>
");

            sb.Append(tpl.GenerateHtmlFooter());

            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(sb.ToString());
        }
    }
}
