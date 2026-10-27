using System;
using System.Text;
using cshttp;
using CompleteApiDemo.engine;

namespace CompleteApiDemo.RH
{
    public static class HomePage
    {
        public static void HandleRequest()
        {
            var tpl = new PageTemplate { Title = "Dashboard — cshttp API Showcase", ActiveNav = "home" };
            var sb = new StringBuilder();

            sb.Append(tpl.GenerateHtmlHeader());

            sb.Append(@"
    <div class=""hero"">
      <h1><i class=""fa-solid fa-layer-group""></i> cshttp Public API Showcase</h1>
      <p>Interactive playground testing every foundational API of the pure C# micro web server.</p>
    </div>

    <div class=""features-grid"">

      <!-- 1. Path Routing -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-route""></i> 1. Path Routing</span>
          <code>Request.Path</code>
        </div>
        <div class=""card-body"">
          <p>Inspects the normalized URL path without allocations or complex route table overhead.</p>
          <div class=""code-preview"">
<span class=""keyword"">string</span> path = <span class=""type"">Request</span>.Path.ToLower();
<span class=""keyword"">switch</span> (path) {
    <span class=""keyword"">case</span> <span class=""string"">""/api/hello""</span>: ...
}
          </div>
          <div class=""demo-action"">
            <button class=""btn btn-outline btn-sm"" onclick=""testRoute('/api/hello')"">GET /api/hello</button>
            <button class=""btn btn-outline btn-sm"" onclick=""testRoute('/api/nonexistent')"">GET /api/nonexistent</button>
            <div id=""res-routing"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 2. QueryString -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-magnifying-glass""></i> 2. URL Query String</span>
          <code>Request.QueryString</code>
        </div>
        <div class=""card-body"">
          <p>Lazy-parsed URL query key-values with automatic percent-decoding.</p>
          <div class=""code-preview"">
<span class=""keyword"">string</span> name = <span class=""type"">Request</span>.QueryString[<span class=""string"">""name""</span>];
<span class=""keyword"">string</span> role = <span class=""type"">Request</span>.QueryString[<span class=""string"">""role""</span>];
          </div>
          <div class=""demo-action"">
            <div class=""form-group"">
              <label>Name</label>
              <input id=""query-name"" type=""text"" class=""form-control"" value=""Alice"" />
            </div>
            <div class=""form-group"">
              <label>Role</label>
              <input id=""query-role"" type=""text"" class=""form-control"" value=""Architect"" />
            </div>
            <button class=""btn btn-primary btn-sm"" onclick=""testQuery()""><i class=""fa-solid fa-paper-plane""></i> Send Query</button>
            <div id=""res-query"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 3. Forms (POST) -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-table-list""></i> 3. Form Submissions</span>
          <code>Request.Form</code>
        </div>
        <div class=""card-body"">
          <p>Extracts fields from <code>application/x-www-form-urlencoded</code> forms.</p>
          <div class=""code-preview"">
<span class=""keyword"">if</span> (<span class=""type"">Request</span>.HttpMethod == <span class=""string"">""POST""</span>) {
    <span class=""keyword"">string</span> user = <span class=""type"">Request</span>.Form[<span class=""string"">""username""</span>];
}
          </div>
          <div class=""demo-action"">
            <form id=""form-demo"" onsubmit=""testFormPost(event)"">
              <div class=""form-group"">
                <label>Username</label>
                <input name=""username"" type=""text"" class=""form-control"" value=""developer_bob"" />
              </div>
              <div class=""form-group"">
                <label>Message</label>
                <input name=""message"" type=""text"" class=""form-control"" value=""cshttp is blazing fast!"" />
              </div>
              <button type=""submit"" class=""btn btn-primary btn-sm""><i class=""fa-solid fa-check""></i> Submit Form</button>
            </form>
            <div id=""res-form"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 4. Cookies -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-cookie-bite""></i> 4. Visitor Cookies</span>
          <code>Request / Response.Cookies</code>
        </div>
        <div class=""card-body"">
          <p>Read and issue RFC 6265 HTTP cookies with expiry, domain, and security policies.</p>
          <div class=""code-preview"">
<span class=""type"">HttpCookie</span> c = <span class=""keyword"">new</span> <span class=""type"">HttpCookie</span>(<span class=""string"">""theme""</span>, <span class=""string"">""dark""</span>);
<span class=""type"">Response</span>.Cookies.Add(c);
<span class=""keyword"">string</span> val = <span class=""type"">Request</span>.Cookies[<span class=""string"">""theme""</span>]?.Value;
          </div>
          <div class=""demo-action"">
            <div style=""display:flex;gap:8px;margin-bottom:8px;"">
              <input id=""cookie-key"" type=""text"" class=""form-control"" value=""session_token"" placeholder=""Key"" />
              <input id=""cookie-val"" type=""text"" class=""form-control"" value=""cshttp-xyz-777"" placeholder=""Value"" />
            </div>
            <button class=""btn btn-primary btn-sm"" onclick=""setCookieDemo()"">Set Cookie</button>
            <button class=""btn btn-outline btn-sm"" onclick=""getCookiesDemo()"">Read Cookies</button>
            <div id=""res-cookies"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 5. BodyText -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-file-lines""></i> 5. Raw Body Text</span>
          <code>Request.BodyText</code>
        </div>
        <div class=""card-body"">
          <p>Lazily decodes the entire request payload into a UTF-8 text string without stream plumbing.</p>
          <div class=""code-preview"">
<span class=""comment"">// Pure text in one line (JSON, XML, CSV):</span>
<span class=""keyword"">string</span> rawJson = <span class=""type"">Request</span>.BodyText;
          </div>
          <div class=""demo-action"">
            <div class=""form-group"">
              <label>Raw Payload Content</label>
              <textarea id=""raw-text-input"" class=""form-control"" rows=""2"">{ ""sensorId"": 101, ""status"": ""active"" }</textarea>
            </div>
            <button class=""btn btn-primary btn-sm"" onclick=""testBodyText()""><i class=""fa-solid fa-upload""></i> Send Text Payload</button>
            <div id=""res-bodytext"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 6. BodyBytes -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-microchip""></i> 6. Raw Body Bytes</span>
          <code>Request.BodyBytes</code>
        </div>
        <div class=""card-body"">
          <p>Accesses raw payload bytes directly for Protobuf, custom binary serialization, or hashing.</p>
          <div class=""code-preview"">
<span class=""keyword"">byte</span>[] data = <span class=""type"">Request</span>.BodyBytes;
<span class=""keyword"">int</span> size = <span class=""type"">Request</span>.ContentLength;
          </div>
          <div class=""demo-action"">
            <div class=""form-group"">
              <label>Hex Bytes (Comma-separated)</label>
              <input id=""raw-hex-input"" type=""text"" class=""form-control"" value=""0x43, 0x53, 0x48, 0x54, 0x54, 0x50"" />
            </div>
            <button class=""btn btn-primary btn-sm"" onclick=""testBodyBytes()""><i class=""fa-solid fa-arrow-right""></i> Post Binary Bytes</button>
            <div id=""res-bodybytes"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 7. HTTP Status Codes -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-traffic-light""></i> 7. Status Codes</span>
          <code>Response.StatusCode</code>
        </div>
        <div class=""card-body"">
          <p>Direct HTTP status code setting (200, 201, 400, 401, 403, 404, 500).</p>
          <div class=""code-preview"">
<span class=""type"">Response</span>.StatusCode = <span class=""type"">404</span>;
<span class=""type"">Response</span>.Write(<span class=""string"">""Item Not Found""</span>);
          </div>
          <div class=""demo-action"">
            <div class=""form-group"">
              <label>Select Status Code</label>
              <select id=""status-code-select"" class=""form-control"">
                <option value=""200"">200 OK</option>
                <option value=""201"">201 Created</option>
                <option value=""400"">400 Bad Request</option>
                <option value=""401"">401 Unauthorized</option>
                <option value=""404"">404 Not Found</option>
                <option value=""500"">500 Internal Error</option>
              </select>
            </div>
            <button class=""btn btn-outline btn-sm"" onclick=""testStatusCode()"">Test Status</button>
            <div id=""res-status"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 8. Response Output -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-reply""></i> 8. Content Delivery</span>
          <code>Response.Write</code>
        </div>
        <div class=""card-body"">
          <p>Delivers HTML, JSON, or plain text with automatic RFC 9112 framing.</p>
          <div class=""code-preview"">
<span class=""type"">Response</span>.ContentType = <span class=""string"">""application/json""</span>;
<span class=""type"">Response</span>.Write(<span class=""string"">""{\""status\"":\""ok\""}""</span>);
          </div>
          <div class=""demo-action"">
            <div style=""display:flex;gap:6px;flex-wrap:wrap;"">
              <button class=""btn btn-outline btn-sm"" onclick=""testOutput('json')"">JSON</button>
              <button class=""btn btn-outline btn-sm"" onclick=""testOutput('html')"">HTML</button>
              <button class=""btn btn-outline btn-sm"" onclick=""testOutput('text')"">Plain Text</button>
            </div>
            <div id=""res-output"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 9. Binary Output -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-image""></i> 9. Binary Streaming</span>
          <code>Response.BinaryWrite</code>
        </div>
        <div class=""card-body"">
          <p>Outputs binary images or streams dynamically without string conversions.</p>
          <div class=""code-preview"">
<span class=""type"">Response</span>.ContentType = <span class=""string"">""image/png""</span>;
<span class=""type"">Response</span>.BinaryWrite(pngBytes);
          </div>
          <div class=""demo-action"">
            <button class=""btn btn-primary btn-sm"" onclick=""testBinaryWrite()""><i class=""fa-solid fa-paintbrush""></i> Render Binary Image</button>
            <div style=""margin-top:10px;text-align:center;"">
              <img id=""binary-img-preview"" style=""display:none;margin:0 auto;border:1px solid #ddd;padding:4px;border-radius:4px;width:64px;height:64px;"" alt=""Generated PNG"" />
            </div>
            <div id=""res-binary"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 10. TransmitFile -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-bolt""></i> 10. File Streaming</span>
          <code>Response.TransmitFile</code>
        </div>
        <div class=""card-body"">
          <p>Zero-copy file streaming directly from disk with automatic MIME types and byte ranges.</p>
          <div class=""code-preview"">
<span class=""type"">Response</span>.TransmitFile(<span class=""type"">WebApp</span>.MapPath(<span class=""string"">""~/favicon.svg""</span>));
          </div>
          <div class=""demo-action"">
            <a href=""/assets/images/favicon.svg"" target=""_blank"" class=""btn btn-outline btn-sm""><i class=""fa-solid fa-arrow-up-right-from-square""></i> Stream SVG Directly</a>
            <a href=""/assets/css/site.css"" target=""_blank"" class=""btn btn-outline btn-sm""><i class=""fa-solid fa-file-code""></i> Stream CSS Directly</a>
          </div>
        </div>
      </div>

      <!-- 11. File Uploads (Single & Multi) -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-cloud-arrow-up""></i> 11. Multipart Uploads</span>
          <code>Request.Files</code>
        </div>
        <div class=""card-body"">
          <p>Zero-dependency multipart parser handling single and multi-file input fields.</p>
          <div class=""code-preview"">
<span class=""type"">HttpPostedFile</span> f = <span class=""type"">Request</span>.Files[<span class=""string"">""avatar""</span>];
<span class=""keyword"">var</span> multi = <span class=""type"">Request</span>.Files.GetMultiple(<span class=""string"">""photos""</span>);
          </div>
          <div class=""demo-action"">
            <form onsubmit=""testFileUploadSingle(event)"" style=""margin-bottom:10px;"">
              <label style=""font-size:0.75rem;font-weight:600;color:gray;"">SINGLE FILE</label>
              <div style=""display:flex;gap:6px;"">
                <input id=""file-single-input"" type=""file"" class=""form-control"" style=""font-size:0.8rem;"" />
                <button type=""submit"" class=""btn btn-primary btn-sm"">Upload</button>
              </div>
            </form>
            <div id=""res-upload-single"" class=""result-box""></div>

            <form onsubmit=""testFileUploadMulti(event)"" style=""margin-top:10px;"">
              <label style=""font-size:0.75rem;font-weight:600;color:gray;"">MULTI-FILES (MULTIPLE ATTRIBUTE)</label>
              <div style=""display:flex;gap:6px;"">
                <input id=""file-multi-input"" type=""file"" multiple class=""form-control"" style=""font-size:0.8rem;"" />
                <button type=""submit"" class=""btn btn-primary btn-sm"">Upload All</button>
              </div>
            </form>
            <div id=""res-upload-multi"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 12. Safe Background Work -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-clock-rotate-left""></i> 12. Background Work</span>
          <code>WebApp.BackgroundWork</code>
        </div>
        <div class=""card-body"">
          <p>Tracked background task engine with graceful shutdown coordination for IIS & OS Services.</p>
          <div class=""code-preview"">
<span class=""type"">WebApp</span>.BackgroundWork(ct => {
    <span class=""comment"">// Server waits for this before process terminates!</span>
});
          </div>
          <div class=""demo-action"">
            <button class=""btn btn-primary btn-sm"" onclick=""testBackgroundWork()""><i class=""fa-solid fa-play""></i> Queue 3s Task</button>
            <button class=""btn btn-outline btn-sm"" onclick=""checkBackgroundStatus()"">Check Status</button>
            <div id=""res-bgwork"" class=""result-box""></div>
          </div>
        </div>
      </div>

      <!-- 13. Security / WebApp Encoding -->
      <div class=""card"">
        <div class=""card-header"">
          <span class=""card-title""><i class=""fa-solid fa-shield-halved""></i> 13. Security & Utility</span>
          <code>WebApp.HtmlEncode / UrlEncode</code>
        </div>
        <div class=""card-body"">
          <p>Zero-dependency XSS protection, URL encoding, and JavaScript string escaping.</p>
          <div class=""code-preview"">
<span class=""keyword"">string</span> safe = <span class=""type"">WebApp</span>.HtmlEncode(untrusted);
<span class=""keyword"">string</span> url  = <span class=""type"">WebApp</span>.UrlEncode(text);
          </div>
          <div class=""demo-action"">
            <div class=""form-group"">
              <label>Untrusted Test Input</label>
              <input id=""xss-input"" type=""text"" class=""form-control"" value=""<script>alert('Hacked')</script> & 'Quotes'"" />
            </div>
            <button class=""btn btn-primary btn-sm"" onclick=""testHttpUtility()""><i class=""fa-solid fa-lock""></i> Encode Input</button>
            <div id=""res-httputility"" class=""result-box""></div>
          </div>
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
