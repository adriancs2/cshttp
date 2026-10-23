# Day 5: Dynamic Data & Memory State (Interactive Guestbook)

In Day 4, when someone submitted a contact message, we received it and printed it to the console, but the message was gone once the page finished loading.

In Day 5, we build our first **Stateful Web Application**! We create an **Interactive Guestbook** where visitors can post comments, and the comments are stored in server memory (`List<string>`). Every visitor to your website can see all posted comments!

---

## What You Will Learn Today

1. **Server-Side Memory State (`List<string>`)**: Storing data in a static C# collection on the server.
2. **`StringBuilder` & `foreach` loop**: Dynamically generating HTML list items (`<li>`) from our stored data.
3. **PRG Pattern (Post-Redirect-Get)**: Using `Response.Redirect("/guestbook")` after saving a comment so the browser refreshes cleanly without duplicate form submissions.

---

## The Code

Update `Program.cs` with the following code:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using cshttp;

static class Program
{
    // NEW IN DAY 5: A list stored in server memory!
    // Every visitor to the website shares this exact same list.
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

    // NEW IN DAY 5: Combining POST forms, C# Lists, and foreach loops!
    static void HandleGuestbook()
    {
        // 1. If user submitted a new comment:
        if (Request.HttpMethod == "POST")
        {
            string author = Request.Form["author"];
            string comment = Request.Form["comment"];

            if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(comment))
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string entry = $"[{timestamp}] {author}: {comment}";

                // Add to our server list
                guestbookEntries.Add(entry);
            }

            // Redirect back to the guestbook page so the browser refreshes cleanly
            Response.Redirect("/guestbook");
            return;
        }

        // 2. Build the HTML list of all current comments using a loop
        StringBuilder listHtml = new StringBuilder();
        listHtml.Append("<ul>");
        foreach (string entry in guestbookEntries)
        {
            listHtml.Append($"<li style=\"margin-bottom: 8px;\">{entry}</li>");
        }
        listHtml.Append("</ul>");

        // 3. Render the Guestbook form + the list of comments
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

            string thankYouContent = $@"
                <h1>Thank You, {senderName}!</h1>
                <p>We received your message and will reply to <b>{email}</b> soon.</p>
                <div style=""background: #f0f0f0; padding: 10px; border-radius: 4px;"">
                    <i>""{message}""</i>
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
            greetingMessage = $"<h2>Hello, {name}! Welcome to C# Web Development!</h2>";
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
            <p>Check out our new <a href=""/guestbook"">Guestbook</a> to see shared server state in action!</p>";

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

1. Run the app (`dotnet run`).
2. Visit `http://localhost:8080/guestbook`.
3. Enter your name and a comment, then click **Sign Guestbook**.
4. The page refreshes and your new comment appears in the list!
5. Open an Incognito window or a different browser tab and visit the same link—you will see the exact same comments!
