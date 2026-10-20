using System;
using cshttp;
using HearthFileManager.engine;

namespace HearthFileManager.RH
{
    public class LogoutPage
    {
        public static void HandleRequest()
        {
            AppSession.Logout();
            Response.Redirect("/login", false);
            ApiHelper.EndResponse();
        }
    }
}
