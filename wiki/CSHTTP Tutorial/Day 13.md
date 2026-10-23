# Day 13: Let a Library Write the JSON (Newtonsoft.Json)

In Day 12, we built JSON **by hand**. It worked, but think about how much we had to remember:

- Put `[` at the start and `]` at the end.
- Put a comma **between** items, but **not** after the last one.
- Put `{` and `}` around every object.
- Write every key in quotes: `"name":`.
- Encode **every** text value with `JavaScriptStringEncode`, or one `"` breaks everything.
- Turn dates and numbers into text ourselves.

And if we add **one new property** to `GuestbookEntry`, we must remember to add it to the JSON code too. One small mistake, and JavaScript cannot read the data.

Turning C# objects into JSON is something **almost every web app** needs. So programmers have written ready-made code that does it perfectly, and shared it. This ready-made code is called a **library**.

Today we use the most popular JSON library for C#: **Newtonsoft.Json** (also known as **Json.NET**).

> **Is a library a must?** No! Day 12 proved you can write JSON yourself — it is just text. A library is a **convenience**: less code, fewer mistakes, and it handles tricky cases (quotes, line breaks, dates, `null`, lists inside objects) that are easy to get wrong by hand.

---

## What You Will Learn Today

1. **NuGet**: How to add a library to your project.
2. **The shape of JSON values**: text, numbers, `true`/`false`, dates, lists, and `null`.
3. **`JsonConvert.SerializeObject(obj)`**: Turns any C# object (or list of objects) into JSON — in one line.
4. **How C# maps to JSON**: how each property type becomes a JSON value.
5. **`[JsonProperty("name")]`**: Choosing the JSON key name yourself — and the problem it solves.

---

## Step 1: Install Newtonsoft.Json with NuGet

**NuGet** is like an app store for C# libraries. You ask for a library by name, and it downloads it and connects it to your project.

**Option A — Terminal (VS Code or any terminal):** Open a terminal in your project folder (where the `.csproj` file is) and run:

```bash
dotnet add package Newtonsoft.Json
```

**Option B — Visual Studio:** Right-click your project in **Solution Explorer** → **Manage NuGet Packages...** → **Browse** tab → search for `Newtonsoft.Json` → click **Install**.

Either way, one new line appears in your `.csproj` file (the version number may be different):

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

That's it! Newtonsoft.Json works on **both** .NET Framework 4.8 and modern .NET (8, 9, 10...), exactly like cshttp.

To use it, add this line at the top of `Program.cs`:

```csharp
using Newtonsoft.Json;
```

---

## Step 2: A Complete JSON Example

Before letting the library write JSON, let's look at **every kind of value** JSON can hold. Here is a student, written by hand:

```json
{
  "Name": "Alice",
  "Age": 12,
  "Score": 95.5,
  "IsMember": true,
  "JoinDate": "2026-10-23T14:30:00",
  "Hobbies": [
    "drawing",
    "football"
  ],
  "Nickname": null
}
```

| Key | Value | Kind of value |
| :--- | :--- | :--- |
| `"Name"` | `"Alice"` | **Text** (a string) — always in double quotes |
| `"Age"` | `12` | **Whole number** — no quotes |
| `"Score"` | `95.5` | **Decimal number** — no quotes, a dot for decimals |
| `"IsMember"` | `true` | **Boolean** — `true` or `false`, no quotes |
| `"JoinDate"` | `"2026-10-23T14:30:00"` | **Date** — JSON has no date type, so it is written as text in a standard format (`year-month-dayThour:minute:second`) |
| `"Hobbies"` | `["drawing", "football"]` | **List** (an array) — square brackets, items separated by commas |
| `"Nickname"` | `null` | **Nothing** — the value is empty / not set |

---

## Step 3: The Same Thing as a C# Class

Now here is a C# class whose object will become **exactly** that JSON:

