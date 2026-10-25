# Day 12: Sending Structured Data (JSON)

In Day 11, the server and JavaScript passed **one block of plain text** back and forth. That was perfect for a notepad.

But what if we want to send our **guestbook entries** to JavaScript? Each entry has **three parts**: a name, a message, and a time. And there are **many** entries. With plain text, JavaScript would have to guess where one entry ends and the next begins, and which part is the name.

We need a way to write data **with structure**. The most popular way on the web is **JSON**.

---

## What You Will Learn Today

1. **What JSON is**: Plain text, written in a special shape that both C# and JavaScript understand.
2. **Building JSON in C#** by hand, with a `StringBuilder`.
3. **`HttpUtility.JavaScriptStringEncode(text, true)`**: Makes any text safe to put inside JSON.
4. **`Response.ContentType = "application/json"`**: The label for JSON.
5. **`response.json()`**: JavaScript turns the JSON text into real objects.
6. **`setInterval`**: Run a function again and again, so the page updates **by itself**.

---

## JSON Is Just Text (With a Shape)

Here is one guestbook entry in JSON:

```json
{"name":"Alice","message":"Hello everyone!","time":"2026-10-23 14:30"}
```

- `{ ... }` is an **object** — like one `GuestbookEntry`.
- Inside, each property is written as `"name":value`, separated by commas.
- Text values are always inside **double quotes**.

And a **list** of entries uses square brackets `[ ... ]`, with commas **between** the items:

```json
[
  {"name":"Alice","message":"Hello everyone!","time":"2026-10-23 14:30"},
  {"name":"Bob","message":"Nice website!","time":"2026-10-23 14:31"}
]
```

Look closely: this is still **just text**! You could type it in Notepad. The only new thing compared to Day 11 is the **shape**.

| C# | JSON |
| :--- | :--- |
| `List<GuestbookEntry>` | `[ ... ]` (an array) |
| one `GuestbookEntry` object | `{ ... }` (an object) |
| `entry.Name = "Alice"` | `"name":"Alice"` |

---

## The Danger of Quotes

What if Bob's message is:

```text
I said "hi"
```

If we just glue it into our JSON, we get:

```json
{"name":"Bob","message":"I said "hi"","time":"..."}
```

The `"` before `hi` **closes** the message too early, and the JSON is broken! JavaScript will fail to read it.

This is the same kind of problem as Day 6, and the solution is the same idea: **encode** the text. In JSON, a quote inside text must be written as `\"`:

```json
{"name":"Bob","message":"I said \"hi\"","time":"..."}
```

C# does this for us:

```csharp
string message = HttpUtility.JavaScriptStringEncode(entry.Message, true);
// The 'true' means: also put the "double quotes" around it.
```

