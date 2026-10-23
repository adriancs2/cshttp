# Day 4: Handling Forms & Submissions (HTTP POST)

In Day 3, we sent data using GET (`/greet?name=Alice`). GET puts parameters right in the browser's address bar, which is great for search filters or shareable links.

However, for contact forms, comments, passwords, or longer messages, web standards use **HTTP POST**. POST sends data in the body of the request instead of the URL.

In Day 4, we teach our server how to handle **both GET and POST** on the same route!

---

## What You Will Learn Today

1. **`Request.HttpMethod`**: Detects if the incoming request is `"GET"` (visitor just opened the page) or `"POST"` (visitor clicked the Submit button).
2. **`Request.Form["fieldName"]`**: Reads form fields submitted by the user.
3. **`Console.WriteLine(...)`**: Logs user messages directly to your server's console terminal.

---

## The Code

Update `Program.cs` with the following code:

```csharp
using System;
using cshttp;

static class Program
{
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

    // NEW IN DAY 4: Handling both GET (show form) and POST (receive form)
    static void HandleContact()
    {
        // If the user clicked the Submit button, the browser sends a POST request
        if (Request.HttpMethod == "POST")
        {
            string senderName = Request.Form["sender"];
            string email = Request.Form["email"];
            string message = Request.Form["message"];

            // Print to the C# server console so you see it arrive live in your terminal!
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

        // Otherwise, it's a GET request: Display the empty contact form
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
            <p>Visit <a href=""/contact"">Contact</a> to test submitting an interactive POST form!</p>";

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
2. Visit `http://localhost:8080/contact`.
3. Fill out your Name, Email, and Message, then click **Send Message**.
4. The page will show a confirmation thanking you.
5. Look at your server terminal window: you will see your logged message printed right there!
