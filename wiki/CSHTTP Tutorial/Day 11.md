# Day 11: Talking to the Server Without Reloading (fetch + Plain Text)

Until now, every time we sent something to the server (clicking a link, submitting a form), the **whole page reloaded**.

Modern websites often update just a **small part** of the page: a chat message appears, a "Saved!" label pops up, a counter changes. The page itself never reloads. This is done with JavaScript's **`fetch`**.

`fetch` lets JavaScript in the browser send a request to our C# server **in the background**, get the reply, and put it somewhere on the page.

Today we keep things as simple as possible: the server sends **just plain text**, and the browser sends **just plain text**. No HTML, no forms. (In Day 12, we'll upgrade to JSON.)

---

## What You Will Learn Today

1. **An "API" route**: A route that returns **data** (here: plain text), not a web page.
2. **`fetch('/api/note')`**: JavaScript asks the server for something, without reloading the page.
3. **`response.text()`**: Reads the server's reply as plain text.
4. **`<pre>` and `textContent`**: Shows the text **exactly** as it is, line breaks and all — and safely.
5. **`fetch(url, { method: 'POST', body: text })`**: JavaScript **sends** plain text to the server.
6. **`Request.InputStream`**: How the C# server reads that raw text (the request **body**).

---

## Two Kinds of Routes

| Route | Who uses it? | What it returns |
| :--- | :--- | :--- |
| `/notepad` | A **person** in the browser | A full HTML page |
| `/api/note` | **JavaScript** on that page | Just the plain text of the note |

Many developers start API routes with `/api/` so it's easy to tell them apart. That's just a habit, not a rule.

---

## How the Server Reads Plain Text

In Day 4, the browser sent a **form**, and we read it with `Request.Form["fieldName"]`. A form body looks like this:

```text
sender=Alice&email=alice%40example.com&message=Hello
```

Today, `fetch` sends the text **as it is** — no names, no `=`, no `&`:

```text
Buy milk
Call grandma
```

There are no field names, so `Request.Form` is **empty**. Instead, we read the whole body ourselves:

```csharp
string text;
using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
{
    text = reader.ReadToEnd();
}
```

- `Request.InputStream` is the raw body that the browser sent.
- `StreamReader` turns those raw bytes into text, using UTF-8 (so emoji and other languages work).
- `ReadToEnd()` reads everything, from the first character to the last.
- `using (...) { }` makes sure the reader is closed when we are done.

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 11`**:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using cshttp;

class GuestbookEntry
{
    public string Name { get; set; }
    public string Message { get; set; }
    public DateTime Time { get; set; }
}

static class Program
{
    static List<GuestbookEntry> guestbookEntries = new List<GuestbookEntry>();
    static string dataFile = WebApp.MapPath("~/guestbook.txt");
    static readonly object guestbookLock = new object();

    // NEW IN DAY 11: The shared notepad text (shared by every visitor, so it needs a lock too)
    static string noteText = "Hello! This is the shared notepad.\nEdit me and click Save.";
    static readonly object noteLock = new object();

    static void Main()
    {
        LoadEntries();
        Console.WriteLine("Guestbook file: " + dataFile);

        WebApp.Run(8080, OnRequest);
    }

    static void LoadEntries()
    {
        if (!File.Exists(dataFile))
        {
            return;
        }

        string[] lines = File.ReadAllLines(dataFile);
        foreach (string line in lines)
        {
            string[] parts = line.Split(new char[] { '|' }, 3);
            if (parts.Length != 3)
            {
                continue;
            }

            GuestbookEntry entry = new GuestbookEntry
            {
                Time = DateTime.ParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Name = parts[1],
                Message = parts[2]
            };
            guestbookEntries.Add(entry);
        }
    }

    static void SaveEntry(GuestbookEntry entry)
    {
        string time = entry.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string line = time + "|" + entry.Name + "|" + entry.Message;
        File.AppendAllText(dataFile, line + Environment.NewLine);
    }

    static void OnRequest()
    {
        string path = Request.Path.ToLower();

        switch (path)
        {
            case "/":
            case "/home":
                HandleHome();
                return;
            case "/guestbook":
                HandleGuestbook();
                return;
            case "/cookies":
                HandleCookies();
                return;
            case "/notepad":
                HandleNotepad(); // NEW IN DAY 11: the page
                return;
            case "/api/note":
                HandleNoteApi(); // NEW IN DAY 11: plain text only, no HTML
                return;
            case "/greet":
                HandleGreet();
                return;
            case "/about":
                HandleAbout();
                return;
            case "/contact":
                HandleContact();
                return;
            case "/style.css":
                HandleStyle();
                return;
            case "/logo.svg":
                HandleLogo();
                return;
            case "/hello.txt":
                HandleHelloText();
                return;
            default:
                HandleNotFound();
                return;
        }
    }

    static void HandleStyle()
    {
        Response.ContentType = "text/css";
        Response.Write(@"
body {
    font-family: Arial, sans-serif;
    max-width: 700px;
    margin: 20px auto;
    background: #fdfdfd;
    color: #333;
}
nav {
    display: flex;
    align-items: center;
    gap: 10px;
}
nav a {
    color: #2b6cb0;
    text-decoration: none;
}
nav a:hover {
    text-decoration: underline;
}
h1 {
    color: #2c5282;
}
footer {
    color: gray;
}
.box {
    background: #f9f9f9;
    border: 1px solid #ddd;
    padding: 15px;
    margin-bottom: 15px;
}");
    }

    static void HandleLogo()
    {
        Response.ContentType = "image/svg+xml";
        Response.Write(@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""40"" height=""40"" viewBox=""0 0 40 40"">
    <circle cx=""20"" cy=""20"" r=""18"" fill=""#2c5282"" />
    <text x=""20"" y=""26"" font-size=""16"" font-family=""Arial"" fill=""white"" text-anchor=""middle"">C#</text>
</svg>");
    }

    static void HandleHelloText()
    {
        Response.ContentType = "text/plain";
        Response.Write("Hello! This is plain text.\n");
        Response.Write("<b>This is NOT bold</b>, because the browser knows this is not HTML.\n");
        Response.Write("Line breaks are kept exactly as they are.");
    }

    static void RenderPage(string title, string bodyContent)
    {
        string html = $@"<!DOCTYPE html>
<html>
    <head>
        <title>{title}</title>
        <link rel=""stylesheet"" href=""/style.css"" />
    </head>
    <body>
        <nav>
            <img src=""/logo.svg"" alt=""Logo"" width=""40"" height=""40"" />
            <a href=""/"">Home</a> |
            <a href=""/guestbook"">Guestbook</a> |
            <a href=""/cookies"">Cookies</a> |
            <a href=""/notepad"">Notepad</a> |
            <a href=""/greet"">Greet</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
        </nav>
        <hr />

        {bodyContent}

        <hr />
        <footer>
            <small>Server Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</small>
        </footer>
    </body>
</html>";

        Response.Write(html);
        Response.End();
    }

   // A small helper that reads a cookie and decodes it.
    // Returns null if the browser did not send this cookie.
    static string GetCookieValue(string name)
    {
        HttpCookie cookie = Request.Cookies[name];
        if (cookie == null)
        {
            return null;
        }
        return HttpUtility.UrlDecode(cookie.Value);
    }

   // The cookie playground
    static void HandleCookies()
    {
        if (Request.HttpMethod == "POST")
        {
            string action = Request.Form["action"];

            if (action == "set-color")
            {
                // ISSUE or CHANGE: same name = the browser replaces the old value
                string color = Request.Form["color"];
                HttpCookie cookie = new HttpCookie("favorite_color", HttpUtility.UrlEncode(color));
                cookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Add(cookie);
            }
            else if (action == "delete-color")
            {
                // DELETE: send it again with an expiry date in the past
                HttpCookie cookie = new HttpCookie("favorite_color", "");
                cookie.Expires = DateTime.Now.AddDays(-1);
                Response.Cookies.Add(cookie);
            }
            else if (action == "set-secret")
            {
                // HttpOnly: JavaScript in the browser is NOT allowed to read this cookie
                HttpCookie cookie = new HttpCookie("secret_token", "ABC-123-XYZ");
                cookie.Expires = DateTime.Now.AddDays(30);
                cookie.HttpOnly = true;
                Response.Cookies.Add(cookie);
            }
            else if (action == "delete-secret")
            {
                HttpCookie cookie = new HttpCookie("secret_token", "");
                cookie.Expires = DateTime.Now.AddDays(-1);
                Response.Cookies.Add(cookie);
            }

            Response.Redirect("/cookies");
            return;
        }

        // READ on the server
        string favoriteColor = GetCookieValue("favorite_color");
        string secretToken = GetCookieValue("secret_token");

        string colorText = favoriteColor == null ? "(no cookie)" : HttpUtility.HtmlEncode(favoriteColor);
        string secretText = secretToken == null ? "(no cookie)" : HttpUtility.HtmlEncode(secretToken);

        // Only allow colours from our own list, so nobody can put strange text into our style="..."
        string boxColor = "white";
        if (favoriteColor == "pink" || favoriteColor == "lightblue" || favoriteColor == "lightgreen")
        {
            boxColor = favoriteColor;
        }

        string content = $@"
            <h1>Cookie Playground</h1>

            <div class=""box"" style=""background: {boxColor};"">
                <h2>1. What the SERVER sees (C#)</h2>
                <p>favorite_color = <b>{colorText}</b></p>
                <p>secret_token = <b>{secretText}</b></p>
            </div>

            <div class=""box"">
                <h2>2. What JAVASCRIPT sees (in the browser)</h2>
                <pre id=""jsCookies""></pre>
                <p><small>Notice: <b>secret_token</b> is missing here, because it is <b>HttpOnly</b>!</small></p>
            </div>

            <div class=""box"">
                <h2>3. Favourite colour (a normal cookie)</h2>
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""set-color"" />
                    <select name=""color"">
                        <option value=""pink"">Pink</option>
                        <option value=""lightblue"">Light Blue</option>
                        <option value=""lightgreen"">Light Green</option>
                    </select>
                    <button type=""submit"">Set / Change Cookie</button>
                </form>
                <br />
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""delete-color"" />
                    <button type=""submit"">Delete Cookie</button>
                </form>
            </div>

            <div class=""box"">
                <h2>4. Secret token (an HttpOnly cookie)</h2>
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""set-secret"" />
                    <button type=""submit"">Set Secret Cookie</button>
                </form>
                <br />
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""delete-secret"" />
                    <button type=""submit"">Delete Secret Cookie</button>
                </form>
            </div>

            <script>
                // READ in the browser: document.cookie holds every cookie JavaScript is allowed to see
                var text = document.cookie;
                if (text === '') {{
                    text = '(no cookies visible to JavaScript)';
                }}
                // textContent shows the text safely, just like HtmlEncode does in C#
                document.getElementById('jsCookies').textContent = text;
            </script>";

        RenderPage("Cookies", content);
    }

    // NEW IN DAY 11: Sends or receives PLAIN TEXT. No HTML at all.
    static void HandleNoteApi()
    {
        if (Request.HttpMethod == "POST")
        {
            // Read the raw body of the request: exactly the text the browser sent
            string text;
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }

            lock (noteLock)
            {
                noteText = text;
            }

            Console.WriteLine("Note saved (" + text.Length + " characters)");
            Console.WriteLine("Request.Form has " + Request.Form.Count + " fields (the body is not a form!)");

            Response.ContentType = "text/plain";
            Response.Write("Saved! (" + text.Length + " characters)");
            return;
        }

        // GET: send the current note as plain text
        string current;
        lock (noteLock)
        {
            current = noteText;
        }

        Response.ContentType = "text/plain";
        Response.Write(current);
    }

    // NEW IN DAY 11: The notepad page. JavaScript talks to /api/note without reloading the page.
    static void HandleNotepad()
    {
        // A normal @"..." string (no $), so the JavaScript curly braces need no doubling
        string content = @"
            <h1>Shared Notepad</h1>
            <p>Everyone visiting this website shares this one note.</p>

            <div class=""box"">
                <h2>The note (from the server)</h2>
                <pre id=""noteView"" style=""background: white; border: 1px solid #ccc; padding: 10px; min-height: 60px;""></pre>
                <button onclick=""loadNote()"">Reload from server</button>
            </div>

            <div class=""box"">
                <h2>Edit the note</h2>
                <textarea id=""noteEditor"" rows=""6"" style=""width: 100%;""></textarea>
                <br /><br />
                <button onclick=""saveNote()"">Save to server</button>
                <span id=""status"" style=""color: green; margin-left: 10px;""></span>
            </div>

            <script>
                var noteView = document.getElementById('noteView');
                var noteEditor = document.getElementById('noteEditor');
                var statusText = document.getElementById('status');

                // 1. Ask the server for the note (GET), then show it
                function loadNote() {
                    fetch('/api/note')
                        .then(function (response) {
                            return response.text();   // read the reply as plain text
                        })
                        .then(function (text) {
                            noteView.textContent = text;  // textContent = safe, shown exactly as-is
                            noteEditor.value = text;
                        });
                }

                // 2. Send the textarea's text to the server (POST), then reload it
                function saveNote() {
                    fetch('/api/note', {
                        method: 'POST',
                        body: noteEditor.value          // the raw text IS the request body
                    })
                        .then(function (response) {
                            return response.text();
                        })
                        .then(function (reply) {
                            statusText.textContent = reply;  // e.g. ""Saved! (42 characters)""
                            loadNote();
                        });
                }

                // Load the note once when the page opens
                loadNote();
            </script>";

        RenderPage("Notepad", content);
    }

    static void HandleGuestbook()
    {
        if (Request.HttpMethod == "POST")
        {
            string author = Request.Form["author"];
            string comment = Request.Form["comment"];

            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(comment))
            {
                GuestbookEntry entry = new GuestbookEntry
                {
                    Name = author.Replace("|", "/").Replace("\r", " ").Replace("\n", " "),
                    Message = comment.Replace("\r", " ").Replace("\n", " "),
                    Time = DateTime.Now
                };

                lock (guestbookLock)
                {
                    guestbookEntries.Add(entry);
                    SaveEntry(entry);
                }

               // Remember the visitor's name for 30 days
                HttpCookie nameCookie = new HttpCookie("guest_name", HttpUtility.UrlEncode(entry.Name));
                nameCookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Add(nameCookie);
            }

            Response.Redirect("/guestbook");
            return;
        }

       // If we remember the visitor, greet them and fill in their name
        string rememberedName = GetCookieValue("guest_name");
        string welcomeText = "";
        string nameValue = "";
        if (!string.IsNullOrEmpty(rememberedName))
        {
            welcomeText = $"<p><b>Welcome back, {HttpUtility.HtmlEncode(rememberedName)}!</b></p>";
            nameValue = HttpUtility.HtmlEncode(rememberedName); // Also encode inside value=""...""
        }

        StringBuilder listHtml = new StringBuilder();
        int count;

        lock (guestbookLock)
        {
            count = guestbookEntries.Count;

            listHtml.Append("<ul>");
            foreach (GuestbookEntry entry in guestbookEntries)
            {
                string safeName = HttpUtility.HtmlEncode(entry.Name);
                string safeMessage = HttpUtility.HtmlEncode(entry.Message);
                string time = entry.Time.ToString("yyyy-MM-dd HH:mm");

                listHtml.Append($@"
                    <li style=""margin-bottom: 8px;"">
                        <b>{safeName}</b>
                        <small style=""color: gray;"">({time})</small><br />
                        {safeMessage}
                    </li>");
            }
            listHtml.Append("</ul>");
        }

        string content = $@"
            <h1>Community Guestbook</h1>
            {welcomeText}
            <p>Leave a message for everyone visiting this site!</p>

            <form action=""/guestbook"" method=""post"" class=""box"" style=""max-width: 450px;"">
                <div>
                    <label>Your Name:</label><br />
                    <input type=""text"" name=""author"" value=""{nameValue}"" required style=""width: 100%;"" />
                </div>
                <br />
                <div>
                    <label>Your Comment:</label><br />
                    <textarea name=""comment"" rows=""3"" required style=""width: 100%;""></textarea>
                </div>
                <br />
                <button type=""submit"">Sign Guestbook</button>
            </form>

            <hr />
            <h2>Recent Entries ({count})</h2>
            {listHtml.ToString()}";

        RenderPage("Guestbook", content);
    }

    static void HandleContact()
    {
        if (Request.HttpMethod == "POST")
        {
            string senderName = Request.Form["sender"];
            string email = Request.Form["email"];
            string message = Request.Form["message"];

            Console.WriteLine($"[New Message] From: {senderName} ({email}) - {message}");

            string safeName = HttpUtility.HtmlEncode(senderName);
            string safeEmail = HttpUtility.HtmlEncode(email);
            string safeMessage = HttpUtility.HtmlEncode(message);

            string thankYouContent = $@"
                <h1>Thank You, {safeName}!</h1>
                <p>We received your message and will reply to <b>{safeEmail}</b> soon.</p>
                <div style=""background: #f0f0f0; padding: 10px; border-radius: 4px;"">
                    <i>""{safeMessage}""</i>
                </div>
                <p><a href=""/contact"">Send another message</a></p>";

            RenderPage("Message Received", thankYouContent);
            return;
        }

        string formContent = @"
            <h1>Contact Us</h1>
            <p>Send us a message below:</p>
            <form action=""/contact"" method=""post"">
                <div>
                    <label>Your Name:</label><br />
                    <input type=""text"" name=""sender"" required style=""width: 300px;"" />
                </div>
                <br />
                <div>
                    <label>Email Address:</label><br />
                    <input type=""email"" name=""email"" required style=""width: 300px;"" />
                </div>
                <br />
                <div>
                    <label>Message:</label><br />
                    <textarea name=""message"" rows=""5"" cols=""40"" required></textarea>
                </div>
                <br />
                <button type=""submit"">Send Message</button>
            </form>";

        RenderPage("Contact", formContent);
    }

    static void HandleGreet()
    {
        string name = Request.QueryString["name"];
        string greetingMessage;

        if (string.IsNullOrEmpty(name))
        {
            greetingMessage = "<p>Please enter your name below, or try clicking a quick link!</p>";
        }
        else
        {
            string safeName = HttpUtility.HtmlEncode(name);
            greetingMessage = $"<h2>Hello, {safeName}! Welcome to C# Web Development!</h2>";
        }

        string content = $@"
            <h1>Interactive Greeting</h1>
            {greetingMessage}

            <form action=""/greet"" method=""get"">
                <label>Your Name:</label>
                <input type=""text"" name=""name"" placeholder=""Type your name..."" />
                <button type=""submit"">Say Hello</button>
            </form>";

        RenderPage("Greet", content);
    }

    static void HandleHome()
    {
        string content = @"
            <h1>Welcome Home</h1>
            <p>Try the <a href=""/cookies"">Cookie Playground</a>, then sign the <a href=""/guestbook"">Guestbook</a> twice. The second time, it remembers you!</p>";

        RenderPage("Home", content);
    }

    static void HandleAbout()
    {
        string content = @"
            <h1>About Us</h1>
            <p>We build fast, zero-dependency web apps in C#.</p>";

        RenderPage("About Us", content);
    }

    static void HandleNotFound()
    {
        Response.StatusCode = 404;
        string content = @"
            <h1>404 Not Found</h1>
            <p>The page you are looking for does not exist.</p>";

        RenderPage("404 Not Found", content);
    }
}
```

---

## Test It in Your Browser

### 1. Look at the API Route Directly

Visit `http://localhost:8080/api/note`.

You see **only** the text of the note — no menu, no styles, no HTML. This is what JavaScript receives.

### 2. Use the Notepad

1. Visit `http://localhost:8080/notepad`.
2. The note appears in the grey `<pre>` box. Notice that the **line break** is kept: "Edit me and click Save." is on its own line.
3. Type a few lines into the editor, for example a shopping list:
   ```text
   Buy milk
   Buy bread
   Call grandma
   ```
4. Click **Save to server**. The label says **"Saved! (31 characters)"**, and the box updates — **without the page reloading**! (Watch the browser's reload button: it doesn't move.)
5. Look at the **server console**:
   ```text
   Note saved (31 characters)
   Request.Form has 0 fields (the body is not a form!)
   ```
   That proves the text arrived as a raw body, not as a form.

### 3. Shared by Everyone

Open the notepad in a **second browser window**. Save a new note in window 1. Then click **Reload from server** in window 2. The new note appears there too!

### 4. Try to Break It

Type `<b>bold?</b>` into the editor and save. It is **not** bold — you see the tags as text. That is because we used `textContent`, which is JavaScript's version of `HtmlEncode`. (If we had used `innerHTML`, the browser would run it as HTML. Remember Day 6!)

### 5. Watch It Happen (Optional)

Press `F12` and open the **Network** tab, then click **Save to server**. You will see a request to `note` with method **POST**. Click it to see:

- **Payload / Request**: exactly the text you typed.
- **Response**: `Saved! (... characters)`.

---

## How Does It Work?

```text
 Browser (JavaScript)                         C# Server
 ────────────────────                         ─────────
 fetch('/api/note')            ── GET ──▶     HandleNoteApi(): Response.Write(noteText)
 response.text()               ◀── text ──
 noteView.textContent = text

 fetch('/api/note', {
     method: 'POST',
     body: noteEditor.value    ── POST ──▶    HandleNoteApi(): read Request.InputStream
 })                                                            noteText = text
 response.text()               ◀── "Saved!" ──
```

The page loaded **once**. After that, JavaScript and the server just pass **small pieces of text** back and forth.

---

## Summary

| I want to... | Code |
| :--- | :--- |
| (Server) send plain text | `Response.ContentType = "text/plain";` then `Response.Write(text);` |
| (Server) read plain text the browser sent | `new StreamReader(Request.InputStream, Encoding.UTF8).ReadToEnd()` |
| (Browser) get text from the server | `fetch(url).then(function (r) { return r.text(); })` |
| (Browser) send text to the server | `fetch(url, { method: 'POST', body: text })` |
| (Browser) show text safely, with line breaks | a `<pre>` element + `element.textContent = text` |

In **Day 12**, we will send something more useful than one block of text: a **list of guestbook entries**, each with a name, a message, and a time. For that, we need **JSON**.
