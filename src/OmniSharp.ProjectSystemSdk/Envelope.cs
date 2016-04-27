using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmniSharp.ProjectSystemSdk
{
    public class Envelope
    {
        public static readonly string Head = "MSG";

        public static string Serialize(Guid session, string kind, object args)
        {
            return $"{Head}|{session.ToString("N")}|{kind}|{JsonConvert.SerializeObject(args)}";
        }

        public static Envelope Deserialize(string rawContent)
        {
            try
            {
                Guid sessionId;
                var index = 0;

                var next = rawContent.IndexOf('|', index);
                if (next == -1 || string.Compare(Head, 0, rawContent, index, next - index) != 0)
                {
                    // head is not MSG
                    return null;
                }

                index = next + 1;
                next = rawContent.IndexOf('|', index);
                if (next == -1 || !Guid.TryParse(rawContent.Substring(index, next - index), out sessionId))
                {
                    // session id is not guid
                    return null;
                }

                index = next + 1;
                next = rawContent.IndexOf('|', index);
                if (next == -1)
                {
                    // kind segment is missing
                }

                var kind = rawContent.Substring(index, next - index);

                var data = rawContent.Substring(next + 1);
                if (!string.IsNullOrEmpty(data))
                {
                    var jobject = JsonConvert.DeserializeObject<JObject>(data);
                    return new Envelope(sessionId, kind, jobject);
                }
                else
                {
                    return new Envelope(sessionId, kind);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected ex: {ex.Message} for message [{rawContent}]");
                throw;
            }
        }

        private Envelope(Guid session, string kind) : this(session, kind, data: null)
        {
        }

        private Envelope(Guid session, string kind, JObject data)
        {
            Session = session;
            Kind = kind;
            Data = data;
        }

        public Guid Session { get; }

        public string Kind { get; }

        public JObject Data { get; }
    }
}