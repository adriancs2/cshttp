using System;
using cshttp;
using Newtonsoft.Json;

namespace CommonWebAppDemo.engine
{
    public static class ApiHelper
    {
        public static void WriteJson(string json)
        {
            Response.ContentType = "application/json; charset=utf-8";
            Response.Write(json);
        }

        public static void WriteSuccess(string message = "Success")
        {
            WriteJson(JsonConvert.SerializeObject(new { success = true, message = message }));
        }

        public static void WriteSuccessData(string message, object data)
        {
            WriteJson(JsonConvert.SerializeObject(new { success = true, message = message, data = data }));
        }

        public static void WriteError(string message, int statusCode = 400)
        {
            Response.StatusCode = statusCode;
            WriteJson(JsonConvert.SerializeObject(new { success = false, message = message }));
        }
    }
}
