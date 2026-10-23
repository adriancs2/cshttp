# Day 8: Saving to a File (and Sharing Safely with `lock`)

Try this with your Day 7 code: post a few guestbook comments, then stop the server (`Ctrl + C`) and start it again.

All your comments are **gone**!

That is because our `List<GuestbookEntry>` lives in the computer's **memory (RAM)**. When the program stops, the memory is cleared. To keep data after a restart, we must save it somewhere permanent: a **file** on the hard disk.

Today's lesson has **two parts**:

- **Part 1:** Save and load the guestbook using a text file. You will see it working!
- **Part 2:** Learn about a hidden problem that only happens on a web server, and fix it with `lock`.

---

## What You Will Learn Today

1. **`File.AppendAllText(path, text)`**: Adds a line to the end of a text file (and creates the file if it does not exist).
2. **`File.ReadAllLines(path)`**: Reads every line of a text file into a `string[]` array.
3. **`File.Exists(path)`**: Checks whether a file is there before we try to read it.
4. **`string.Split(...)`**: Cuts one line of text back into its separate parts.
5. **`lock`**: Makes sure only one visitor at a time can change our shared list (Part 2).

---

# Part 1: Saving the Guestbook to a File

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

## The Code (Part 1)

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

## Test It (Part 1)

1. Run the app (`dotnet run`). Look at the console: it prints the full path of `guestbook.txt`.
2. Visit `http://localhost:8080/guestbook` and post two or three comments.
3. Open `guestbook.txt` in Notepad. Your comments are there, one per line!
4. Now **stop the server** (`Ctrl + C`) and **start it again** (`dotnet run`).
5. Refresh the guestbook page. **Your comments are still there!** 🎉

Congratulations! Your web app now has **permanent storage**.

---

# Part 2: The Hidden Problem (and `lock`)

Our guestbook works. But there is a problem hiding inside it — one that you will **almost never see while testing alone**, but that **will** happen on a real website with many visitors.

## A Web Server Serves Many Visitors at the Same Time

When you run a normal console program, there is only **one** "worker" running your code from top to bottom.

A web server is different. Imagine a busy restaurant:

- Each visitor's request is a **customer**.
- The server has **many waiters** (called **threads**). Each waiter serves one customer at the same time as the others.
- Our `guestbookEntries` list is **one notebook** on the counter, shared by all waiters.

Now imagine this:

1. **Waiter A** is reading the notebook to show the guestbook page to Alice (our `foreach` loop).
2. At the **exact same moment**, **Waiter B** writes a new comment from Bob into the same notebook (`guestbookEntries.Add`).

Waiter A was in the middle of reading — and the notebook changed while they were still reading it! In C#, this crashes with an error:

```text
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
```

Even worse: if two waiters both call `Add` at the exact same moment, one comment can be **silently lost**, or two lines can get mixed up in the file.

It does not happen every time. It happens **randomly**, only when two requests touch the list at the same instant. That is why it is so dangerous: everything looks fine during testing, and then it breaks when real visitors arrive.

## The Solution: `lock` (Only One Pen)

The fix is to make a rule: **only one waiter may use the notebook at a time.** We do this with a "pen" that waiters must hold before touching the notebook. If another waiter has the pen, you wait until they put it down.

In C#, the pen is any object, and the rule is the `lock` keyword:

```csharp
// The "pen". There is only one, shared by everybody.
static readonly object guestbookLock = new object();
```

```csharp
lock (guestbookLock)
{
    // Only ONE thread at a time can be inside these curly braces.
    // Everyone else waits at the door until this block is finished.
    guestbookEntries.Add(entry);
    SaveEntry(entry);
}
```

We must use the **same** lock everywhere the list is used — both when **writing** (adding a comment) and when **reading** (building the HTML list).

**Keep the lock short.** Only put the code that touches the shared list or file inside it. The waiters waiting at the door are customers waiting for their page!

## The Code (Part 2)

Here is the complete, safe version. Look for the comments marked **`NEW IN DAY 8 (PART 2)`**:

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
    static List<GuestbookEntry> guestbookEntries = new List<GuestbookEntry>();
    static string dataFile = WebApp.MapPath("~/guestbook.txt");

    // NEW IN DAY 8 (PART 2): The "pen". Only one thread can hold it at a time.
    static readonly object guestbookLock = new object();

    static void Main()
    {
        // No lock needed here: the server has not started yet, so nobody else is running
        LoadEntries();
        Console.WriteLine("Guestbook file: " + dataFile);

        WebApp.Run(8080, OnRequest);
    }

    static void LoadEntries()
    {
        if (!File.Exists(dataFile))
        {
            return;
        }

        string[] lines = File.ReadAllLines(dataFile);
        foreach (string line in lines)
        {
            string[] parts = line.Split(new char[] { '|' }, 3);
            if (parts.Length != 3)
            {
                continue;
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
                    Name = author.Replace("|", "/").Replace("\r", " ").Replace("\n", " "),
                    Message = comment.Replace("\r", " ").Replace("\n", " "),
                    Time = DateTime.Now
                };

                // NEW IN DAY 8 (PART 2): Only one visitor at a time may add to the list and the file
                lock (guestbookLock)
                {
                    guestbookEntries.Add(entry);
                    SaveEntry(entry);
                }
            }

            Response.Redirect("/guestbook");
            return;
        }

        StringBuilder listHtml = new StringBuilder();
        int count;

        // NEW IN DAY 8 (PART 2): Nobody may add a comment while we are reading the list
        lock (guestbookLock)
        {
            count = guestbookEntries.Count;

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
        }

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
            <h2>Recent Entries ({count})</h2>
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

## Test It (Part 2)

1. Run the app (`dotnet run`).
2. Everything works exactly the same as Part 1 — and that is the point! `lock` does not change *what* your program does. It only makes sure it keeps working correctly when many visitors arrive at once.
3. Open the guestbook in two browser windows and post from both. Every comment is saved, in order.

---

## Summary

| Problem | Solution |
| :--- | :--- |
| Data disappears when the server stops | Save it to a file (`File.AppendAllText`) and load it at startup (`File.ReadAllLines`) |
| Many visitors use the same list at the same time | Wrap every use of the shared list in `lock (guestbookLock) { ... }` |

**Remember:** Any `static` variable that your request handlers **change** is shared by every visitor. If you change it, protect it with a `lock`.

In **Day 9**, we will learn that a web server can send much more than HTML pages: stylesheets, pictures, and plain text — and how the browser knows which is which.