```csharp
class Student
{
    public string Name { get; set; }
    public int Age { get; set; }
    public double Score { get; set; }
    public bool IsMember { get; set; }
    public DateTime JoinDate { get; set; }
    public List<string> Hobbies { get; set; }
    public string Nickname { get; set; }
}
```

And an object of it, with the same values:

```csharp
Student student = new Student
{
    Name = "Alice",
    Age = 12,
    Score = 95.5,
    IsMember = true,
    JoinDate = new DateTime(2026, 10, 23, 14, 30, 0),
    Hobbies = new List<string> { "drawing", "football" },
    Nickname = null
};
```

Now the magic line:

```csharp
string json = JsonConvert.SerializeObject(student, Formatting.Indented);
```

The text in `json` is **exactly** the JSON from Step 2 — character for character!

- **Serialize** means "turn an object into text".
- `Formatting.Indented` adds line breaks and spaces so humans can read it easily. Without it, you get the same JSON on **one line** (smaller, which is better for sending over the network):

```json
{"Name":"Alice","Age":12,"Score":95.5,"IsMember":true,"JoinDate":"2026-10-23T14:30:00","Hobbies":["drawing","football"],"Nickname":null}
```

Look at how each C# type became a JSON value:

| C# type | C# value | JSON |
| :--- | :--- | :--- |
| `string` | `"Alice"` | `"Alice"` |
| `int` | `12` | `12` |
| `double` | `95.5` | `95.5` |
| `bool` | `true` | `true` |
| `DateTime` | `new DateTime(2026, 10, 23, 14, 30, 0)` | `"2026-10-23T14:30:00"` |
| `List<string>` | `{ "drawing", "football" }` | `["drawing","football"]` |
| `string` | `null` | `null` |

Notice one more thing: **each JSON key is the C# property name, exactly as written** — `Name` becomes `"Name"`, `IsMember` becomes `"IsMember"`. That is Newtonsoft's default. Remember this — it matters in Step 5!

---

## Step 4: Use It for the Guestbook

In Day 12, `HandleEntriesApi` needed a `for` loop, comma logic, and `JavaScriptStringEncode` on every value. With Newtonsoft, the whole list becomes JSON in **one line**:

```csharp
static void HandleEntriesApi()
{
    string json;

    lock (guestbookLock)
    {
        json = JsonConvert.SerializeObject(guestbookEntries);
    }

    Response.ContentType = "application/json";
    Response.Write(json);
}
```

The library also takes care of encoding: a message like `Say "hi"` safely becomes `"Say \"hi\""`. No more `JavaScriptStringEncode`!

---

## Step 5: The Problem — Our Live Page Breaks!

If you make **only** the change from Step 4 and open the **Live** page from Day 12... the names and messages are **gone**! The list shows entries with no text.

Let's look at `/api/entries`:

```json
[{"Name":"Ann","Message":"Hello","Time":"2026-10-23T16:54:11.3094439+08:00"}]
```

And our JavaScript from Day 12:

```javascript
nameEl.textContent = entry.name;       // lowercase "name"
messageEl.textContent = entry.message; // lowercase "message"
```

In Day 12, *we* wrote the keys by hand, in lowercase: `"name"`. Now Newtonsoft uses the C# property names: `"Name"`. And JavaScript is **case-sensitive**: `entry.name` and `entry.Name` are two **different** things. `entry.name` doesn't exist anymore, so nothing is shown.

We could fix it by changing the JavaScript to `entry.Name`. But think about what that means in a real project:

