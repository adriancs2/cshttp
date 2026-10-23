# Day 2: Clean Templates & Dynamic Content (Shared Layout)

In Day 1, notice how every single method (`HandleHome`, `HandleAbout`, `HandleContact`) had to repeat the entire `<html>`, `<head>`, `<body>`, and navigation menu over and over again.

If we wanted to change a navigation link, we would have had to update 4 different places!

In Day 2, we introduce **Templates**. We create one single master layout method called `RenderPage(...)` that wraps all our pages automatically, and we insert dynamic server data (the real-time server clock) using C# string interpolation (`$""`).

---

## What You Will Learn Today

1. **`RenderPage(title, bodyContent)`**: A reusable master layout function that generates the header, navigation bar, and footer once for the whole website.
2. **String Interpolation (`$""`)**: Injecting C# variables directly into HTML strings using `{variableName}`.
3. **Dynamic Server Time**: Displaying `DateTime.Now` live in the footer on every page refresh.

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

    // NEW IN DAY 2: One shared template for the whole website
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

    static void HandleHome()
    {
        string content = @"
            <h1>Welcome Home</h1>
            <p>Refresh the page and watch the server clock at the bottom change!</p>";

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
2. Visit `http://localhost:8080/`.
3. Notice the navigation bar at the top and the **Server Time** at the bottom.
4. Refresh the page several times—the time updates on every request because the C# code executes live on the server!
