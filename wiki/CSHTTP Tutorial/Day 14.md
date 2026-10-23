# Day 14: Mini Project — Build a To-Do List App

Congratulations! You have learned a lot in 13 days:

| Day | You learned |
| :--- | :--- |
| 0–2 | Running a server, routing with `switch`, a shared page template |
| 3–4 | Reading user input with GET (`QueryString`) and POST (`Form`) |
| 5 | Storing data in a server-side `List` and the Post-Redirect-Get pattern |
| 6 | Keeping your site safe with `HttpUtility.HtmlEncode` |
| 7 | Designing your own `class` |
| 8 | Saving to a file, and protecting shared data with `lock` |
| 9 | Content types: CSS, pictures, plain text |
| 10 | Cookies |
| 11–12 | `fetch`, plain text, and JSON |
| 13 | The Newtonsoft.Json library and `[JsonProperty]` |

Today there is **no new lesson**. Instead, you will build a complete app **by yourself**, starting from an empty `Program.cs`: a **To-Do List**.

Try to build it on your own first. Look back at earlier days when you get stuck. A complete **reference solution** is at the bottom — but only peek when you have really tried!

---

## What Your App Must Do

1. Show a list of tasks at `/`.
2. Have a form to **add** a new task.
3. Each task has a **Done / Undo** button that ticks it off (show done tasks ~~crossed out~~).
4. Each task has a **Delete** button.
5. Tasks are **saved to a file**, so they survive a server restart.
6. It must be **safe**: typing `<script>` as a task must not run any code.
7. It must be **correct with many visitors**: protect the shared list with `lock`.
8. It should look nice, using a `/style.css` route.

---

## Step-by-Step Plan

Build it in small steps. **Run and test after every step!**

### Step 1: A class for one task

Create a class `TodoItem` with three properties:

- `Id` (`int`) — a unique number, so the buttons know *which* task to change.
- `Text` (`string`) — what needs to be done.
- `Done` (`bool`) — `true` if it is finished.

*Hint: Day 7.*

### Step 2: Show the list

Create a `static List<TodoItem>` with two or three tasks already in it. Build the HTML for the list with a `foreach` loop and a `StringBuilder`, inside a `RenderPage` template.

*Hint: Day 2 and Day 5. Don't forget `HtmlEncode` (Day 6)!*

### Step 3: Add a task

Add a form with `method="post"` and `action="/add"`. In the `/add` route, read the text from `Request.Form`, create a new `TodoItem`, add it to the list, then `Response.Redirect("/")`.

*Hint: Day 4 and Day 5. To give each task a new `Id`, keep a `static int nextId` and increase it by one every time.*

### Step 4: Done and Delete buttons

Each task needs two small forms, each with a **hidden** field that carries the task's `Id`:

```html
<form action="/toggle" method="post">
    <input type="hidden" name="id" value="3" />
    <button type="submit">Done</button>
</form>
```

In the `/toggle` route, read the id with `int.Parse(Request.Form["id"])`, find the task with that `Id`, and flip `Done` (`item.Done = !item.Done;`). Do the same for `/delete`, but remove the task from the list.

*Hint: To remove an item while looping is not allowed! Find it first, then remove it after the loop. Or use `todoItems.RemoveAll(t => t.Id == id);`.*

### Step 5: Save to a file

Save the tasks to `todo.txt`, one per line: `Id|Done|Text`

```text
1|False|Buy milk
2|True|Learn C#
```

Because tasks can change or be deleted, it is easiest to **rewrite the whole file** after every change, with `File.WriteAllLines(path, lines)`. Load the file in `Main` before the server starts.

*Hint: Day 8, Part 1. `bool.Parse("True")` turns text back into a `bool`.*

### Step 6: Add `lock`

Put **every** use of the list (show, add, toggle, delete, save) inside `lock (todoLock) { ... }`.

*Hint: Day 8, Part 2. Keep `Response.Redirect` and `RenderPage` **outside** the lock.*

### Step 7: Make it pretty

Add a `/style.css` route with `Response.ContentType = "text/css"` and link it from your template.

*Hint: Day 9.*

---

## Test Checklist