1. **Different naming habits.** C# programmers write property names in `PascalCase` (`Name`, `IsMember`). JavaScript programmers write in `camelCase` (`name`, `isMember`). Both sides want to follow their own habit.
2. **The JSON is a promise.** Other code (your JavaScript, a phone app, another programmer's website) already reads `"name"`. If the key changes, **all of them break**.
3. **Renaming in C# breaks the JSON.** If you later rename the C# property `Name` to `AuthorName`, the JSON key silently changes too — and every reader breaks again.
4. **Some keys aren't valid C# names.** What if the JSON must use `"guest-name"` or `"2fa"`? You can't name a C# property like that.

The real problem: **the C# property name and the JSON key name are stuck together.** We need a way to choose the JSON name **separately**.

---

## Step 6: `[JsonProperty("name")]` — Choose the JSON Key Yourself

Newtonsoft lets you put a small label, called an **attribute**, above a property:

```csharp
class GuestbookEntry
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("time")]
    public DateTime Time { get; set; }
}
```

`[JsonProperty("name")]` means: *"In the JSON, call this property `name`."*

Now the JSON looks like this again:

```json
[{"name":"Ann","message":"Hello","time":"2026-10-23T16:54:11.3094439+08:00"}]
```

| Problem | How `[JsonProperty]` solves it |
| :--- | :--- |
| C# wants `Name`, JavaScript wants `name` | Both get what they want |
| Other code depends on the key `"name"` | The key is fixed, written right there in the class |
| Renaming the C# property breaks the JSON | Rename `Name` to `AuthorName` freely — the JSON still says `"name"` |
| The key isn't a valid C# name | `[JsonProperty("guest-name")]` works fine |

The C# name is for **C# programmers**. The JSON name is for **whoever reads the JSON**. Now they are independent.

---

## Step 7: Dates Look Different Now

Notice the `time` value: `"2026-10-23T16:54:11.3094439+08:00"`. Newtonsoft writes dates in the international standard format (called **ISO 8601**), including the seconds and your time zone. That is great for computers, but not pretty for people.

JavaScript understands this format, so we let the browser turn it into a friendly local date:

```javascript
timeEl.textContent = ' (' + new Date(entry.time).toLocaleString() + ')';
```

---

## The Code

Here is the complete `Program.cs`. Look for the comments marked **`NEW IN DAY 13`**:

- `using Newtonsoft.Json;` at the top.
- `[JsonProperty]` labels on `GuestbookEntry`.
- A `Student` class and a `/api/sample` route, so you can see Step 2 and Step 3 in your browser.
- `HandleEntriesApi` is now just one `SerializeObject` call.
- The Live page formats the date with `toLocaleString()`.

> **Remember:** Install the package first (`dotnet add package Newtonsoft.Json`), or the code will not compile!

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using cshttp;
using Newtonsoft.Json; // NEW IN DAY 13

// NEW IN DAY 13: [JsonProperty("...")] chooses the key name used in the JSON
class GuestbookEntry
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("time")]
    public DateTime Time { get; set; }
}

// NEW IN DAY 13: A sample class, to see how every kind of value becomes JSON
class Student
{
    public string Name { get; set; }
    public int Age { get; set; }
    public double Score { get; set; }
    public bool IsMember { get; set; }
    public DateTime JoinDate { get; set; }
    public List<string> Hobbies { get; set; }
    public string Nickname { get; set; }
}

static class Program
{
    static List<GuestbookEntry> guestbookEntries = new List<GuestbookEntry>();
    static string dataFile = WebApp.MapPath("~/guestbook.txt");
    static readonly object guestbookLock = new object();

    // The shared notepad text (shared by every visitor, so it needs a lock too)
    static string noteText = "Hello! This is the shared notepad.\nEdit me and click Save.";
    static readonly object noteLock = new object();

