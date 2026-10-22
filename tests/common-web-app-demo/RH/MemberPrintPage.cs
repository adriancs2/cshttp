using System;
using System.Text;
using cshttp;
using CommonWebAppDemo.engine;

namespace CommonWebAppDemo.RH
{
    public static class MemberPrintPage
    {
        public static void HandleRequest()
        {
            int id = 0;
            int.TryParse(Request.QueryString["id"], out id);

            MemberItem m = id > 0 ? Db.GetById(id) : null;
            if (m == null)
            {
                Response.StatusCode = 404;
                Response.ContentType = "text/html; charset=utf-8";
                Response.Write("<!DOCTYPE html><html><head><title>Member Not Found</title></head><body><h1>404 - Member Not Found</h1></body></html>");
                return;
            }

            bool shouldAutoPrint = Request.QueryString["print"] == "1";

            string name = HttpUtility.HtmlEncode(m.Name);
            string email = HttpUtility.HtmlEncode(m.Email);
            string role = HttpUtility.HtmlEncode(m.Role);
            string avatar = HttpUtility.HtmlAttributeEncode(m.AvatarUrl);
            string date = HttpUtility.HtmlEncode(m.CreatedAt);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html>\n");
            sb.Append("<html lang='en'>\n");
            sb.Append("<head>\n");
            sb.Append("  <meta charset='utf-8' />\n");
            sb.Append("  <title>Membership Card - " + name + "</title>\n");
            sb.Append(@"  <style>
    /* Self-contained print stylesheet per Pageless Web Forms Architecture guidelines */
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      background: #f4f6f8;
      color: #1e293b;
      padding: 40px;
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 100vh;
    }
    .print-card {
      width: 480px;
      background: #ffffff;
      border: 2px solid #0284c7;
      border-radius: 16px;
      overflow: hidden;
      box-shadow: 0 10px 25px rgba(0,0,0,0.1);
    }
    .card-header {
      background: linear-gradient(135deg, #0284c7 0%, #0369a1 100%);
      color: white;
      padding: 24px;
      text-align: center;
    }
    .card-header h1 {
      font-size: 20px;
      letter-spacing: 1px;
      text-transform: uppercase;
      margin-bottom: 4px;
    }
    .card-header p {
      font-size: 12px;
      opacity: 0.9;
    }
    .card-body {
      padding: 30px;
      display: flex;
      gap: 20px;
      align-items: center;
    }
    .avatar-frame {
      width: 100px;
      height: 100px;
      border-radius: 50%;
      border: 3px solid #e2e8f0;
      overflow: hidden;
      flex-shrink: 0;
      background: #f8fafc;
    }
    .avatar-frame img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }
    .info-list {
      flex: 1;
    }
    .info-row {
      margin-bottom: 12px;
    }
    .info-label {
      font-size: 11px;
      text-transform: uppercase;
      color: #64748b;
      font-weight: 600;
      margin-bottom: 2px;
    }
    .info-value {
      font-size: 15px;
      font-weight: 600;
      color: #0f172a;
    }
    .card-footer {
      border-top: 1px dashed #cbd5e1;
      padding: 16px 24px;
      background: #f8fafc;
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 11px;
      color: #64748b;
    }
    .status-badge {
      display: inline-block;
      padding: 4px 10px;
      background: #dcfce7;
      color: #15803d;
      border-radius: 9999px;
      font-weight: 600;
    }
    @media print {
      body {
        background: none;
        padding: 0;
      }
      .print-card {
        box-shadow: none;
        border: 1px solid #94a3b8;
        page-break-inside: avoid;
      }
    }
  </style>\n");

            if (shouldAutoPrint)
            {
                sb.Append("  <script>window.onload = function() { window.print(); };</script>\n");
            }

            sb.Append("</head>\n<body>\n");
            sb.Append($@"
  <div class='print-card'>
    <div class='card-header'>
      <h1>Official Membership Pass</h1>
      <p>cshttp High-Performance Enterprise Ecosystem</p>
    </div>
    <div class='card-body'>
      <div class='avatar-frame'>
        <img src='{avatar}' alt='{name}' />
      </div>
      <div class='info-list'>
        <div class='info-row'>
          <div class='info-label'>Full Name</div>
          <div class='info-value'>{name}</div>
        </div>
        <div class='info-row'>
          <div class='info-label'>Email</div>
          <div class='info-value' style='font-size:13px;'>{email}</div>
        </div>
        <div class='info-row'>
          <div class='info-label'>Assigned Role</div>
          <div class='info-value'>{role}</div>
        </div>
      </div>
    </div>
    <div class='card-footer'>
      <div>Member #{m.Id} &bull; Registered {date}</div>
      <span class='status-badge'>ACTIVE MEMBER</span>
    </div>
  </div>
</body>
</html>");

            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(sb.ToString());
        }
    }
}
