# Day 3: Accepting User Input (URL Query String & GET Forms)

So far, our website only sends information to the visitor. But modern web apps need to **interact** with the visitor!

In Day 3, we teach our web server how to accept input from users. We do this with **Query Strings** (e.g., `/greet?name=Alice`) and simple **HTML Forms** using `method="get"`.

---

## What You Will Learn Today

1. **`Request.QueryString["name"]`**: Reads data sent by the browser in the URL query string (after the `?`).
2. **`string.IsNullOrEmpty()`**: Checks if the visitor provided a name or left it blank.
3. **Interactive HTML `<form>`**: Lets users type their name into an input box and click a button to send it to the C# server.

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

    // NEW IN DAY 3: Accepting user input via URL Query String & HTML Form
    static void HandleGreet()
    {
        // 1. Read the "name" parameter from the URL (e.g. /greet?name=Alice)
        string name = Request.QueryString["name"];

        // 2. Decide what message to show using standard C# if/else
        string greetingMessage;
        if (string.IsNullOrEmpty(name))
        {
            greetingMessage = "<p>Please enter your name below, or try clicking a quick link!</p>";
        }
        else
        {
            greetingMessage = $"<h2>Hello, {name}! Welcome to C# Web Development!</h2>";
        }

        // 3. Render both the message AND the form so they can try again
        string content = $@"
            <h1>Interactive Greeting</h1>
            {greetingMessage}

            <form action=""/greet"" method=""get"">
                <label>Your Name:</label>
                <input type=""text"" name=""name"" placeholder=""Type your name..."" />
                <button type=""submit"">Say Hello</button>
            </form>

            <p>Or test query links directly: 
                <a href=""/greet?name=Alice"">Greet Alice</a> | 
                <a href=""/greet?name=Bob"">Greet Bob</a>
            </p>";

        RenderPage("Greet", content);
    }

    static void HandleHome()
    {
        string content = @"
            <h1>Welcome Home</h1>
            <p>Click on <a href=""/greet"">Greet</a> in the menu to test user input!</p>";

        RenderPage("Home", content);
    }

    static void HandleAbout()
    {
        string content = @"
            <h1>About Us</h1>
            <p>We build fast, zero-dependency web apps in C#.</p>";

        RenderPage("About Us", content);
    }

    static void HandleContact()
    {
        string content = @"
            <h1>Contact</h1>
            <p>Email us at hello@example.com</p>";

        RenderPage("Contact", content);
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
2. Visit `http://localhost:8080/greet`.
3. Type your name into the text box and click **Say Hello**.
4. Notice how the URL changes to `http://localhost:8080/greet?name=YourName`, and the C# server personalizes the greeting for you!
