using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using cshttp;
using CommonWebAppDemo.engine;

namespace CommonWebAppDemo.RH
{
    public static class MemberPageApi
    {
        public static void HandleRequest()
        {
            string action = (Request.Params["action"] ?? Request.Form["action"] ?? "").ToLower().Trim();

            try
            {
                switch (action)
                {
                    case "get-members-json":
                        GetMembersJson();
                        break;

                    case "get-members-html":
                        GetMembersHtml();
                        break;

                    case "get-member":
                        GetMemberSingle();
                        break;

                    case "save-member":
                        SaveMember();
                        break;

                    case "delete-member":
                        DeleteMember();
                        break;

                    default:
                        ApiHelper.WriteError("Unknown action: " + action, 400);
                        break;
                }
            }
            catch (Exception ex)
            {
                ApiHelper.WriteError("Server Error: " + ex.Message, 500);
            }
        }

        static void GetMembersJson()
        {
            List<MemberItem> members = Db.GetAll();
            ApiHelper.WriteSuccessData("Members retrieved", members);
        }

        static void GetMemberSingle()
        {
            int id = 0;
            int.TryParse(Request.Params["id"] ?? Request.Form["id"], out id);
            if (id <= 0)
            {
                ApiHelper.WriteError("Invalid member ID");
                return;
            }

            MemberItem m = Db.GetById(id);
            if (m == null)
            {
                ApiHelper.WriteError("Member not found", 404);
                return;
            }

            ApiHelper.WriteSuccessData("Member found", m);
        }

        static void GetMembersHtml()
        {
            List<MemberItem> members = Db.GetAll();
            string html = RenderMemberListHtml(members);

            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(html);
        }

        public static string RenderMemberListHtml(List<MemberItem> members)
        {
            if (members == null || members.Count == 0)
            {
                return "<div class='empty-state'><p>No members registered yet. Fill the form to add one!</p></div>";
            }

            var sb = new StringBuilder();
            sb.Append("<div class='member-table-wrapper'>");
            sb.Append("<table class='member-table'>");
            sb.Append("<thead><tr>");
            sb.Append("<th>Member</th>");
            sb.Append("<th>Role</th>");
            sb.Append("<th>Joined</th>");
            sb.Append("<th style='text-align:right;'>Actions</th>");
            sb.Append("</tr></thead>");
            sb.Append("<tbody>");

            for (int i = 0; i < members.Count; i++)
            {
                MemberItem m = members[i];
                string name = HttpUtility.HtmlEncode(m.Name);
                string email = HttpUtility.HtmlEncode(m.Email);
                string role = HttpUtility.HtmlEncode(m.Role);
                string avatar = HttpUtility.HtmlAttributeEncode(m.AvatarUrl);
                string date = HttpUtility.HtmlEncode(m.CreatedAt);

                sb.Append($@"
<tr class='member-row' data-id='{m.Id}'>
  <td>
    <div class='user-cell'>
      <img src='{avatar}' class='user-avatar' alt='{name}' />
      <div class='user-details'>
        <div class='user-name'>{name}</div>
        <div class='user-email'>{email}</div>
      </div>
    </div>
  </td>
  <td><span class='role-pill'>{role}</span></td>
  <td class='date-cell'>{date}</td>
  <td class='action-cell'>
    <button type='button' class='btn-action btn-print' data-id='{m.Id}' title='Semi-silent iframe print'>🖨️ Print</button>
    <button type='button' class='btn-action btn-edit' data-id='{m.Id}'>✏️ Edit</button>
    <button type='button' class='btn-action btn-delete' data-id='{m.Id}'>🗑️ Delete</button>
  </td>
</tr>");
            }

            sb.Append("</tbody></table></div>");
            return sb.ToString();
        }

        static void SaveMember()
        {
            int id = 0;
            int.TryParse(Request.Form["id"], out id);

            string name = (Request.Form["name"] ?? "").Trim();
            string email = (Request.Form["email"] ?? "").Trim();
            string role = (Request.Form["role"] ?? "Standard Member").Trim();

            if (string.IsNullOrEmpty(name))
            {
                ApiHelper.WriteError("Full Name is required.");
                return;
            }
            if (string.IsNullOrEmpty(email))
            {
                ApiHelper.WriteError("Email address is required.");
                return;
            }

            string avatarUrl = null;

            // Handle file upload if present
            HttpPostedFile file = Request.Files["avatar"];
            if (file != null && file.ContentLength > 0)
            {
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".webp" && ext != ".gif")
                {
                    ApiHelper.WriteError("Invalid avatar image format. Allowed: PNG, JPG, WebP, GIF.");
                    return;
                }

                if (file.ContentLength > 5 * 1024 * 1024)
                {
                    ApiHelper.WriteError("Avatar image file size must not exceed 5MB.");
                    return;
                }

                string uploadDir = WebApp.MapPath("~/App_Data/uploads");
                if (string.IsNullOrEmpty(uploadDir))
                {
                    uploadDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "uploads");
                }
                if (!Directory.Exists(uploadDir))
                {
                    Directory.CreateDirectory(uploadDir);
                }

                string filename = Guid.NewGuid().ToString("N") + ext;
                string destPath = Path.Combine(uploadDir, filename);
                file.SaveAs(destPath);

                avatarUrl = "/uploads/" + filename;
            }

            if (id <= 0)
            {
                // New member
                var member = new MemberItem
                {
                    Name = name,
                    Email = email,
                    Role = role,
                    AvatarUrl = avatarUrl ?? "/assets/images/default-avatar.svg",
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };
                int newId = Db.Insert(member);
                ApiHelper.WriteSuccess("Member '" + name + "' registered successfully (ID: " + newId + ").");
            }
            else
            {
                // Update member
                MemberItem existing = Db.GetById(id);
                if (existing == null)
                {
                    ApiHelper.WriteError("Member with ID " + id + " does not exist.", 404);
                    return;
                }

                existing.Name = name;
                existing.Email = email;
                existing.Role = role;
                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    existing.AvatarUrl = avatarUrl;
                }

                Db.Update(existing);
                ApiHelper.WriteSuccess("Member '" + name + "' updated successfully.");
            }
        }

        static void DeleteMember()
        {
            int id = 0;
            int.TryParse(Request.Form["id"], out id);
            if (id <= 0)
            {
                ApiHelper.WriteError("Invalid member ID for deletion.");
                return;
            }

            bool ok = Db.Delete(id);
            if (ok)
            {
                ApiHelper.WriteSuccess("Member removed successfully.");
            }
            else
            {
                ApiHelper.WriteError("Failed to delete member or member not found.", 404);
            }
        }
    }
}
