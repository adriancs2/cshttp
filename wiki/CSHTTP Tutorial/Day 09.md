# Day 9: More Than HTML (Content Types: CSS, Pictures & Plain Text)

So far, every response our server sent was an **HTML page**.

But when a browser opens a real website, it asks the server for **many** different things: the HTML page, a stylesheet (CSS) to make it pretty, pictures, icons, and more. **Each one is a separate request**, and each one goes into our `OnRequest` method and our `switch` statement — exactly like a page does.

But how does the browser know whether the server sent it a page, a stylesheet, or a picture? The server tells it, using a label called the **Content-Type**.

---

## What You Will Learn Today

1. **One page = many requests**: The browser asks for the HTML first, then asks again for every stylesheet and picture the page mentions.
2. **`Response.ContentType`**: A label that tells the browser what kind of data we are sending.
3. **Common content types**: `text/html`, `text/css`, `text/plain`, and `image/svg+xml`.
4. **`<!DOCTYPE html>`**: One line at the top of the page that tells the browser to use modern, strict rules.

---

## Common Content Types

| What we send | Content-Type | Example file |
| :--- | :--- | :--- |
| A web page | `text/html` | `index.html` |
| A stylesheet | `text/css` | `style.css` |
| Plain text | `text/plain` | `hello.txt` |
| A drawing (vector picture) | `image/svg+xml` | `logo.svg` |
| A photo | `image/jpeg` | `cat.jpg` |
| Data for JavaScript | `application/json` | (we'll see this in Day 12!) |

If we don't set `Response.ContentType`, cshttp uses `text/html` by default. That's why all our pages worked so far without setting it.

---

## Our Plan for Today

We will add three new routes to our `switch`:

- **`/style.css`** — a stylesheet that makes our website look nicer (`text/css`).
- **`/logo.svg`** — a small picture drawn with SVG code (`image/svg+xml`).
- **`/hello.txt`** — a plain text file (`text/plain`).

None of these are real files on the disk! Our C# code **writes** them, just like it writes our HTML pages. The browser doesn't care — it only looks at the Content-Type label.

Then we will change `RenderPage` so every page uses the stylesheet and shows the logo.

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 9`**:

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

        // NEW IN DAY 9: Print every request so you can see how many requests one page makes!
        Console.WriteLine("Request: " + path);

        switch (path)
        {
            case "/":
            case "/home":
                HandleHome();
                return;
            case "/guestbook":
                HandleGuestbook();
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

            // NEW IN DAY 9: Routes that are NOT HTML pages
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

    // NEW IN DAY 9: A stylesheet, sent with the "text/css" label
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
}");
    }

    // NEW IN DAY 9: A picture drawn with SVG code, sent with the "image/svg+xml" label
    static void HandleLogo()
    {
        Response.ContentType = "image/svg+xml";
        Response.Write(@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""40"" height=""40"" viewBox=""0 0 40 40"">
    <circle cx=""20"" cy=""20"" r=""18"" fill=""#2c5282"" />
    <text x=""20"" y=""26"" font-size=""16"" font-family=""Arial"" fill=""white"" text-anchor=""middle"">C#</text>
</svg>");
    }

    // NEW IN DAY 9: Plain text, sent with the "text/plain" label
    static void HandleHelloText()
    {
        Response.ContentType = "text/plain";
        Response.Write("Hello! This is plain text.\n");
        Response.Write("<b>This is NOT bold</b>, because the browser knows this is not HTML.\n");
        Response.Write("Line breaks are kept exactly as they are.");
    }

    static void RenderPage(string title, string bodyContent)
    {
        // NEW IN DAY 9: <!DOCTYPE html>, the <link> to our stylesheet, and the logo <img>
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
            <a href=""/greet"">Greet</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a> |
            <a href=""/hello.txt"">Hello.txt</a>
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
            }

            Response.Redirect("/guestbook");
            return;
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
            <p>Leave a message for everyone visiting this site!</p>

            <form action=""/guestbook"" method=""post"" style=""background: #f9f9f9; padding: 15px; border: 1px solid #ddd; max-width: 450px;"">
                <div>
                    <label>Your Name:</label><br />
                    <input type=""text"" name=""author"" required style=""width: 100%;"" />
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
            <p>This page now has a stylesheet and a logo. Look at your server console: how many requests did this one page make?</p>";

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

1. Run the app (`dotnet run`) and visit `http://localhost:8080/`.
2. The website now has a nicer font, colours, and a round **C#** logo in the menu!
3. Look at your **server console**. For one page visit, you will see several requests:
   ```text
   Request: /
   Request: /style.css
   Request: /logo.svg
   ```
   The browser read the HTML, found the `<link>` and the `<img>`, and came back to ask for each of them.
   *(You might also see `/favicon.ico` — browsers automatically ask for a little tab icon. We don't have one, so it gets our 404 page. That's fine!)*
4. Visit these addresses directly:
   - `http://localhost:8080/style.css` — you see the CSS code as text.
   - `http://localhost:8080/logo.svg` — you see the logo picture.
   - `http://localhost:8080/hello.txt` — you see plain text. Notice that `<b>` is **not** bold, and the line breaks are kept!

---

## Experiment: What If the Label Is Wrong?

Let's prove that the browser really trusts the Content-Type label.

1. In `HandleStyle()`, put `//` in front of the Content-Type line to turn it off:
   ```csharp
   // Response.ContentType = "text/css";
   ```
2. Restart the server and refresh the page (press `Ctrl + F5` to make sure the browser does not use an old copy).
3. **The styling is gone!** Now the server sends the CSS with the default label `text/html`. The browser thinks: *"I asked for a stylesheet, but you gave me an HTML page?"* and refuses to use it. (Press `F12` and look at the **Console** tab — the browser explains the problem there.)
4. Try the same with `HandleLogo()` — the logo becomes a broken picture.
5. Put the Content-Type lines back when you're done.

This is why `<!DOCTYPE html>` matters: it tells the browser to follow the modern, strict rules — including checking the label.

---

## Summary

- One web page usually causes **many requests**: the page, its stylesheet, its pictures...
- Every request goes through the same `OnRequest` and `switch`.
- `Response.ContentType` tells the browser **what kind of data** it is receiving. The same text can be a page, a stylesheet, or a picture — depending on the label.

In **Day 10**, we will learn how the server can **remember** a visitor between requests, using **cookies**.
