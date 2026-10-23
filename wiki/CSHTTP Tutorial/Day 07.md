# Day 7: Your First Class (GuestbookEntry)

In Day 5 and Day 6, every guestbook entry was stored as one long string:

```text
[2026-10-22 09:05] Alice: First comment! C# web dev is awesome.
```

This works, but it has a problem: the name, the time, and the comment are all glued together. What if we want to show the name in **bold**, or the time in a smaller grey font? We would have to cut the string apart again.

In Day 7, we create our very first **C# class** called `GuestbookEntry`. A class is like a **form with labelled boxes**: one box for the name, one box for the message, one box for the time.

---

## What You Will Learn Today

1. **`class`**: Designing your own data type with **properties** (`Name`, `Message`, `Time`).
2. **`new`**: Creating an object from a class (filling in a new form).
3. **`List<GuestbookEntry>`**: A list that holds objects instead of plain strings.
4. **Accessing properties**: Reading `entry.Name` and `entry.Message` inside a `foreach` loop.

---

## A Class Is a Blueprint

Here is our class:

```csharp
class GuestbookEntry
{
    public string Name { get; set; }
    public string Message { get; set; }
    public DateTime Time { get; set; }
}
```

- `class GuestbookEntry` — the name of our new type.
- `public string Name { get; set; }` — a **property**. It is a labelled box that can hold a `string`. `get` means we can read it, `set` means we can change it.
- `DateTime Time` — this box holds a date and time, not text.

To create a new entry, we use `new` and fill in the boxes:

```csharp
GuestbookEntry entry = new GuestbookEntry();
entry.Name = "Alice";
entry.Message = "Hello!";
entry.Time = DateTime.Now;
```

Or the shorter way (called an **object initializer**), which does exactly the same thing:

```csharp
GuestbookEntry entry = new GuestbookEntry
{
    Name = "Alice",
    Message = "Hello!",
    Time = DateTime.Now
};
```

---

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 7`**:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using cshttp;

// NEW IN DAY 7: Our very first class!
class GuestbookEntry
{
    public string Name { get; set; }
    public string Message { get; set; }
    public DateTime Time { get; set; }
}

static class Program
{
    // NEW IN DAY 7: The list now holds GuestbookEntry objects instead of strings
    static List<GuestbookEntry> guestbookEntries = new List<GuestbookEntry>()
    {
        new GuestbookEntry { Name = "System", Message = "Welcome to our new Guestbook!", Time = new DateTime(2026, 10, 22, 9, 0, 0) },
        new GuestbookEntry { Name = "Alice", Message = "First comment! C# web dev is awesome.", Time = new DateTime(2026, 10, 22, 9, 5, 0) }
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
                // NEW IN DAY 7: Create a new GuestbookEntry object and fill in its properties
                GuestbookEntry entry = new GuestbookEntry
                {
                    Name = author,
                    Message = comment,
                    Time = DateTime.Now
                };

                guestbookEntries.Add(entry);
            }

            Response.Redirect("/guestbook");
            return;
        }

        StringBuilder listHtml = new StringBuilder();
        listHtml.Append("<ul>");
        foreach (GuestbookEntry entry in guestbookEntries)
        {
            // NEW IN DAY 7: Each part has its own property, so we can style each part differently
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
            <p>Visit the <a href=""/guestbook"">Guestbook</a> to see entries built from a C# class!</p>";

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
3. Notice each entry now shows the **name in bold**, the **time in small grey text**, and the message on its own line.
4. Post a new comment. It appears with the same nice layout.

---

## Why Is a Class Better Than a String?

| One long string | A `GuestbookEntry` class |
| :--- | :--- |
| `"[2026-10-22 09:05] Alice: Hello"` | `Name = "Alice"`, `Message = "Hello"`, `Time = ...` |
| To get the name, you must cut the string apart | Just read `entry.Name` |
| Hard to style each part differently | Easy: `<b>{entry.Name}</b>` |
| Time is just text | Time is a real `DateTime`: you can sort it, compare it, and format it any way you like |

**Try it yourself:** Change `entry.Time.ToString("yyyy-MM-dd HH:mm")` to `entry.Time.ToString("dddd, d MMMM")` and see what happens!

In **Day 8**, we will solve a big problem: every time you stop the server, all the guestbook entries disappear. We will save them to a **file**.
