# Day 10: Remembering Visitors (Cookies)

Here is a surprising fact about the web: **the server has no memory of who you are.**

Every request is like a phone call from a stranger. When you post a guestbook comment and then open the guestbook again, the server has no idea that it is *you* again. That is why you have to type your name **every single time**.

So how do websites remember that you are logged in, or what language you prefer? They use **cookies**.

A **cookie** is a small note (a name and a value) that the **server gives to the browser**. The browser keeps the note, and **sends it back to the server with every request** after that.

```text
1. Server  -->  Browser:   "Please remember: guest_name = Alice"
2. Browser  -->  Server:   (every next request) "By the way, guest_name = Alice"
```

---

## What You Will Learn Today

1. **Issue a cookie** (server → browser): `Response.Cookies.Add(new HttpCookie("name", "value"))`.
2. **Read a cookie on the server**: `Request.Cookies["name"]`.
3. **Read a cookie in the browser** with JavaScript: `document.cookie`.
4. **Change a cookie**: send a new cookie with the **same name**.
5. **Delete a cookie**: send it again with an **expiry date in the past**.
6. **`HttpOnly`**: A cookie that JavaScript is **not allowed** to read (for secret things like login tokens).
7. **Use it for real**: the guestbook remembers your name and fills it in for you.

---

## Cookie Basics in C#

### 1. Issuing a Cookie

```csharp
HttpCookie cookie = new HttpCookie("favorite_color", "pink");
cookie.Expires = DateTime.Now.AddDays(30);   // Keep it for 30 days
Response.Cookies.Add(cookie);
```

- If you **don't** set `Expires`, it is a **session cookie**: the browser forgets it when you close the browser.
- If you **do** set `Expires`, the browser keeps it until that date, even after a restart.

### 2. Reading a Cookie on the Server

```csharp
HttpCookie cookie = Request.Cookies["favorite_color"];
if (cookie != null)
{
    string color = cookie.Value;   // "pink"
}
```

If the browser doesn't have that cookie, `Request.Cookies["..."]` gives you `null`. Always check!

### 3. Changing a Cookie

There is no "edit" command. You simply issue a **new cookie with the same name**, and the browser replaces the old one.

### 4. Deleting a Cookie

There is also no "delete" command! Instead, you send the cookie again with an **expiry date in the past**. The browser sees that it has already expired and throws it away:

```csharp
HttpCookie cookie = new HttpCookie("favorite_color", "");
cookie.Expires = DateTime.Now.AddDays(-1);   // Yesterday = already expired
Response.Cookies.Add(cookie);
```

### 5. Cookies Can't Hold Every Character

A cookie value **cannot** contain some characters, like `;` (it is used to separate cookies) or line breaks. Since visitors can type anything, we wrap the value with `HttpUtility.UrlEncode(...)` when saving, and `HttpUtility.UrlDecode(...)` when reading. For example, `Mary Jane` is stored as `Mary+Jane`.

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 10`**:

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
                HandleCookies(); // NEW IN DAY 10
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

    // NEW IN DAY 10: A small helper that reads a cookie and decodes it.
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

    // NEW IN DAY 10: The cookie playground
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

                // NEW IN DAY 10: Remember the visitor's name for 30 days
                HttpCookie nameCookie = new HttpCookie("guest_name", HttpUtility.UrlEncode(entry.Name));
                nameCookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Add(nameCookie);
            }

            Response.Redirect("/guestbook");
            return;
        }

        // NEW IN DAY 10: If we remember the visitor, greet them and fill in their name
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

> **Note about `{{` and `}}`:** Inside a C# `$@"..."` string, `{` and `}` are used for `{variables}`. The JavaScript in our page also uses curly braces for its `if`. To write a real `{` or `}`, we type it **twice**: `{{` and `}}`. C# turns them back into single braces when it builds the page.

---

## Test It in Your Browser

### The Cookie Playground

1. Run the app (`dotnet run`) and visit `http://localhost:8080/cookies`.
2. At first, both the server and JavaScript say **(no cookie)**.
3. **Issue:** Choose **Pink** and click **Set / Change Cookie**. The box turns pink. The server sees `favorite_color = pink`, and JavaScript sees `favorite_color=pink`.
4. **Change:** Choose **Light Green** and click again. The value is replaced — there is still only **one** `favorite_color` cookie.
5. **HttpOnly:** Click **Set Secret Cookie**. The **server** sees `secret_token`, but **JavaScript does not**! This is why login cookies should always be `HttpOnly`: even if a bad script gets into your page, it cannot steal them.
6. **Delete:** Click **Delete Cookie** and **Delete Secret Cookie**. Both disappear.
7. **Keep it:** Set the colour again, then close the browser completely and open the page again. The colour is still there, because we set `Expires` to 30 days.

### Look Inside the Browser

Press `F12` to open the developer tools:

- **Chrome / Edge:** go to **Application** → **Cookies** → `http://localhost:8080`
- **Firefox:** go to **Storage** → **Cookies**

You can see every cookie, its value, its expiry date, and a tick in the **HttpOnly** column for `secret_token`.

### The Guestbook Remembers You

1. Visit `http://localhost:8080/guestbook` and sign it with your name.
2. After the page reloads, you see **"Welcome back, (your name)!"** and the name box is already filled in.
3. Open the guestbook in a **private / incognito window**: it does not know you, because that window has its own cookies.

---

## Summary

| I want to... | C# code |
| :--- | :--- |
| Issue (create) a cookie | `Response.Cookies.Add(new HttpCookie("name", "value"))` |
| Keep it after the browser closes | `cookie.Expires = DateTime.Now.AddDays(30);` |
| Read it on the server | `Request.Cookies["name"]` (check for `null`!) |
| Read it in the browser | JavaScript: `document.cookie` |
| Change it | Issue a new cookie with the **same name** |
| Delete it | Issue it again with `Expires` in the **past** |
| Hide it from JavaScript | `cookie.HttpOnly = true;` |

**Important:** The visitor can see and change their own cookies (you just did it with F12!). Never store secrets like passwords in a cookie, and never fully trust a cookie value — treat it like any other user input.

In **Day 11**, we will use JavaScript's `fetch` to talk to our C# server **without reloading the page**, starting with the simplest thing possible: plain text.
