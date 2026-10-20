using cshttp;

static class Program
{
	static void Main()
	{
		// style 1: single port
		//WebApp.Port = 8080;
		//WebApp.Run(OnRequest);
		
		// style 2: multiple port
		//WebApp.Ports = new int[] { 8080, 9090 };
		//WebApp.Run(OnRequest);
		
		// style 3: single port
		WebApp.Run(8080, OnRequest);
		
		// style 4: multiple port
		//WebApp.Run(8080, 9090, OnRequest);
	}

	static void OnRequest()
	{
		string path = Request.Path.ToLower();
		
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