**The rule is the same as Day 6:** Every time you put user text into JSON, encode it.

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 12`**:

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

    // The shared notepad text
    static string noteText = "Hello! This is the shared notepad.\nEdit me and click Save.";

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
                HandleNotepad();
                return;
            case "/api/note":
                HandleNoteApi();
                return;
            case "/live":
                HandleLiveGuestbook(); // NEW IN DAY 12: the page
                return;
            case "/api/entries":
                HandleEntriesApi(); // NEW IN DAY 12: JSON data only
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
            <a href=""/live"">Live</a> |
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

    // Sends or receives PLAIN TEXT. No HTML at all.
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

            noteText = text;

            Console.WriteLine("Note saved (" + text.Length + " characters)");
            Console.WriteLine("Request.Form has " + Request.Form.Count + " fields (the body is not a form!)");

            Response.ContentType = "text/plain";
            Response.Write("Saved! (" + text.Length + " characters)");
            return;
        }

        // GET: send the current note as plain text
        Response.ContentType = "text/plain";
        Response.Write(noteText);
    }

    // The notepad page. JavaScript talks to /api/note without reloading the page.
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

    // NEW IN DAY 12: Sends the guestbook entries as JSON
    static void HandleEntriesApi()
    {
        StringBuilder json = new StringBuilder();
        json.Append("[");
        for (int i = 0; i < guestbookEntries.Count; i++)
        {
            GuestbookEntry entry = guestbookEntries[i];

            // A comma goes BETWEEN items, not before the first one
            if (i > 0)
            {
                json.Append(",");
            }

            // JavaScriptStringEncode(text, true) adds the "quotes" and escapes
            // any " or \ or line break inside the text, so the JSON never breaks
            string name = HttpUtility.JavaScriptStringEncode(entry.Name, true);
            string message = HttpUtility.JavaScriptStringEncode(entry.Message, true);
            string time = HttpUtility.JavaScriptStringEncode(entry.Time.ToString("yyyy-MM-dd HH:mm"), true);

            json.Append("{");
            json.Append("\"name\":" + name + ",");
            json.Append("\"message\":" + message + ",");
            json.Append("\"time\":" + time);
            json.Append("}");
        }
        json.Append("]");

        Response.ContentType = "application/json";
        Response.Write(json.ToString());
    }

    // NEW IN DAY 12: A guestbook page that updates itself every 3 seconds
    static void HandleLiveGuestbook()
    {
        string content = @"
            <h1>Live Guestbook</h1>
            <p>This list updates by itself every 3 seconds. Sign the <a href=""/guestbook"" target=""_blank"">Guestbook</a> in another tab and watch!</p>
            <p id=""liveCount"" style=""color: gray;"">Loading...</p>
            <ul id=""liveList""></ul>

            <script>
                var list = document.getElementById('liveList');
                var countText = document.getElementById('liveCount');

                function loadEntries() {
                    fetch('/api/entries')
                        .then(function (response) {
                            return response.json();   // read the reply as JSON (not plain text)
                        })
                        .then(function (entries) {
                            // 'entries' is now a real JavaScript array of objects!
                            list.innerHTML = '';      // clear the old list (this is our own empty text, so it is safe)

                            for (var i = 0; i < entries.length; i++) {
                                var entry = entries[i];

                                var li = document.createElement('li');
                                li.style.marginBottom = '8px';

                                var nameEl = document.createElement('b');
                                nameEl.textContent = entry.name;          // textContent = safe

                                var timeEl = document.createElement('small');
                                timeEl.style.color = 'gray';
                                timeEl.textContent = ' (' + entry.time + ')';

                                var messageEl = document.createElement('div');
                                messageEl.textContent = entry.message;

                                li.appendChild(nameEl);
                                li.appendChild(timeEl);
                                li.appendChild(messageEl);
                                list.appendChild(li);
                            }

                            countText.textContent = entries.length + ' entries (last updated ' + new Date().toLocaleTimeString() + ')';
                        });
                }

                loadEntries();                      // load once now
                setInterval(loadEntries, 3000);     // and again every 3000 ms (3 seconds)
            </script>";

        RenderPage("Live Guestbook", content);
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

                guestbookEntries.Add(entry);
                SaveEntry(entry);

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

### 1. Look at the JSON

Visit `http://localhost:8080/api/entries`.

You see your guestbook entries as JSON text. (Chrome, Edge, and Firefox may show a **"Pretty-print"** checkbox or a nice tree view, because we labelled it `application/json`.)

### 2. Break It on Purpose (Then See It Survive)

1. Go to the **Guestbook** and post a comment with quotes and a backslash: `I said "hi" \o/`
2. Visit `/api/entries` again. You will see `\"hi\"` and `\\o/` — the text was encoded, so the JSON is still valid.

### 3. The Live Guestbook

1. Visit `http://localhost:8080/live`. You see all entries, and **"last updated ..."** changes every 3 seconds.
2. Click the **Guestbook** link in the text (it opens in a **new tab**). Post a new comment there.
3. Switch back to the **Live** tab. Within 3 seconds, the new comment appears **by itself** — no reload!
4. Press `F12` → **Network**. You will see a new `entries` request every 3 seconds.

---

## `response.text()` vs `response.json()`

| | Day 11 | Day 12 |
| :--- | :--- | :--- |
| Server sends | plain text | JSON text |
| Content-Type | `text/plain` | `application/json` |
| JavaScript reads it with | `response.text()` | `response.json()` |
| JavaScript gets | one string | a real array of objects: `entries[0].name` |

---

## Why Build JSON by Hand?

Real projects usually use a **JSON library** (like `System.Text.Json` or `Newtonsoft.Json`) that turns a `List<GuestbookEntry>` into JSON with one line of code. We'll meet one tomorrow, in Day 13.

We built it by hand today so you can **see** that JSON is just text with a shape — there is no magic. Once you understand that, using a library is easy.

---

## Summary

- **JSON** is plain text with structure: `[ ]` for lists, `{ }` for objects, `"name":value` for properties.
- Always **encode** user text that goes into JSON: `HttpUtility.JavaScriptStringEncode(text, true)`.
- Label it: `Response.ContentType = "application/json";`
- In JavaScript: `fetch(url).then(function (r) { return r.json(); })`
- Show user text with `textContent`, never with `innerHTML`.

In **Day 13**, we will let a library called **Newtonsoft.Json** do all this hard work for us — turning a whole `List<GuestbookEntry>` into JSON with **one line** of code.
