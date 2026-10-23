# Day 1: Multi-Page Routing (Home, About, Contact & 404)

In Day 0, our server returned the exact same "Hello World" page no matter what URL was visited.

In Day 1, we turn our server into a **real multi-page website**! When a visitor navigates to `/home`, `/about`, or `/contact`, our server checks what page they requested and serves the corresponding content.

---

## What You Will Learn Today

1. **`Request.Path`**: Reads the URL path requested by the browser (for example: `/home` or `/contact`).
2. **The `switch` statement**: Routes each URL to its own C# handler method (`HandleHome()`, `HandleAbout()`, etc.).
3. **HTTP 404 Status Code**: When someone asks for a page that does not exist, we return `Response.StatusCode = 404` and show a friendly error page.
4. **`Response.End()`**: Tells the server that we are finished sending content for this request.

---

## The Code

Replace `Program.cs` with the following code and run it (`dotnet run`):

```csharp
using cshttp;

static class Program
{
    static void Main()
    {
        // Listening Port: 8080
        // http://localhost:8080/
        WebApp.Run(8080, OnRequest);
    }

    static void OnRequest()
    {
        // 1. Get the requested path in lower case
        string path = Request.Path.ToLower();
        
        // 2. Decide which page to show based on the URL path
        switch(path)
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

    static void HandleHome()
    {
        string html = @"
    <html>
        <head>
            <title>hello</title>
        </head>
        <body>
            <h1>Welcome Home</h1>
            <a href=""/"">Home</a>
            <a href=""/about"">About</a>
            <a href=""/contact"">Contact</a>
        </body>
    </html>
    ";
        Response.Write(html);
        Response.End();
    }

    static void HandleAbout()
    {
        string html = @"
    <html>
        <head>
            <title>hello</title>
        </head>
        <body>
            <h1>About Us</h1>
            <a href=""/"">Home</a>
            <a href=""/about"">About</a>
            <a href=""/contact"">Contact</a>
        </body>
    </html>
    ";
        Response.Write(html);
        Response.End();
    }

    static void HandleContact()
    {
        string html = @"
    <html>
        <head>
            <title>Contact</title>
        </head>
        <body>
            <h1>Contact</h1>
            <a href=""/"">Home</a>
            <a href=""/about"">About</a>
            <a href=""/contact"">Contact</a>
        </body>
    </html>
    ";
        Response.Write(html);
        Response.End();
    }

    static void HandleNotFound()
    {
        string html = @"
    <html>
        <head>
            <title>404 Not Found</title>
        </head>
        <body>
            <h1>404 Not Found</h1>
            <a href=""/"">Home</a>
            <a href=""/about"">About</a>
            <a href=""/contact"">Contact</a>
        </body>
    </html>
    ";
        Response.StatusCode = 404;
        Response.Write(html);
        Response.End();
    }
}
```

---

## Test It in Your Browser

1. Run the app (`dotnet run`).
2. Visit `http://localhost:8080/` -> You will see the Home page.
3. Click the links on the page to visit `/about` and `/contact`.
4. Try typing a random address like `http://localhost:8080/pizza` -> You will see your custom 404 page!