- [ ] Add three tasks. They appear in the list.
- [ ] Click **Done** on one. It is crossed out. Click **Undo**. It is back to normal.
- [ ] Click **Delete**. It disappears.
- [ ] Stop the server and start it again. Your tasks are still there.
- [ ] Open `todo.txt` in Notepad. You can read your tasks.
- [ ] Add a task called `<b>bold</b>`. It is shown as text, not bold.
- [ ] Refresh the page after adding a task. The browser does **not** ask "Resend form data?" (that is the Post-Redirect-Get pattern working!).
- [ ] Open the app in two browser windows. Changes in one appear in the other after a refresh.

---

## Bonus Challenges

Finished? Try these:

1. **Counter:** Show "2 of 5 tasks done" above the list.
2. **Clear finished:** Add a button that deletes every task where `Done == true`.
3. **Cookie:** Ask for the visitor's name once and show "Alice's To-Do List" using a cookie (Day 10).
4. **JSON API:** Add `/api/todos` that returns all tasks as JSON using `JsonConvert.SerializeObject` (Day 13), then build a `/live` page that refreshes the list every 3 seconds.
5. **Empty tasks:** What happens if someone submits only spaces? Make sure your app ignores them (`string.IsNullOrWhiteSpace`).

---

## Reference Solution

**Try it yourself first!** When you are done (or really stuck), compare with this version.

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using cshttp;

class TodoItem
{
    public int Id { get; set; }
    public string Text { get; set; }
    public bool Done { get; set; }
}

static class Program
{
    static List<TodoItem> todoItems = new List<TodoItem>();
    static int nextId = 1;
    static string dataFile = WebApp.MapPath("~/todo.txt");
    static readonly object todoLock = new object();

    static void Main()
    {
        LoadTodos();
        Console.WriteLine("To-do file: " + dataFile);

        WebApp.Run(8080, OnRequest);
    }

    // ---------- Saving and loading ----------

