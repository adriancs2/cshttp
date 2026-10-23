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
            <p>The page that you are looking is not existed</p>
        </body>
    </html>
				";
				break;
		}
        
        Response.Write(html);
    }
}
