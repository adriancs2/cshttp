// cshttp Complete API Interactive Demo Client JS
function showResult(boxId, content, isJson = false) {
    const box = document.getElementById(boxId);
    if (!box) return;
    box.classList.add('active');
    if (isJson) {
        try {
            const parsed = typeof content === 'string' ? JSON.parse(content) : content;
            box.textContent = JSON.stringify(parsed, null, 2);
        } catch (e) {
            box.textContent = content;
        }
    } else {
        box.textContent = content;
    }
}

// 1. Path Routing Demo
function testRoute(path) {
    fetch(path)
        .then(res => res.text())
        .then(txt => showResult('res-routing', txt));
}

// 2. QueryString Demo
function testQuery() {
    const name = document.getElementById('query-name').value;
    const role = document.getElementById('query-role').value;
    const url = `/api/query-demo?name=${encodeURIComponent(name)}&role=${encodeURIComponent(role)}`;
    fetch(url)
        .then(res => res.json())
        .then(data => showResult('res-query', data, true));
}

// 3. Form Submission Demo
function testFormPost(e) {
    e.preventDefault();
    const form = document.getElementById('form-demo');
    const data = new URLSearchParams(new FormData(form));
    fetch('/api/form-demo', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: data.toString()
    })
    .then(res => res.json())
    .then(data => showResult('res-form', data, true));
}

// 4. Cookies Demo
function setCookieDemo() {
    const key = document.getElementById('cookie-key').value;
    const val = document.getElementById('cookie-val').value;
    fetch('/api/cookie-demo', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `action=set&key=${encodeURIComponent(key)}&val=${encodeURIComponent(val)}`
    })
    .then(res => res.json())
    .then(data => showResult('res-cookies', data, true));
}

function getCookiesDemo() {
    fetch('/api/cookie-demo?action=get')
        .then(res => res.json())
        .then(data => showResult('res-cookies', data, true));
}

// 5. BodyText Demo
function testBodyText() {
    const raw = document.getElementById('raw-text-input').value;
    fetch('/api/body-text-demo', {
        method: 'POST',
        headers: { 'Content-Type': 'text/plain; charset=utf-8' },
        body: raw
    })
    .then(res => res.json())
    .then(data => showResult('res-bodytext', data, true));
}

// 6. BodyBytes Demo
function testBodyBytes() {
    const hex = document.getElementById('raw-hex-input').value;
    // send as octet-stream
    const bytes = new Uint8Array(hex.split(',').map(s => parseInt(s.trim(), 16) || 0));
    fetch('/api/body-bytes-demo', {
        method: 'POST',
        headers: { 'Content-Type': 'application/octet-stream' },
        body: bytes
    })
    .then(res => res.json())
    .then(data => showResult('res-bodybytes', data, true));
}

// 7. Status Code Demo
function testStatusCode() {
    const code = document.getElementById('status-code-select').value;
    fetch(`/api/status-demo?code=${code}`)
        .then(res => {
            return res.text().then(t => `HTTP Status: ${res.status} ${res.statusText}\nBody: ${t}`);
        })
        .then(msg => showResult('res-status', msg));
}

// 8. Output Text / JSON / HTML
function testOutput(type) {
    fetch(`/api/output-demo?type=${type}`)
        .then(res => {
            const ct = res.headers.get('content-type');
            return res.text().then(t => `Content-Type: ${ct}\n\n${t}`);
        })
        .then(msg => showResult('res-output', msg));
}

// 9. Binary Write Demo
function testBinaryWrite() {
    const img = document.getElementById('binary-img-preview');
    img.src = '/api/binary-image-demo?t=' + Date.now();
    img.style.display = 'block';
    showResult('res-binary', 'Requested /api/binary-image-demo (Dynamic 2x2 PNG via Response.BinaryWrite)');
}

// 10. File Uploads
function testFileUploadSingle(e) {
    e.preventDefault();
    const input = document.getElementById('file-single-input');
    if (!input.files || input.files.length === 0) return alert('Select a file first!');
    
    const fd = new FormData();
    fd.append('avatar', input.files[0]);

    fetch('/api/upload-single-demo', {
        method: 'POST',
        body: fd
    })
    .then(res => res.json())
    .then(data => showResult('res-upload-single', data, true));
}

function testFileUploadMulti(e) {
    e.preventDefault();
    const input = document.getElementById('file-multi-input');
    if (!input.files || input.files.length === 0) return alert('Select multiple files first!');

    const fd = new FormData();
    for (let i = 0; i < input.files.length; i++) {
        fd.append('photos', input.files[i]);
    }

    fetch('/api/upload-multi-demo', {
        method: 'POST',
        body: fd
    })
    .then(res => res.json())
    .then(data => showResult('res-upload-multi', data, true));
}

// 11. Background Work Demo
function testBackgroundWork() {
    fetch('/api/background-work-demo', { method: 'POST' })
        .then(res => res.json())
        .then(data => showResult('res-bgwork', data, true));
}

function checkBackgroundStatus() {
    fetch('/api/background-work-status')
        .then(res => res.json())
        .then(data => showResult('res-bgwork', data, true));
}

// 12. Security / HttpUtility Demo
function testHttpUtility() {
    const input = document.getElementById('xss-input').value;
    fetch(`/api/httputility-demo?input=${encodeURIComponent(input)}`)
        .then(res => res.json())
        .then(data => showResult('res-httputility', data, true));
}
