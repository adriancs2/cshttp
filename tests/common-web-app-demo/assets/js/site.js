// site.js - Fetch API communication & client logic for cshttp demo

// 1. Fetch API POST helpers
async function apiPost(url, formData) {
  var res = await fetch(url, { method: 'POST', body: formData });
  if (!res.ok && res.status !== 400 && res.status !== 404 && res.status !== 500) {
    throw new Error('HTTP ' + res.status);
  }
  return await res.json();
}

async function apiPostText(url, formData) {
  var res = await fetch(url, { method: 'POST', body: formData });
  if (!res.ok && res.status !== 400 && res.status !== 404 && res.status !== 500) {
    throw new Error('HTTP ' + res.status);
  }
  return await res.text();
}

// 2. Toast notification helper
function showToast(message, isError) {
  var container = document.getElementById('toast-container');
  if (!container) return;

  var toast = document.createElement('div');
  toast.className = 'toast ' + (isError ? 'toast-error' : 'toast-success');
  toast.textContent = (isError ? '⚠️ ' : '✓ ') + message;

  container.appendChild(toast);

  setTimeout(function() {
    toast.style.opacity = '0';
    toast.style.transform = 'translateY(10px)';
    toast.style.transition = 'all 0.3s ease';
    setTimeout(function() {
      if (toast.parentNode) toast.parentNode.removeChild(toast);
    }, 300);
  }, 3500);
}

