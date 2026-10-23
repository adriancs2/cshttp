# Day 6: Safety First (HTML Encoding)

In Day 5, we built a Guestbook where anyone can post a comment and everyone else can see it.

That is great... but there is a hidden problem. Our server takes whatever the visitor types and puts it **directly into the HTML page**. And HTML is not just text — it is **code** that the browser runs!

In Day 6, we will first **break our own website** on purpose, and then fix it with one simple C# method: `HttpUtility.HtmlEncode()`.

---

## What You Will Learn Today

1. **Why user input is dangerous**: The browser cannot tell the difference between HTML that *you* wrote and HTML that a *visitor* typed.
2. **`HttpUtility.HtmlEncode(text)`**: Converts special characters like `<` and `>` into safe text (`&lt;` and `&gt;`) so the browser shows them instead of running them.
3. **The golden rule**: Never trust user input. Encode it every time you put it into a page.

---

## Part 1: Let's Break It!

Run your **Day 5** code (`dotnet run`) and open `http://localhost:8080/guestbook`.

Post a comment with this text:

```text
<b>I am BOLD!</b>
```

Look at the result. Your comment is shown in **bold**. The `<b>` tag was not shown as text — the browser *ran* it as HTML!

Now try something more serious. Post this comment:

```text
<script>alert('Your website has been hacked!')</script>
```

Refresh the page. A popup box appears! And it will appear for **every visitor**, forever, because the comment is saved in our list.

This attack is called **Cross-Site Scripting (XSS)**. A bad person could use it to steal other visitors' login cookies or redirect them to a fake website.

Try the same thing on the Greet page:

```text
http://localhost:8080/greet?name=<i>Hello</i>
```

The name appears in *italic*. Same problem!

---

## Part 2: How to Fix It

The fix is one method: `HttpUtility.HtmlEncode()`.

It replaces dangerous characters with safe "HTML entities":

| You type | HtmlEncode turns it into | The browser shows |
| :--- | :--- | :--- |
| `<` | `&lt;` | `<` |
| `>` | `&gt;` | `>` |
| `"` | `&quot;` | `"` |
| `&` | `&amp;` | `&` |

So `<script>` becomes `&lt;script&gt;`, and the browser simply **displays** the text `<script>` on the screen instead of running it.

**The golden rule:** Every time you put something the visitor typed into your HTML, wrap it with `HttpUtility.HtmlEncode(...)`.

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 6`**:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using cshttp;

static class Program
{
    static List<string> guestbookEntries = new List<string>()
    {
        "[2026-10-22 09:00] System: Welcome to our new Guestbook!",
        "[2026-10-22 09:05] Alice: First comment! C# web dev is awesome."
    };

    static void Main()
    {
        WebApp.Run(8080, OnRequest);
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
            case "/greet":
                HandleGreet();
                return;
            case "/about":
                HandleAbout();
                return;
            case "/contact":
                HandleContact();
                return;
            default:
                HandleNotFound();
                return;
        }
    }

    static void RenderPage(string title, string bodyContent)
    {
        string html = $@"
<html>
    <head>
        <title>{title}</title>
    </head>
    <body>
        <nav>
            <a href=""/"">Home</a> |
            <a href=""/guestbook"">Guestbook</a> |
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

    static void HandleGuestbook()
    {
        if (Request.HttpMethod == "POST")
        {
            string author = Request.Form["author"];
            string comment = Request.Form["comment"];

            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(comment))
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string entry = $"[{timestamp}] {author}: {comment}";
                guestbookEntries.Add(entry);
            }

            Response.Redirect("/guestbook");
            return;
        }

        StringBuilder listHtml = new StringBuilder();
        listHtml.Append("<ul>");
        foreach (string entry in guestbookEntries)
        {
            // NEW IN DAY 6: Encode the entry before putting it into the HTML
            string safeEntry = HttpUtility.HtmlEncode(entry);
            listHtml.Append($"<li style=\"margin-bottom: 8px;\">{safeEntry}</li>");
        }
        listHtml.Append("</ul>");

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
            <h2>Recent Entries ({guestbookEntries.Count})</h2>
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

            // NEW IN DAY 6: Encode everything the visitor typed
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
            // NEW IN DAY 6: Encode the name from the URL
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
            <p>Our <a href=""/guestbook"">Guestbook</a> is now safe from HTML injection. Try to break it!</p>";

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

1. Stop the old server (`Ctrl + C`) and run the new code (`dotnet run`).
2. Visit `http://localhost:8080/guestbook`.
3. Post the comment `<b>I am BOLD!</b>` again.
4. This time you see the text **exactly as you typed it**, including the `<b>` and `</b>`. Nothing is bold!
5. Post `<script>alert('hacked')</script>`. No popup appears. The text is just shown on the page.
6. Right-click the page and choose **View Page Source**. Find your comment. You will see `&lt;b&gt;` instead of `<b>`. That is HtmlEncode doing its job!
7. Try `http://localhost:8080/greet?name=<i>Hello</i>`. The name is no longer italic.

---

## Why Didn't We Encode Everything?

Notice that we did **not** encode our own HTML (like `<h1>Thank You</h1>` or `<li>`). We only encoded the parts that **came from the visitor**:

- `Request.Form[...]` — typed into a form
- `Request.QueryString[...]` — typed into the URL

Our own HTML is trusted, because *we* wrote it. The visitor's text is not trusted, because *anyone* could have typed *anything*.

In **Day 7**, we will make our guestbook entries smarter by creating our very first C# **class**.
