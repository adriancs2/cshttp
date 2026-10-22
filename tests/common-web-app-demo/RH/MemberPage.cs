using System.Text;
using cshttp;
using CommonWebAppDemo.engine;

namespace CommonWebAppDemo.RH
{
    public static class MemberPage
    {
        public static void HandleRequest()
        {
            var pt = new PageTemplate
            {
                Title = "Membership Directory - cshttp High Performance",
                Description = "A complete modern Pageless ASP.NET style web app built on cshttp zero-dependency engine.",
                BodyClass = "page-members"
            };

            var sb = new StringBuilder();
            sb.Append(pt.GenerateHtmlHeader());

            sb.Append(@"
<div class='hero-section'>
  <div class='hero-content'>
    <h1>Enterprise Membership Directory</h1>
    <p>Zero-dependency high performance web engine powered by <code>cshttp</code> (.NET 10). Demonstrating static asset caching, ETag validation, JSON/HTML partial endpoints, file uploads, SQLite storage, and semi-silent iframe printing.</p>
  </div>
</div>

<div class='dashboard-grid'>
  <!-- Left Side: Member Management Form -->
  <section class='card form-card'>
    <div class='card-header'>
      <h2>Register / Edit Member</h2>
      <span class='badge' id='form-mode-badge'>New Member</span>
    </div>
    <form id='member-form' class='member-form' autocomplete='off'>
      <input type='hidden' id='field-id' name='id' value='0' />
      
      <div class='form-group'>
        <label for='field-name'>Full Name <span class='req'>*</span></label>
        <input type='text' id='field-name' name='name' class='form-control' placeholder='e.g. Adrian Tan' required />
      </div>

      <div class='form-group'>
        <label for='field-email'>Email Address <span class='req'>*</span></label>
        <input type='email' id='field-email' name='email' class='form-control' placeholder='e.g. adrian@example.com' required />
      </div>

      <div class='form-group'>
        <label for='field-role'>Designation / Role <span class='req'>*</span></label>
        <select id='field-role' name='role' class='form-control'>
          <option value='Lead Architect'>Lead Architect</option>
          <option value='Senior Engineer'>Senior Engineer</option>
          <option value='DevOps Specialist'>DevOps Specialist</option>
          <option value='UI/UX Designer'>UI/UX Designer</option>
          <option value='Security Analyst'>Security Analyst</option>
          <option value='Member'>Standard Member</option>
        </select>
      </div>

      <div class='form-group'>
        <label for='field-avatar'>Avatar Photo (Upload)</label>
        <input type='file' id='field-avatar' name='avatar' class='form-control-file' accept='image/png,image/jpeg,image/webp,image/gif' />
        <small class='form-hint'>Supported: PNG, JPG, WebP, GIF. Saved safely in ~/App_Data/uploads/</small>
      </div>

      <div class='form-actions'>
        <button type='button' id='btn-save' class='btn btn-primary'>Save Member (JSON POST)</button>
        <button type='button' id='btn-reset' class='btn btn-secondary'>Reset</button>
      </div>
    </form>
  </section>

  <!-- Right Side: Members Directory List -->
  <section class='card list-card'>
    <div class='card-header'>
      <div class='header-title-group'>
        <h2>Members Directory</h2>
        <span class='counter-tag' id='member-count'>...</span>
      </div>
      <div class='header-actions'>
        <button type='button' id='btn-refresh-html' class='btn btn-sm btn-outline' title='Fetch API Form POST returning Partial Block HTML'>
          🔄 Refresh via Partial HTML
        </button>
        <button type='button' id='btn-refresh-json' class='btn btn-sm btn-outline' title='Fetch API Form POST returning JSON'>
          ⚡ Refresh via JSON
        </button>
      </div>
    </div>

    <!-- Container for dynamic partial HTML replacement -->
    <div id='member-list-container' class='member-list-container'>
      <div class='loading-placeholder'>Loading members...</div>
    </div>
  </section>
</div>
");

            sb.Append(pt.GenerateHtmlFooter());

            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(sb.ToString());
        }
    }
}
