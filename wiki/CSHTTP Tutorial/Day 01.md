# Day 1: Getting Started & Your First Multi-Page Website

Welcome! In this tutorial series, you will learn how to build real, interactive web applications using C#.

We will use **cshttp** — a high-performance, self-sustained, standalone micro web framework for C#. It lets you run web servers and create web apps with almost zero boilerplate or configuration.

- **Official GitHub Repository**: [https://github.com/adriancs2/cshttp](https://github.com/adriancs2/cshttp)
- **Releases & Downloads**: [https://github.com/adriancs2/cshttp/releases](https://github.com/adriancs2/cshttp/releases)

---

## 1. Prerequisites (Choose Your Development Environment)

Before starting, choose **ONE** of the following two development setups:

### Environment 1: Visual Studio (Community Edition - Recommended for Beginners)
Visual Studio provides a complete, all-in-one graphical development environment.
- Download: [https://visualstudio.microsoft.com/vs/community/](https://visualstudio.microsoft.com/vs/community/)
- **During Installation**:
  - In the installer workloads screen, check **".NET desktop development"** or **"ASP.NET and web development"**.
  - In the **Individual components** tab, make sure **.NET Framework 4.8 development tools** and the latest **.NET SDK** (e.g., .NET 8 or newer) are selected.

### Environment 2: Visual Studio Code + .NET SDK (Lightweight & Cross-Platform)
If you prefer a lightweight code editor:
1. **Install .NET SDK**:
   - Download & install the .NET SDK (.NET 8 or newer): [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. **Install VS Code**:
   - Download: [https://code.visualstudio.com/](https://code.visualstudio.com/)
3. **Install Extension**:
   - Open VS Code, go to the Extensions tab (`Ctrl + Shift + X`), search for and install the official **"C# Dev Kit"** extension.

---

## 2. Getting cshttp into Your Project (3 Methods)

There are three ways to get cshttp into your project, depending on your preference:

### Method 1: Download the Starter Pack (Easiest & Recommended)
Download the pre-configured **cshttp-starter-pack.zip** from the [GitHub Releases page](https://github.com/adriancs2/cshttp/releases).

The Starter Pack is pre-configured to support **both modern .NET (8.0, 9.0, 10.0+) and classic .NET Framework 4.8** automatically.

When you unzip the folder, you will see:
```text
starter-pack/
├── lib/
│   ├── net8.0/               <-- Pre-compiled DLL for modern .NET
│   │   └── cshttp.dll
│   └── net48/                <-- Pre-compiled DLL for .NET Framework 4.8
│       └── cshttp.dll
├── StarterApp.csproj         <-- Pre-configured project (auto-detects runtime)
├── Program.cs                <-- Your application code
└── README.md
```
Everything is pre-wired. Open the folder, press run, and it works immediately.

### Method 2: Manually Include the Pre-Compiled DLL
If you already have an existing C# project:
1. Grab the compiled `cshttp.dll`:
   - **Download** it directly from [GitHub Releases](https://github.com/adriancs2/cshttp/releases).
   - *OR* **Compile from source**: Clone the repo from [https://github.com/adriancs2/cshttp](https://github.com/adriancs2/cshttp) and build the solution in `Release` mode.
2. Copy `cshttp.dll` into a `lib` folder inside your project.
3. In your `.csproj` file, add a reference to it:
   ```xml
   <ItemGroup>
     <Reference Include="cshttp"><HintPath>lib\cshttp.dll</HintPath></Reference>
   </ItemGroup>
   ```

### Method 3: Directly Include All Source Code
If you want complete control and want to step through cshttp's source code line by line while debugging:
1. Clone or download the repository: [https://github.com/adriancs2/cshttp](https://github.com/adriancs2/cshttp).
2. Either add the `cshttp` project (`src\cshttp\cshttp.csproj`) to your Solution and add a project reference, or copy the `Core/`, `Web/`, and `Server/` source folders directly into your project.

---

## 3. Starting from `Program.cs`

Open `Program.cs` in Visual Studio via the **Solution Explorer**.

<!-- [Screenshot: Visual Studio Solution Explorer showing Program.cs] -->

Here is what you initially see in a new C# console program:

```csharp
static class Program
{
    static void Main()
    {

    }
}
```

Now, let's initialize our web application!

1. At the very top of the file, add `using cshttp;` to import the cshttp web framework.
2. Inside `Main()`, call `WebApp.Run(8080, OnRequest);`.
3. Create the `OnRequest()` method below `Main()`.

```csharp
// Add this include line at the topmost to import cshttp web framework
using cshttp;

static class Program
{
    static void Main()
    {
        // Begin the web application
        WebApp.Run(8080, OnRequest);
    }

    static void OnRequest()
    {
        
    }
}
```

### Understanding `WebApp.Run(8080, OnRequest);`

Let's look closely at the two parameters inside this line:

```csharp
WebApp.Run(8080, OnRequest);
```

1. **`8080` (Port Number)**:
   `8080` is the port number where your website will be listening. Once the program is running, you can open your web browser and visit your website at this address:
   ```text
   http://localhost:8080
   ```
   <!-- [Screenshot: Browser address bar showing http://localhost:8080] -->

2. **`OnRequest` (Request Handler Method)**:
   The second parameter, `OnRequest`, routes every incoming HTTP request to our dedicated `OnRequest()` method. Behind the scenes, `WebApp` runs in a continuous loop, keeping your web application alive and always ready to call `OnRequest()` whenever someone visits your site.

---

## 4. Returning Your First Web Page ("Hello World")

Right now, `OnRequest()` is empty, so the server doesn't send anything back to the browser yet.

Next, what we want to do is return website content (HTML) to the user using `Response.Write(html);`:

```csharp
using cshttp;

static class Program
{
    static void Main()
    {
        // Begin the web application
        WebApp.Run(8080, OnRequest);
    }

    static void OnRequest()
    {
        string html = @"
<html>
    <head>
        <title>Hello World</title>
    </head>
    <body>
        <h1>Welcome cshttp!</h1>
        <p>Hello World!</p>
    </body>
</html>
";
        Response.Write(html);
    }
}
```

### Run and See It!

1. Press **Start / Play** in Visual Studio (or run `dotnet run` in your terminal).
2. Open your browser and visit `http://localhost:8080`.
3. You will see your very first web page: **"Welcome cshttp! Hello World!"**

<!-- [Screenshot: Browser showcasing the first "Hello World" page] -->

*(To stop the server at any time so you can edit code, click the **Stop** button in Visual Studio or press `Ctrl + C` in your terminal.)*

---

## 5. Understanding URL Paths

In a real website, we normally have different addresses for different pages, such as:

```text
http://www.mywebsite.com/
http://www.mywebsite.com/home
http://www.mywebsite.com/about
http://www.mywebsite.com/contact
```

Notice the ending part after the domain name. Above, we have 4 distinct pages:

| Page | Path |
| :--- | :--- |
| Root | `/` |
| Home page | `/home` |
| About page | `/about` |
| Contact page | `/contact` |

In the web development world, this ending part is called the **path**.

When we are developing websites locally on our own computer, we don't have a custom domain name bound to it yet (such as `www.mywebsite.com`). Instead, the default domain for your own machine is always **`localhost`**.

Thus, on your browser and our `WebApp` server, those paths look like this:

```text
http://localhost:8080/
http://localhost:8080/home
http://localhost:8080/about
http://localhost:8080/contact
```

---

## 6. Identifying the Path in Code

Now, let's go back to our code! We will expand `OnRequest()` to identify which **path** the user is currently browsing.

We can read the path using `Request.Path.ToLower()`, and use a standard C# `switch` statement to check it:

```csharp
using cshttp;

static class Program
{
    static void Main()
    {
        WebApp.Run(8080, OnRequest);
    }

    static void OnRequest()
    {
        string html = "";
        string path = Request.Path.ToLower();

        switch (path)
        {
            case "/":
            case "/home":
                break;
            default:
                break;
        }
    }
}
```

This is a standard, basic `switch` statement in C# to branch our logic based on `path`.

---

## 7. Returning Content for Each Path (and 404 Not Found)

Next, let's return the specific HTML content that we want to show the user for each `path`:

```csharp
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
        string html = "";

        switch (path)
        {
            case "/":
            case "/home":
                html = @"
    <html>
        <head>
            <title>Hello World</title>
        </head>
        <body>
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>
        </body>
    </html>
    ";
                break;
            default:
                Response.StatusCode = 404;
                html = @"
    <html>
        <head>
            <title>Not Found</title>
        </head>
        <body>
            <h1>404 Not Found</h1>
            <p>The page that you are looking for does not exist.</p>
        </body>
    </html>
                ";
                break;
        }

        Response.Write(html);
    }
}
```

The code above handles the following paths:

```text
http://localhost:8080/
http://localhost:8080/home
>> Shows the Home page ("Welcome cshttp!")
```

Any other path that is **not** defined in our `switch` statement, such as:

```text
http://localhost:8080/about
http://localhost:8080/contact
http://localhost:8080/any-other-pages
```

will jump to the `default:` block, set `Response.StatusCode = 404`, and return the **"404 Not Found"** page!

---

## 8. Adding Support for `/about` and `/contact`

How do we add support for another path—let's say `/about`?

You simply add another `case "/about":` block inside the `switch` statement:

```csharp
            case "/about":
                html = @"
    <html>
        <head>
            <title>About Us</title>
        </head>
        <body>
            <h1>About Us</h1>
            <p>It's Me!</p>
        </body>
    </html>
    ";
                break;
```

And we can do the exact same thing for `/contact`!

Here is what the complete code looks like when we put `/`, `/home`, `/about`, `/contact`, and the `404` page together:

```csharp
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
        string html = "";

        switch (path)
        {
            case "/":
            case "/home":
                html = @"
    <html>
        <head>
            <title>Hello World</title>
        </head>
        <body>
            <h1>Welcome cshttp!</h1>
            <p>Hello World!</p>
        </body>
    </html>
    ";
                break;
            case "/about":
                html = @"
    <html>
        <head>
            <title>About Us</title>
        </head>
        <body>
            <h1>About Us</h1>
            <p>It's Me!</p>
        </body>
    </html>
    ";
                break;
            case "/contact":
                html = @"
    <html>
        <head>
            <title>Contact</title>
        </head>
        <body>
            <h1>Contact</h1>
            <p>Email us at hello@example.com</p>
        </body>
    </html>
    ";
                break;
            default:
                Response.StatusCode = 404;
                html = @"
    <html>
        <head>
            <title>Not Found</title>
        </head>
        <body>
            <h1>404 Not Found</h1>
            <p>The page that you are looking for does not exist.</p>
        </body>
    </html>
                ";
                break;
        }

        Response.Write(html);
    }
}
```

---

## 9. Adding Navigation Links Between Pages

Typing URLs into the browser's address bar works, but real websites have clickable menu links (`<a href="...">`) so visitors can click between pages!

Let's add a simple navigation menu to our pages:

```html
<a href="/">Home</a> |
<a href="/about">About</a> |
<a href="/contact">Contact</a>
```

And notice one more thing: as our HTML grows, putting all the HTML strings directly inside the `switch` statement starts to make `OnRequest()` look long and crowded.

We can keep `OnRequest()` super clean and easy to read by moving each page's HTML into its own helper method (`HandleHome()`, `HandleAbout()`, `HandleContact()`, and `HandleNotFound()`):

```csharp
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

    static void HandleContact()
    {
        string html = @"
    <html>
        <head>
            <title>Contact</title>
        </head>
        <body>
            <a href=""/"">Home</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
            <h1>Contact</h1>
            <p>Email us at hello@example.com</p>
        </body>
    </html>
    ";
        Response.Write(html);
    }

    static void HandleNotFound()
    {
        Response.StatusCode = 404;
        string html = @"
    <html>
        <head>
            <title>404 Not Found</title>
        </head>
        <body>
            <a href=""/"">Home</a> |
            <a href=""/about"">About</a> |
            <a href=""/contact"">Contact</a>
            <h1>404 Not Found</h1>
            <p>The page that you are looking for does not exist.</p>
        </body>
    </html>
    ";
        Response.Write(html);
    }
}
```

---

## 10. Test It in Your Browser

1. Run the app (`dotnet run` or press **Start** in Visual Studio).
2. Visit `http://localhost:8080/` -> You will see the **Home** page.
3. Click the links at the top of the page to visit **About** (`/about`) and **Contact** (`/contact`).
4. Try typing a random address like `http://localhost:8080/pizza` -> You will see your custom **404 Not Found** page!

In **Day 2**, we will solve the next problem you might have noticed: repeating the navigation links (`<a href="/">Home</a>...`) in every single method! We will create a shared master template so we only write our layout once.
