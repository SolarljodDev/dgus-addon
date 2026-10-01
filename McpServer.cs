using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DgusPlus
{
    // MCP-сервер (Streamable HTTP, ответы простым JSON) внутри процесса DGUS, слушает только 127.0.0.1.
    // Подключение: claude mcp add --transport http dgus http://127.0.0.1:8765/mcp. Инструменты — McpTools.cs, выполняются в UI-потоке DGUS.
    static class McpServer
    {
        public const string Path = "/mcp";
        static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        static TcpListener listener;
        public static string Status = "выключен";
        public static int Port;

        public static readonly JavaScriptSerializer Json = CreateJson();

        static JavaScriptSerializer CreateJson()
        {
            JavaScriptSerializer j = new JavaScriptSerializer();
            j.MaxJsonLength = 64 * 1024 * 1024;
            j.RecursionLimit = 64;
            return j;
        }

        public static void Start(int port)
        {
            try
            {
                Port = port;
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                Thread t = new Thread(AcceptLoop);
                t.IsBackground = true;
                t.Name = "DGUS+ MCP";
                t.Start();
                Status = "http://127.0.0.1:" + port + Path;
            }
            catch (Exception ex)
            {
                Status = "порт " + port + " занят";
                Plus.Log(ex);
            }
        }

        static void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(delegate { Serve(c); });
            }
        }


        static void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    NetworkStream s = client.GetStream();
                    string method, path;
                    Dictionary<string, string> headers;
                    byte[] body;
                    if (!ReadRequest(s, out method, out path, out headers, out body)) return;

                    // Защита от DNS-rebinding и чужих сайтов: браузерные запросы — только
                    // со страниц самого DGUS+ (точное совпадение адреса и порта).
                    string origin;
                    if (headers.TryGetValue("origin", out origin) && origin.Length > 0 &&
                        origin != "http://127.0.0.1:" + Port && origin != "http://localhost:" + Port)
                    { Write(s, 403, "text/plain", "forbidden origin"); return; }
                    string host;
                    if (headers.TryGetValue("host", out host) &&
                        host != "127.0.0.1:" + Port && host != "localhost:" + Port)
                    { Write(s, 403, "text/plain", "forbidden host"); return; }

                    string query = "";
                    int q = path.IndexOf('?');
                    if (q >= 0) { query = path.Substring(q + 1); path = path.Substring(0, q); }

                    if (path != Path)
                    {
                        WebApi.Response wr = WebApi.Handle(method, path, query, body);
                        WriteBytes(s, wr.Code, wr.Type, wr.Body);
                        return;
                    }
                    if (method != "POST") { Write(s, 405, "text/plain", "POST only"); return; }

                    string reply = HandleRpc(Encoding.UTF8.GetString(body));
                    if (reply == null) Write(s, 202, null, null);
                    else Write(s, 200, "application/json", reply);
                }
                catch (Exception ex) { Plus.Log(ex); }
            }
        }

        static bool ReadRequest(Stream s, out string method, out string path,
                                out Dictionary<string, string> headers, out byte[] body)
        {
            method = path = null; headers = null; body = null;
            MemoryStream head = new MemoryStream();
            int state = 0;
            while (state < 4)
            {
                int b = s.ReadByte();
                if (b < 0) return false;
                head.WriteByte((byte)b);
                if (head.Length > 64 * 1024) return false;
                state = (b == '\r' && (state == 0 || state == 2)) ? state + 1 :
                        (b == '\n' && (state == 1 || state == 3)) ? state + 1 : 0;
            }
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return false;
            method = first[0].ToUpperInvariant();
            path = first[1];
            headers = new Dictionary<string, string>();
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0) headers[lines[i].Substring(0, c).Trim().ToLowerInvariant()] = lines[i].Substring(c + 1).Trim();
            }
            string cl;
            int len = headers.TryGetValue("content-length", out cl) ? int.Parse(cl) : 0;
            body = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = s.Read(body, read, len - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        static void Write(Stream s, int code, string type, string text)
        {
            WriteBytes(s, code, type == null ? null : type + "; charset=utf-8",
                       text == null ? new byte[0] : Encoding.UTF8.GetBytes(text));
        }

        static void WriteBytes(Stream s, int code, string type, byte[] data)
        {
            if (data == null) data = new byte[0];
            string reason = code == 200 ? "OK" : code == 202 ? "Accepted" : code == 400 ? "Bad Request" :
                            code == 403 ? "Forbidden" : code == 404 ? "Not Found" :
                            code == 405 ? "Method Not Allowed" : code == 409 ? "Conflict" : "Error";
            StringBuilder h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            if (type != null) h.Append("Content-Type: ").Append(type).Append("\r\n");
            h.Append("Cache-Control: no-store\r\n");
            if (code == 405) h.Append("Allow: POST\r\n");
            h.Append("Content-Length: ").Append(data.Length).Append("\r\nConnection: close\r\n\r\n");
            byte[] hb = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(hb, 0, hb.Length);
            s.Write(data, 0, data.Length);
            s.Flush();
        }


        static string HandleRpc(string text)
        {
            Dictionary<string, object> req;
            try { req = Json.Deserialize<Dictionary<string, object>>(text); }
            catch { return Error(null, -32700, "Parse error"); }
            if (req == null) return Error(null, -32600, "Invalid Request");

            object id;
            bool isRequest = req.TryGetValue("id", out id);
            string method = req.ContainsKey("method") ? req["method"] as string : null;
            Dictionary<string, object> prms = req.ContainsKey("params") ? req["params"] as Dictionary<string, object> : null;
            if (prms == null) prms = new Dictionary<string, object>();

            if (!isRequest) return null;   // уведомления (notifications/initialized и т.п.) и ответы клиента
            if (method == null) return Error(id, -32600, "Invalid Request");

            try
            {
                switch (method)
                {
                    case "initialize":
                        return Result(id, Initialize(prms));
                    case "ping":
                        return Result(id, new Dictionary<string, object>());
                    case "tools/list":
                        Dictionary<string, object> tl = new Dictionary<string, object>();
                        tl["tools"] = McpTools.Definitions();
                        return Result(id, tl);
                    case "tools/call":
                        return Result(id, McpTools.Call(prms["name"] as string,
                            prms.ContainsKey("arguments") ? prms["arguments"] as Dictionary<string, object> : null));
                    default:
                        return Error(id, -32601, "Method not found: " + method);
                }
            }
            catch (Exception ex)
            {
                Plus.Log(ex);
                return Error(id, -32603, ex.Message);
            }
        }

        static Dictionary<string, object> Initialize(Dictionary<string, object> prms)
        {
            string want = prms.ContainsKey("protocolVersion") ? prms["protocolVersion"] as string : null;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["protocolVersion"] = Array.IndexOf(Versions, want) >= 0 ? want : Versions[0];
            Dictionary<string, object> caps = new Dictionary<string, object>();
            caps["tools"] = new Dictionary<string, object>();
            r["capabilities"] = caps;
            Dictionary<string, object> info = new Dictionary<string, object>();
            info["name"] = "dgus-plus";
            info["version"] = "1.0";
            r["serverInfo"] = info;
            r["instructions"] = McpTools.Instructions;
            return r;
        }

        static string Result(object id, object result)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["jsonrpc"] = "2.0";
            r["id"] = id;
            r["result"] = result;
            return Json.Serialize(r);
        }

        static string Error(object id, int code, string message)
        {
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["code"] = code;
            e["message"] = message;
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["jsonrpc"] = "2.0";
            r["id"] = id;
            r["error"] = e;
            return Json.Serialize(r);
        }
    }
}
