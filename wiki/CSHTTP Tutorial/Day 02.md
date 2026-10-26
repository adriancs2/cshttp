# Day 2: Clean Templates & Dynamic Content (Shared Layout)

In Day 1, we built a working multi-page website with `HandleHome()`, `HandleAbout()`, `HandleContact()`, and `HandleNotFound()`.

Today, we will clean up our code using a **Shared Template**, and then add live, dynamic server data to our pages!

---

## 1. Spotting the Problem in Our Day 1 Code

Let's look at two of our methods from the end of Day 1 (`HandleHome` and `HandleAbout`):

```csharp
    static void HandleHome()
    {
        string html = @"
    <html>
        <head>
            <title>Home</title>
        </head>
        <body>
            <a href=""/"">Home</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>
        </body>
    </html>
    ";
        Response.Write(html);
    }

    static void HandleAbout()
    {
        string html = @"
    <html>
        <head>
            <title>About Us</title>
        </head>
        <body>
            <a href=""/"">Home</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
            <h1>About Us</h1>
            <p>It's Me!</p>
        </body>
    </html>
    ";
        Response.Write(html);
    }
```

Look closely at those two methods. Almost **80% of the code is identical**!

Every single method (`HandleHome`, `HandleAbout`, `HandleContact`, `HandleNotFound`) has to repeat the exact same `<html>`, `<head>`, `<body>`, and navigation menu:

```html
<a href="/">Home</a> |
<a href="/about">About</a> |
<a href="/contact">Contact</a>
```

Imagine if our website had 20 pages, and we wanted to add one new link to the navigation bar—we would have to edit 20 different methods!

To solve this, we can create **one master template method** that holds the outer HTML and navigation bar once, and simply plug in the page title and body content for each page.

---

## 2. Injecting Variables into Strings (`$@"..."`)

Before we build our template method, we need a way to insert C# variables directly into an HTML string.

In Day 1, we used `@"..."` (a **verbatim string**) so our HTML could span across multiple lines:

```csharp
string html = @"
    <h1>Hello World</h1>
";
```

In C#, if you put a **`$`** symbol right in front of `@"..."` (making it **`$@"..."`**), it becomes an **interpolated string**. This lets you place C# variables directly inside the string using curly braces `{variableName}`!

For example:

```csharp
string pageTitle = "About Us";
string bodyText = "<p>It's Me!</p>";

string html = $@"
<html>
    <head>
        <title>{pageTitle}</title>
    </head>
    <body>
        {bodyText}
    </body>
</html>";
```

When C# runs this code, `{pageTitle}` is automatically replaced with `About Us`, and `{bodyText}` is replaced with `<p>It's Me!</p>`.

---

## 3. Creating the `RenderPage` Template Method

Now, let's create a shared method called `RenderPage` that accepts two parameters:
1. `string title` — the title shown on the browser tab (`<title>{title}</title>`).
2. `string bodyContent` — the unique HTML content for that specific page (`{bodyContent}`).

Add this method to your `Program` class:

```csharp
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
    </body>
</html>";

        Response.Write(html);
    }
```

Notice what `RenderPage` does:
- It wraps `<nav>` (our navigation menu) and `<hr />` (a horizontal divider line) at the top of every page.
- It places `{title}` inside `<title>...</title>`.
- It places `{bodyContent}` right below the navigation menu.
- Finally, it calls `Response.Write(html);` to send the finished page to the browser.

---

## 4. Simplifying Our Page Methods

Now that `RenderPage` handles the outer `<html>`, `<head>`, `<body>`, and `<nav>` menu, let's see how much cleaner `HandleHome()` becomes!

### Before (Day 1):
```csharp
    static void HandleHome()
    {
        string html = @"
    <html>
        <head>
            <title>Home</title>
        </head>
        <body>
            <a href=""/"">Home</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>
        </body>
    </html>
    ";
        Response.Write(html);
    }
```

### After (Using `RenderPage`):
```csharp
    static void HandleHome()
    {
        string content = @"
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>";

        RenderPage("Home", content);
    }
```

`HandleHome()` only needs to focus on its own unique content, and then pass `"Home"` and `content` to `RenderPage("Home", content);`!

Let's update all four page methods (`HandleHome`, `HandleAbout`, `HandleContact`, and `HandleNotFound`) to use `RenderPage`:

```csharp
    static void HandleHome()
    {
        string content = @"
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>";

        RenderPage("Home", content);
    }

    static void HandleAbout()
    {
        string content = @"
            <h1>About Us</h1>
            <p>It's Me!</p>";

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
            <p>The page that you are looking for does not exist.</p>";

        RenderPage("404 Not Found", content);
    }
```

---

## 5. Adding Dynamic Content: A Live Server Clock

Because all of our pages now share `RenderPage()`, any change we make inside `RenderPage()` will automatically appear on **every page** of our website!

Let's test this by adding a `<footer>` at the bottom of `RenderPage()` that displays the **current server time** using C#'s `DateTime.Now`.

1. First, add `using System;` at the very top of `Program.cs` (so we can use `DateTime`):
   ```csharp
   using System;
   using cshttp;
   ```

2. Next, add a `<footer>` section at the bottom of `RenderPage()`:
   ```csharp
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
    }
   ```

### What does `{DateTime.Now:yyyy-MM-dd HH:mm:ss}` mean?
- `DateTime.Now` gets the current date and time from your computer's clock at the exact moment the request arrives.
- `:yyyy-MM-dd HH:mm:ss` formats the date cleanly as `Year-Month-Day Hour:Minute:Second` (for example: `2026-10-26 21:35:10`).

---

## 6. Complete Code for Day 2

Here is how your complete `Program.cs` looks when everything is put together:

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
                break;
            case "/about":
                HandleAbout();
                break;
            case "/contact":
                HandleContact();
                break;
            default:
                HandleNotFound();
                break;
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
    }

    static void HandleHome()
    {
        string content = @"
            <h1>Welcome cshttp!</h1>
            <p>Refresh the page and watch the server clock at the bottom change!</p>";

        RenderPage("Home", content);
    }

    static void HandleAbout()
    {
        string content = @"
            <h1>About Us</h1>
            <p>It's Me!</p>";

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
            <p>The page that you are looking for does not exist.</p>";

        RenderPage("404 Not Found", content);
    }
}
```

---

## 7. Test It in Your Browser

1. Run the app (`dotnet run` or press **Start** in Visual Studio).
2. Visit `http://localhost:8080/`.
3. Notice the navigation bar at the top (`<hr />`) and the **Server Time** in the footer at the bottom.
4. Click through **About** and **Contact**—every page automatically shares the exact same navigation bar and footer!
5. Refresh the page several times (`F5`)—watch the seconds on the **Server Time** clock tick upward on every refresh, because your C# code generates the HTML live on every request!

In **Day 3**, we will learn how our web server can **accept input from the visitor** using URL Query Strings and an interactive HTML form.