// 3. Page initialization & event wiring
document.addEventListener('DOMContentLoaded', function() {
  var form = document.getElementById('member-form');
  var btnSave = document.getElementById('btn-save');
  var btnReset = document.getElementById('btn-reset');
  var btnRefreshHtml = document.getElementById('btn-refresh-html');
  var btnRefreshJson = document.getElementById('btn-refresh-json');
  var listContainer = document.getElementById('member-list-container');
  var memberCountBadge = document.getElementById('member-count');
  var formModeBadge = document.getElementById('form-mode-badge');

  if (!listContainer) return; // Not on the dashboard page

  // Initial load via Partial HTML
  loadMembersViaHtml();

  // Reset Form
  function resetForm() {
    if (form) form.reset();
    document.getElementById('field-id').value = '0';
    if (formModeBadge) formModeBadge.textContent = 'New Member';
    if (btnSave) btnSave.textContent = 'Save Member (JSON POST)';
  }

  if (btnReset) {
    btnReset.addEventListener('click', resetForm);
  }

  // Refresh via Partial HTML Endpoint
  async function loadMembersViaHtml() {
    try {
      var fd = new FormData();
      fd.append('action', 'get-members-html');
      var html = await apiPostText('/memberapi', fd);
      listContainer.innerHTML = html;
      updateMemberCountFromDom();
    } catch (err) {
      showToast('Failed to load members via HTML: ' + err.message, true);
    }
  }

  // Refresh via JSON Endpoint (demonstrating client-side JSON reconstruction)
  async function loadMembersViaJson() {
    try {
      var fd = new FormData();
      fd.append('action', 'get-members-json');
      var res = await apiPost('/memberapi', fd);
      if (res.success && res.data) {
        renderMembersJson(res.data);
        showToast('Refreshed via JSON (' + res.data.length + ' records)');
      } else {
        showToast(res.message || 'Error loading JSON', true);
      }
    } catch (err) {
      showToast('Failed to load members via JSON: ' + err.message, true);
    }
  }

  function renderMembersJson(members) {
    if (!members || members.length === 0) {
      listContainer.innerHTML = "<div class='empty-state'><p>No members registered yet.</p></div>";
      if (memberCountBadge) memberCountBadge.textContent = '0 members';
      return;
    }

    var html = "<div class='member-table-wrapper'><table class='member-table'><thead><tr>" +
               "<th>Member</th><th>Role</th><th>Joined</th><th style='text-align:right;'>Actions</th></tr></thead><tbody>";

    for (var i = 0; i < members.length; i++) {
      var m = members[i];
      html += "<tr class='member-row' data-id='" + m.id + "'>" +
              "<td><div class='user-cell'><img src='" + m.avatar_url + "' class='user-avatar' alt='' />" +
              "<div class='user-details'><div class='user-name'>" + escapeHtml(m.name) + "</div>" +
              "<div class='user-email'>" + escapeHtml(m.email) + "</div></div></div></td>" +
              "<td><span class='role-pill'>" + escapeHtml(m.role) + "</span></td>" +
              "<td class='date-cell'>" + escapeHtml(m.created_at) + "</td>" +
              "<td class='action-cell'>" +
              "<button type='button' class='btn-action btn-print' data-id='" + m.id + "'>🖨️ Print</button>" +
              "<button type='button' class='btn-action btn-edit' data-id='" + m.id + "'>✏️ Edit</button>" +
              "<button type='button' class='btn-action btn-delete' data-id='" + m.id + "'>🗑️ Delete</button>" +
              "</td></tr>";
    }

    html += "</tbody></table></div>";
    listContainer.innerHTML = html;
    if (memberCountBadge) memberCountBadge.textContent = members.length + ' members';
  }

  function updateMemberCountFromDom() {
    var rows = listContainer.querySelectorAll('.member-row');
    if (memberCountBadge) {
      memberCountBadge.textContent = rows.length + ' members';
    }
  }

  function escapeHtml(text) {
    if (!text) return '';
    var div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  if (btnRefreshHtml) {
    btnRefreshHtml.addEventListener('click', function() {
      loadMembersViaHtml();
      showToast('Refreshed via Partial HTML fragment');
    });
  }

  if (btnRefreshJson) {
    btnRefreshJson.addEventListener('click', loadMembersViaJson);
  }

  // Save Member (Form POST with optional File Upload -> Return JSON)
  if (btnSave) {
    btnSave.addEventListener('click', async function() {
      var name = document.getElementById('field-name').value.trim();
      var email = document.getElementById('field-email').value.trim();

      if (!name) {
        showToast('Please enter full name', true);
        document.getElementById('field-name').focus();
        return;
      }
      if (!email) {
        showToast('Please enter email address', true);
        document.getElementById('field-email').focus();
        return;
      }

      btnSave.disabled = true;
      btnSave.textContent = 'Saving...';

      try {
        var fd = new FormData(form);
        fd.append('action', 'save-member');

        var res = await apiPost('/memberapi', fd);
        if (res.success) {
          showToast(res.message);
          resetForm();
          loadMembersViaHtml();
        } else {
          showToast(res.message || 'Validation failed', true);
        }
      } catch (err) {
        showToast('Failed to save member: ' + err.message, true);
      } finally {
        btnSave.disabled = false;
        btnSave.textContent = 'Save Member (JSON POST)';
      }
    });
  }

  // Event Delegation for Table Row Actions (Edit, Delete, Semi-Silent Print)
  listContainer.addEventListener('click', async function(e) {
    var target = e.target;
    var btn = target.closest('.btn-action');
    if (!btn) return;

    var id = btn.getAttribute('data-id');
    if (!id) return;

    // 1. Semi-Silent Iframe Print
    if (btn.classList.contains('btn-print')) {
      var iframe = document.getElementById('print-iframe');
      if (iframe) {
        showToast('Generating print card...');
        iframe.src = '/member/print?id=' + encodeURIComponent(id) + '&print=1';
      }
      return;
    }

    // 2. Edit Member
    if (btn.classList.contains('btn-edit')) {
      try {
        var fd = new FormData();
        fd.append('action', 'get-member');
        fd.append('id', id);

        var res = await apiPost('/memberapi', fd);
        if (res.success && res.data) {
          var m = res.data;
          document.getElementById('field-id').value = m.id;
          document.getElementById('field-name').value = m.name;
          document.getElementById('field-email').value = m.email;
          document.getElementById('field-role').value = m.role;

          if (formModeBadge) formModeBadge.textContent = 'Editing #' + m.id;
          if (btnSave) btnSave.textContent = 'Update Member #' + m.id;

          window.scrollTo({ top: 0, behavior: 'smooth' });
          showToast('Loaded member #' + m.id + ' into form');
        } else {
          showToast(res.message || 'Could not load member data', true);
        }
      } catch (err) {
        showToast('Error: ' + err.message, true);
      }
      return;
    }

    // 3. Delete Member
    if (btn.classList.contains('btn-delete')) {
      if (!confirm('Are you sure you want to delete member #' + id + '?')) return;

      try {
        var delFd = new FormData();
        delFd.append('action', 'delete-member');
        delFd.append('id', id);

        var delRes = await apiPost('/memberapi', delFd);
        if (delRes.success) {
          showToast(delRes.message);
          loadMembersViaHtml();
        } else {
          showToast(delRes.message || 'Failed to delete member', true);
        }
      } catch (err) {
        showToast('Error deleting member: ' + err.message, true);
      }
      return;
    }
  });
});
