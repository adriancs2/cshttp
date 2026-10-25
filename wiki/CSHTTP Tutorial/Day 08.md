# Day 8: Saving to a File

Try this with your Day 7 code: post a few guestbook comments, then stop the server (`Ctrl + C`) and start it again.

All your comments are **gone**!

That is because our `List<GuestbookEntry>` lives in the computer's **memory (RAM)**. When the program stops, the memory is cleared. To keep data after a restart, we must save it somewhere permanent: a **file** on the hard disk.

---

## What You Will Learn Today

1. **`File.AppendAllText(path, text)`**: Adds a line to the end of a text file (and creates the file if it does not exist).
2. **`File.ReadAllLines(path)`**: Reads every line of a text file into a `string[]` array.
3. **`File.Exists(path)`**: Checks whether a file is there before we try to read it.
4. **`string.Split(...)`**: Cuts one line of text back into its separate parts.

---

## How Will We Store Each Entry?

We will save **one entry per line**, with the parts separated by a `|` (pipe) character:

```text
2026-10-23 14:30:05|Alice|Hello everyone!
2026-10-23 14:31:40|Bob|Nice website!
```

- To **save**: join the parts together with `|` and add the line to the file.
- To **load**: read each line and `Split` it at the `|` to get the parts back.

Two small safety rules:

- If someone types `|` in their **name**, our file would get confused. So we replace `|` in the name with `/`.
- If someone presses **Enter** inside their comment, the comment would be split across two lines in the file. So we replace line breaks in the message with a space.

## Where Is the File?

```csharp
static string dataFile = WebApp.MapPath("~/guestbook.txt");
```

`WebApp.MapPath("~/guestbook.txt")` gives us the full path of `guestbook.txt` inside our application folder (the folder where the program runs). We print it in the console when the server starts, so you can find the file and open it in Notepad!

## The Code

Update `Program.cs` with the following code. Look for the comments marked **`NEW IN DAY 8`**:

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
    // NEW IN DAY 8: The list starts empty. The entries are loaded from the file.
    static List<GuestbookEntry> guestbookEntries = new List<GuestbookEntry>();

    // NEW IN DAY 8: The full path of our data file
    static string dataFile = WebApp.MapPath("~/guestbook.txt");

    static void Main()
    {
        // NEW IN DAY 8: Load saved entries BEFORE the server starts
        LoadEntries();
        Console.WriteLine("Guestbook file: " + dataFile);

        WebApp.Run(8080, OnRequest);
    }

    // NEW IN DAY 8: Read every line of the file and turn it back into GuestbookEntry objects
    static void LoadEntries()
    {
        if (!File.Exists(dataFile))
        {
            return; // No file yet. That's fine: the guestbook is just empty.
        }

        string[] lines = File.ReadAllLines(dataFile);
        foreach (string line in lines)
        {
            // Split into at most 3 parts: time | name | message
            string[] parts = line.Split(new char[] { '|' }, 3);
            if (parts.Length != 3)
            {
                continue; // Skip broken or empty lines
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

    // NEW IN DAY 8: Add one entry as a new line at the end of the file
    static void SaveEntry(GuestbookEntry entry)
    {
        string time = entry.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string line = time + "|" + entry.Name + "|" + entry.Message;
        File.AppendAllText(dataFile, line + Environment.NewLine);
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
                GuestbookEntry entry = new GuestbookEntry
                {
                    // NEW IN DAY 8: Clean the text so it fits on one line of our file
                    Name = author.Replace("|", "/").Replace("\r", " ").Replace("\n", " "),
                    Message = comment.Replace("\r", " ").Replace("\n", " "),
                    Time = DateTime.Now
                };

                guestbookEntries.Add(entry);
                SaveEntry(entry); // NEW IN DAY 8: Also write it to the file
            }

            Response.Redirect("/guestbook");
            return;
        }

        StringBuilder listHtml = new StringBuilder();
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

        string content = $@"
            <h1>Community Guestbook</h1>
            <p>Leave a message for everyone visiting this site! Entries are now saved to a file.</p>

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
            <p>Our <a href=""/guestbook"">Guestbook</a> now remembers every comment, even after a restart!</p>";

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

## Test It in Your Browser

1. Run the app (`dotnet run`). Look at the console: it prints the full path of `guestbook.txt`.
2. Visit `http://localhost:8080/guestbook` and post two or three comments.
3. Open `guestbook.txt` in Notepad. Your comments are there, one per line!
4. Now **stop the server** (`Ctrl + C`) and **start it again** (`dotnet run`).
5. Refresh the guestbook page. **Your comments are still there!** 🎉

Congratulations! Your web app now has **permanent storage**.

---

> [!NOTE]
> #### 💡 Good to Know: What if 2 People Click at Once?
> Our code works great on your machine! But in real life, if two visitors click **Submit** at the **EXACT SAME MILLISECOND**, they might clash while saving to the file.
> 
> In C#, a tool called **`lock`** (and later, real databases) prevents this clash. We'll explore that in a future topic once you're comfortable with the basics!

---

## Summary

| Concept | What It Does |
| :--- | :--- |
| **`File.AppendAllText(path, text)`** | Appends a line of text to the file (creates it if missing) |
| **`File.ReadAllLines(path)`** | Reads all lines from a file into a `string[]` array |
| **`File.Exists(path)`** | Checks if the file exists before reading |
| **`string.Split(...)`** | Splits a single line of text back into pieces using a delimiter |

In **Day 9**, we will learn that a web server can send much more than HTML pages: stylesheets, pictures, and plain text — and how the browser knows which is which.