    static void Main()
    {
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
            case "/cookies":
                HandleCookies();
                return;
            case "/notepad":
                HandleNotepad();
                return;
            case "/api/note":
                HandleNoteApi();
                return;
            case "/live":
                HandleLiveGuestbook();
                return;
            case "/api/entries":
                HandleEntriesApi();
                return;
            case "/api/sample":
                HandleSampleApi(); // NEW IN DAY 13
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
            case "/style.css":
                HandleStyle();
                return;
            case "/logo.svg":
                HandleLogo();
                return;
            case "/hello.txt":
                HandleHelloText();
                return;
            default:
                HandleNotFound();
                return;
        }
    }

    static void HandleStyle()
    {
        Response.ContentType = "text/css";
        Response.Write(@"
body {
    font-family: Arial, sans-serif;
    max-width: 700px;
    margin: 20px auto;
    background: #fdfdfd;
    color: #333;
}
nav {
    display: flex;
    align-items: center;
    gap: 10px;
}
nav a {
    color: #2b6cb0;
    text-decoration: none;
}
nav a:hover {
    text-decoration: underline;
}
h1 {
    color: #2c5282;
}
footer {
    color: gray;
}
.box {
    background: #f9f9f9;
    border: 1px solid #ddd;
    padding: 15px;
    margin-bottom: 15px;
}");
    }

    static void HandleLogo()
    {
        Response.ContentType = "image/svg+xml";
        Response.Write(@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""40"" height=""40"" viewBox=""0 0 40 40"">
    <circle cx=""20"" cy=""20"" r=""18"" fill=""#2c5282"" />
    <text x=""20"" y=""26"" font-size=""16"" font-family=""Arial"" fill=""white"" text-anchor=""middle"">C#</text>
</svg>");
    }

    static void HandleHelloText()
    {
        Response.ContentType = "text/plain";
        Response.Write("Hello! This is plain text.\n");
        Response.Write("<b>This is NOT bold</b>, because the browser knows this is not HTML.\n");
        Response.Write("Line breaks are kept exactly as they are.");
    }

    static void RenderPage(string title, string bodyContent)
    {
        string html = $@"<!DOCTYPE html>
<html>
    <head>
        <title>{title}</title>
        <link rel=""stylesheet"" href=""/style.css"" />
    </head>
    <body>
        <nav>
            <img src=""/logo.svg"" alt=""Logo"" width=""40"" height=""40"" />
            <a href=""/"">Home</a> |
            <a href=""/guestbook"">Guestbook</a> |
            <a href=""/live"">Live</a> |
            <a href=""/cookies"">Cookies</a> |
            <a href=""/notepad"">Notepad</a> |
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

   // A small helper that reads a cookie and decodes it.
    // Returns null if the browser did not send this cookie.
    static string GetCookieValue(string name)
    {
        HttpCookie cookie = Request.Cookies[name];
        if (cookie == null)
        {
            return null;
        }
        return HttpUtility.UrlDecode(cookie.Value);
    }

   // The cookie playground
    static void HandleCookies()
    {
        if (Request.HttpMethod == "POST")
        {
            string action = Request.Form["action"];

            if (action == "set-color")
            {
                // ISSUE or CHANGE: same name = the browser replaces the old value
                string color = Request.Form["color"];
                HttpCookie cookie = new HttpCookie("favorite_color", HttpUtility.UrlEncode(color));
                cookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Add(cookie);
            }
            else if (action == "delete-color")
            {
                // DELETE: send it again with an expiry date in the past
                HttpCookie cookie = new HttpCookie("favorite_color", "");
                cookie.Expires = DateTime.Now.AddDays(-1);
                Response.Cookies.Add(cookie);
            }
            else if (action == "set-secret")
            {
                // HttpOnly: JavaScript in the browser is NOT allowed to read this cookie
                HttpCookie cookie = new HttpCookie("secret_token", "ABC-123-XYZ");
                cookie.Expires = DateTime.Now.AddDays(30);
                cookie.HttpOnly = true;
                Response.Cookies.Add(cookie);
            }
            else if (action == "delete-secret")
            {
                HttpCookie cookie = new HttpCookie("secret_token", "");
                cookie.Expires = DateTime.Now.AddDays(-1);
                Response.Cookies.Add(cookie);
            }

            Response.Redirect("/cookies");
            return;
        }

        // READ on the server
        string favoriteColor = GetCookieValue("favorite_color");
        string secretToken = GetCookieValue("secret_token");

        string colorText = favoriteColor == null ? "(no cookie)" : HttpUtility.HtmlEncode(favoriteColor);
        string secretText = secretToken == null ? "(no cookie)" : HttpUtility.HtmlEncode(secretToken);

        // Only allow colours from our own list, so nobody can put strange text into our style="..."
        string boxColor = "white";
        if (favoriteColor == "pink" || favoriteColor == "lightblue" || favoriteColor == "lightgreen")
        {
            boxColor = favoriteColor;
        }

        string content = $@"
            <h1>Cookie Playground</h1>

            <div class=""box"" style=""background: {boxColor};"">
                <h2>1. What the SERVER sees (C#)</h2>
                <p>favorite_color = <b>{colorText}</b></p>
                <p>secret_token = <b>{secretText}</b></p>
            </div>

            <div class=""box"">
                <h2>2. What JAVASCRIPT sees (in the browser)</h2>
                <pre id=""jsCookies""></pre>
                <p><small>Notice: <b>secret_token</b> is missing here, because it is <b>HttpOnly</b>!</small></p>
            </div>

            <div class=""box"">
                <h2>3. Favourite colour (a normal cookie)</h2>
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""set-color"" />
                    <select name=""color"">
                        <option value=""pink"">Pink</option>
                        <option value=""lightblue"">Light Blue</option>
                        <option value=""lightgreen"">Light Green</option>
                    </select>
                    <button type=""submit"">Set / Change Cookie</button>
                </form>
                <br />
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""delete-color"" />
                    <button type=""submit"">Delete Cookie</button>
                </form>
            </div>

            <div class=""box"">
                <h2>4. Secret token (an HttpOnly cookie)</h2>
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""set-secret"" />
                    <button type=""submit"">Set Secret Cookie</button>
                </form>
                <br />
                <form action=""/cookies"" method=""post"">
                    <input type=""hidden"" name=""action"" value=""delete-secret"" />
                    <button type=""submit"">Delete Secret Cookie</button>
                </form>
            </div>

            <script>
                // READ in the browser: document.cookie holds every cookie JavaScript is allowed to see
                var text = document.cookie;
                if (text === '') {{
                    text = '(no cookies visible to JavaScript)';
                }}
                // textContent shows the text safely, just like HtmlEncode does in C#
                document.getElementById('jsCookies').textContent = text;
            </script>";

        RenderPage("Cookies", content);
    }

    // Sends or receives PLAIN TEXT. No HTML at all.
    static void HandleNoteApi()
    {
        if (Request.HttpMethod == "POST")
        {
            // Read the raw body of the request: exactly the text the browser sent
            string text;
            using (StreamReader reader = new StreamReader(Request.InputStream, Encoding.UTF8))
            {
                text = reader.ReadToEnd();
            }

            lock (noteLock)
            {
                noteText = text;
            }

            Console.WriteLine("Note saved (" + text.Length + " characters)");
            Console.WriteLine("Request.Form has " + Request.Form.Count + " fields (the body is not a form!)");

            Response.ContentType = "text/plain";
            Response.Write("Saved! (" + text.Length + " characters)");
            return;
        }

        // GET: send the current note as plain text
        string current;
        lock (noteLock)
        {
            current = noteText;
        }

        Response.ContentType = "text/plain";
        Response.Write(current);
    }

    // The notepad page. JavaScript talks to /api/note without reloading the page.
    static void HandleNotepad()
    {
        // A normal @"..." string (no $), so the JavaScript curly braces need no doubling
        string content = @"
            <h1>Shared Notepad</h1>
            <p>Everyone visiting this website shares this one note.</p>

            <div class=""box"">
                <h2>The note (from the server)</h2>
                <pre id=""noteView"" style=""background: white; border: 1px solid #ccc; padding: 10px; min-height: 60px;""></pre>
                <button onclick=""loadNote()"">Reload from server</button>
            </div>

            <div class=""box"">
                <h2>Edit the note</h2>
                <textarea id=""noteEditor"" rows=""6"" style=""width: 100%;""></textarea>
                <br /><br />
                <button onclick=""saveNote()"">Save to server</button>
                <span id=""status"" style=""color: green; margin-left: 10px;""></span>
            </div>

            <script>
                var noteView = document.getElementById('noteView');
                var noteEditor = document.getElementById('noteEditor');
                var statusText = document.getElementById('status');

                // 1. Ask the server for the note (GET), then show it
                function loadNote() {
                    fetch('/api/note')
                        .then(function (response) {
                            return response.text();   // read the reply as plain text
                        })
                        .then(function (text) {
                            noteView.textContent = text;  // textContent = safe, shown exactly as-is
                            noteEditor.value = text;
                        });
                }

                // 2. Send the textarea's text to the server (POST), then reload it
                function saveNote() {
                    fetch('/api/note', {
                        method: 'POST',
                        body: noteEditor.value          // the raw text IS the request body
                    })
                        .then(function (response) {
                            return response.text();
                        })
                        .then(function (reply) {
                            statusText.textContent = reply;  // e.g. ""Saved! (42 characters)""
                            loadNote();
                        });
                }

                // Load the note once when the page opens
                loadNote();
            </script>";

        RenderPage("Notepad", content);
    }

    // NEW IN DAY 13: One student object -> JSON, nicely indented so it is easy to read
    static void HandleSampleApi()
    {
        Student student = new Student
        {
            Name = "Alice",
            Age = 12,
            Score = 95.5,
            IsMember = true,
            JoinDate = new DateTime(2026, 10, 23, 14, 30, 0),
            Hobbies = new List<string> { "drawing", "football" },
            Nickname = null
        };

        string json = JsonConvert.SerializeObject(student, Formatting.Indented);

        Response.ContentType = "application/json";
        Response.Write(json);
    }

    // NEW IN DAY 13: The whole list -> JSON in ONE line. No loop, no commas, no escaping by hand!
    static void HandleEntriesApi()
    {
        string json;

        lock (guestbookLock)
        {
            json = JsonConvert.SerializeObject(guestbookEntries);
        }

        Response.ContentType = "application/json";
        Response.Write(json);
    }
    // A guestbook page that updates itself every 3 seconds
    static void HandleLiveGuestbook()
    {
        string content = @"
            <h1>Live Guestbook</h1>
            <p>This list updates by itself every 3 seconds. Sign the <a href=""/guestbook"" target=""_blank"">Guestbook</a> in another tab and watch!</p>
            <p id=""liveCount"" style=""color: gray;"">Loading...</p>
            <ul id=""liveList""></ul>

            <script>
                var list = document.getElementById('liveList');
                var countText = document.getElementById('liveCount');

                function loadEntries() {
                    fetch('/api/entries')
                        .then(function (response) {
                            return response.json();   // read the reply as JSON (not plain text)
                        })
                        .then(function (entries) {
                            // 'entries' is now a real JavaScript array of objects!
                            list.innerHTML = '';      // clear the old list (this is our own empty text, so it is safe)

                            for (var i = 0; i < entries.length; i++) {
                                var entry = entries[i];

                                var li = document.createElement('li');
                                li.style.marginBottom = '8px';

                                var nameEl = document.createElement('b');
                                nameEl.textContent = entry.name;          // textContent = safe

                                var timeEl = document.createElement('small');
                                timeEl.style.color = 'gray';
                                timeEl.textContent = ' (' + new Date(entry.time).toLocaleString() + ')';   // NEW IN DAY 13: format the date

                                var messageEl = document.createElement('div');
                                messageEl.textContent = entry.message;

                                li.appendChild(nameEl);
                                li.appendChild(timeEl);
                                li.appendChild(messageEl);
                                list.appendChild(li);
                            }

                            countText.textContent = entries.length + ' entries (last updated ' + new Date().toLocaleTimeString() + ')';
                        });
                }

                loadEntries();                      // load once now
                setInterval(loadEntries, 3000);     // and again every 3000 ms (3 seconds)
            </script>";

        RenderPage("Live Guestbook", content);
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

                lock (guestbookLock)
                {
                    guestbookEntries.Add(entry);
                    SaveEntry(entry);
                }

               // Remember the visitor's name for 30 days
                HttpCookie nameCookie = new HttpCookie("guest_name", HttpUtility.UrlEncode(entry.Name));
                nameCookie.Expires = DateTime.Now.AddDays(30);
                Response.Cookies.Add(nameCookie);
            }

            Response.Redirect("/guestbook");
            return;
        }

       // If we remember the visitor, greet them and fill in their name
        string rememberedName = GetCookieValue("guest_name");
        string welcomeText = "";
        string nameValue = "";
        if (!string.IsNullOrEmpty(rememberedName))
        {
            welcomeText = $"<p><b>Welcome back, {HttpUtility.HtmlEncode(rememberedName)}!</b></p>";
            nameValue = HttpUtility.HtmlEncode(rememberedName); // Also encode inside value=""...""
        }

        StringBuilder listHtml = new StringBuilder();
        int count;

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
            {welcomeText}
            <p>Leave a message for everyone visiting this site!</p>

            <form action=""/guestbook"" method=""post"" class=""box"" style=""max-width: 450px;"">
                <div>
                    <label>Your Name:</label><br />
                    <input type=""text"" name=""author"" value=""{nameValue}"" required style=""width: 100%;"" />
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
            <p>Try the <a href=""/cookies"">Cookie Playground</a>, then sign the <a href=""/guestbook"">Guestbook</a> twice. The second time, it remembers you!</p>";

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

### 1. The Sample Student

Visit `http://localhost:8080/api/sample`. You see the JSON from Step 2 — text, numbers, `true`, a date, a list, and `null` — all written by **one** line of C#.

### 2. The Guestbook JSON

1. Sign the **Guestbook** with a message that contains quotes, like `Say "hi"`.
2. Visit `http://localhost:8080/api/entries`. The keys are lowercase (`"name"`, `"message"`, `"time"`) thanks to `[JsonProperty]`, and the quotes are safely encoded as `\"`.

### 3. The Live Page Still Works

Visit `http://localhost:8080/live`. Names and messages are back, and the time is shown in your local format.

### 4. Experiment: See the Problem Yourself

1. Put `//` in front of the three `[JsonProperty(...)]` lines in `GuestbookEntry`.
2. Restart the server and look at `/api/entries`: the keys are now `"Name"`, `"Message"`, `"Time"`.
3. Open `/live`: the names and messages are empty again, and the time says **Invalid Date** — that's the problem from Step 5.
4. Remove the `//` again, and everything is back.

---

## Summary

| I want to... | Code |
| :--- | :--- |
| Add the library | `dotnet add package Newtonsoft.Json` |
| Use it | `using Newtonsoft.Json;` |
| Object or list → JSON (one line) | `JsonConvert.SerializeObject(obj)` |
| Object → JSON, easy for humans to read | `JsonConvert.SerializeObject(obj, Formatting.Indented)` |
| Choose the JSON key for a property | `[JsonProperty("name")]` above the property |

- By default, **the JSON key is the C# property name**.
- `[JsonProperty("...")]` **separates** the C# name from the JSON name, so each side can follow its own habits, and renaming in C# never breaks the JSON.
- A library is not a must — but it saves code and avoids mistakes.

> **Going the other way:** Newtonsoft can also turn JSON text **back into** a C# object. This is called **deserializing**:
> ```csharp
> GuestbookEntry entry = JsonConvert.DeserializeObject<GuestbookEntry>(jsonText);
> ```
> Combine it with Day 11's `Request.InputStream`, and JavaScript can send JSON to your server too!

In **Day 14**, there is no new lesson. Instead, you'll build a **complete To-Do List app by yourself**, using everything from Day 1 to Day 13!
