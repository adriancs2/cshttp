using System;
using cshttp;
using HearthFileManager.engine;
using HearthFileManager.engine.Ai;
using Newtonsoft.Json.Linq;

namespace HearthFileManager.RH
{
    public class AiPageApi
    {
        public static void HandleRequest()
        {
            string action = (Request.Params["action"] + "").ToLower().Trim();
            try
            {
                switch (action)
                {
                    case "start": Guard.Mutating(Perm.Ai); Start(); break;
                    case "poll": Guard.Require(Perm.Ai); Poll(); break;
                    case "stop": Guard.Mutating(Perm.Ai); Stop(); break;
                    case "undo": Guard.Mutating(Perm.Ai); Undo(); break;
                    default: ApiHelper.WriteError($"Unknown action: {action}", 400); break;
                }
            }
            catch (GuardException gx) { ApiHelper.WriteError(gx.Message, gx.StatusCode); }
            catch (FsException fx) { ApiHelper.WriteError(fx.Message, 400); }
            catch (Exception ex) { ApiHelper.WriteError("An error occurred: " + ex.Message, 500); }
            ApiHelper.EndResponse();
        }

        /// <summary>
        /// Form: history = Gemini "contents" JSON array kept in the browser (IndexedDB),
        ///       message = text, images = JSON array of { MimeType, Data(base64) }.
        /// </summary>
        static void Start()
        {
            obConfig cfg = AppConfig.Get();
            if (string.IsNullOrWhiteSpace(cfg.GeminiApiKey)) throw new FsException("No Gemini API key yet. Add one on the Settings page.");

            var form = Request.Unvalidated.Form;
            string message = (form["message"] ?? "").Trim();
            JArray history = string.IsNullOrEmpty(form["history"]) ? new JArray() : JArray.Parse(form["history"]);
            JArray images = string.IsNullOrEmpty(form["images"]) ? new JArray() : JArray.Parse(form["images"]);
            if (message == "" && images.Count == 0) throw new FsException("Please type a message.");
            if (images.Count > 4) throw new FsException("Attach up to 4 pictures at a time.");

            var parts = new JArray();
            foreach (JObject img in images)
            {
                string mime = (string)img["MimeType"] ?? "";
                if (!mime.StartsWith("image/")) continue;
                parts.Add(new JObject { ["inlineData"] = new JObject { ["mimeType"] = mime, ["data"] = (string)img["Data"] } });
            }
            if (message != "") parts.Add(new JObject { ["text"] = message });

            var agent = new GeminiAgent(FsService.ForUser(AppSession.LoginUser), cfg.GeminiApiKey, cfg.GeminiModel, cfg.GeminiRpm, cfg.SitePreviewUrl);
            agent.FallbackModels = cfg.GeminiFallbackModels;
            AiTask task = AiTaskManager.Start(AppSession.LoginUser.Username, agent, history, parts);
            ApiHelper.WriteSuccess("Started", new { TaskId = task.Id });
        }

        static void Poll()
        {
            AiTask task = AiTaskManager.Get(Request.Params["taskId"]);
            if (task == null) throw new FsException("This request is no longer running (the server may have restarted). Please send your message again.");
            int since;
            int.TryParse(Request.Params["since"], out since);

            var events = task.EventsSince(since);
            object result = null;
            if (task.Done && task.Result != null)
            {
                obAiTurnResult r = task.Result;
                result = new
                {
                    r.FinalText,
                    r.Error,
                    r.Changed,
                    r.UndoId,
                    r.Requests,
                    r.Model,
                    Contents = r.Contents
                };
            }
            ApiHelper.WriteJson(new { success = true, message = "Success", data = new { task.Done, Next = since + events.Count, Events = events, Result = result } });
        }

        static void Stop()
        {
            AiTask task = AiTaskManager.Get(Request.Form["taskId"]);
            if (task != null) task.Cancel.Cancel();
            ApiHelper.WriteSuccess("Stopping…");
        }

        static void Undo()
        {
            var restored = AiUndo.Undo(FsService.ForUser(AppSession.LoginUser), Request.Form["undoId"]);
            ApiHelper.WriteSuccess($"Undone. {restored.Count} item(s) restored.", restored);
        }
    }
}