    static void LoadTodos()
    {
        if (!File.Exists(dataFile))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(dataFile))
        {
            // Id | Done | Text
            string[] parts = line.Split(new char[] { '|' }, 3);
            if (parts.Length != 3)
            {
                continue;
            }

            TodoItem item = new TodoItem
            {
                Id = int.Parse(parts[0]),
                Done = bool.Parse(parts[1]),
                Text = parts[2]
            };
            todoItems.Add(item);

            // Make sure new tasks never reuse an old Id
            if (item.Id >= nextId)
            {
                nextId = item.Id + 1;
            }
        }
    }

    // Always called INSIDE lock (todoLock)
    static void SaveTodos()
    {
        List<string> lines = new List<string>();
        foreach (TodoItem item in todoItems)
        {
            lines.Add(item.Id + "|" + item.Done + "|" + item.Text);
        }
        File.WriteAllLines(dataFile, lines);
    }

    // ---------- Routing ----------

    static void OnRequest()
    {
        string path = Request.Path.ToLower();

        switch (path)
        {
            case "/":
                HandleList();
                return;
            case "/add":
                HandleAdd();
                return;
            case "/toggle":
                HandleToggle();
                return;
            case "/delete":
                HandleDelete();
                return;
            case "/style.css":
                HandleStyle();
                return;
            default:
                Response.StatusCode = 404;
                RenderPage("Not Found", "<h1>404 Not Found</h1><p><a href=\"/\">Back to the list</a></p>");
                return;
        }
    }

    // ---------- Pages ----------

    static void RenderPage(string title, string bodyContent)
    {
        string html = $@"<!DOCTYPE html>
<html>
    <head>
        <title>{title}</title>
        <link rel=""stylesheet"" href=""/style.css"" />
    </head>
    <body>
        {bodyContent}
        <footer>Server Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</footer>
    </body>
</html>";

        Response.Write(html);
        Response.End();
    }

    static void HandleList()
    {
        StringBuilder listHtml = new StringBuilder();
        int total;
        int doneCount = 0;

        lock (todoLock)
        {
            total = todoItems.Count;

            foreach (TodoItem item in todoItems)
            {
                string safeText = HttpUtility.HtmlEncode(item.Text);
                string cssClass = item.Done ? "done" : "";
                string toggleLabel = item.Done ? "Undo" : "Done";

                if (item.Done)
                {
                    doneCount++;
                }

                listHtml.Append($@"
                <li>
                    <span class=""{cssClass}"">{safeText}</span>
                    <form action=""/toggle"" method=""post"">
                        <input type=""hidden"" name=""id"" value=""{item.Id}"" />
                        <button type=""submit"">{toggleLabel}</button>
                    </form>
                    <form action=""/delete"" method=""post"">
                        <input type=""hidden"" name=""id"" value=""{item.Id}"" />
                        <button type=""submit"" class=""delete"">Delete</button>
                    </form>
                </li>");
            }
        }

        if (total == 0)
        {
            listHtml.Append("<li class=\"empty\">Nothing to do. Add your first task above!</li>");
        }

        string content = $@"
            <h1>My To-Do List</h1>

            <form action=""/add"" method=""post"" class=""add-form"">
                <input type=""text"" name=""text"" placeholder=""What do you need to do?"" required autofocus />
                <button type=""submit"">Add</button>
            </form>

            <p class=""count"">{doneCount} of {total} tasks done</p>
            <ul>{listHtml.ToString()}</ul>";

        RenderPage("My To-Do List", content);
    }

    // ---------- Actions (POST, then redirect back to the list) ----------

    static void HandleAdd()
    {
        if (Request.HttpMethod == "POST")
        {
            string text = Request.Form["text"];

            if (!string.IsNullOrWhiteSpace(text))
            {
                // Keep it on one line, so it fits in one line of our file
                text = text.Replace("\r", " ").Replace("\n", " ").Trim();

                lock (todoLock)
                {
                    TodoItem item = new TodoItem { Id = nextId, Text = text, Done = false };
                    nextId++;
                    todoItems.Add(item);
                    SaveTodos();
                }
            }
        }

        Response.Redirect("/");
    }

    static void HandleToggle()
    {
        int id;
        if (Request.HttpMethod == "POST" && int.TryParse(Request.Form["id"], out id))
        {
            lock (todoLock)
            {
                foreach (TodoItem item in todoItems)
                {
                    if (item.Id == id)
                    {
                        item.Done = !item.Done;
                        break;
                    }
                }
                SaveTodos();
            }
        }

        Response.Redirect("/");
    }

    static void HandleDelete()
    {
        int id;
        if (Request.HttpMethod == "POST" && int.TryParse(Request.Form["id"], out id))
        {
            lock (todoLock)
            {
                todoItems.RemoveAll(t => t.Id == id);
                SaveTodos();
            }
        }

        Response.Redirect("/");
    }

    // ---------- Stylesheet ----------

    static void HandleStyle()
    {
        Response.ContentType = "text/css";
        Response.Write(@"
body {
    font-family: Arial, sans-serif;
    max-width: 520px;
    margin: 40px auto;
    padding: 0 16px;
    background: #f4f6fb;
    color: #333;
}
h1 {
    color: #2c5282;
}
.add-form {
    display: flex;
    gap: 8px;
}
.add-form input {
    flex: 1;
    padding: 8px;
}
button {
    padding: 6px 12px;
    cursor: pointer;
}
ul {
    list-style: none;
    padding: 0;
}
li {
    display: flex;
    align-items: center;
    gap: 8px;
    background: white;
    border: 1px solid #ddd;
    border-radius: 6px;
    padding: 10px;
    margin-bottom: 8px;
}
li span {
    flex: 1;
}
li form {
    margin: 0;
}
.done {
    text-decoration: line-through;
    color: #999;
}
.delete {
    color: #c53030;
}
.empty {
    color: #999;
    justify-content: center;
}
.count, footer {
    color: gray;
    font-size: small;
}");
    }
}
```

---

## Things to Notice in the Solution

- **`int.TryParse` instead of `int.Parse`:** If someone sends a broken id (like `abc`), `int.Parse` would crash with an error. `int.TryParse` simply returns `false`, and we ignore the request. Never trust input — even hidden fields can be changed by the visitor!
- **`SaveTodos()` is called inside the `lock`:** Saving reads the whole list, so nobody may change it while we write the file.
- **Every action ends with `Response.Redirect("/")`:** That's the Post-Redirect-Get pattern from Day 5. Refreshing the page never repeats an action.
- **`HtmlEncode` on `item.Text`, but not on `item.Id`:** The Id is an `int` that *we* created, so it can only ever be digits.

---

## What's Next?

You've built a complete, safe, saved, multi-user web app in C# — from an empty file! 🎉

From here, you can explore:

- **Databases** (like SQLite or MySQL) instead of text files, for bigger apps.
- **User accounts and logins**, with passwords stored safely (hashed).
- **Serving real files** like photos and downloads.
- **A JSON library**, so you don't have to build JSON by hand.

Every one of them uses the same building blocks you already know: `Request`, `Response`, `switch`, classes, `lock`, and encoding user input. Well done!
