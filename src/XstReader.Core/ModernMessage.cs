using System.IO;
using System.Text;

namespace XstReader
{
    // Cross-platform additions to the original Dijji reader.
    public partial class Message
    {
        public uint NodeId => Nid.dwValue;
        public Message CreateContentReader() => new Message { Folder = Folder, Nid = Nid, Flags = Flags, NativeBody = NativeBody };
        public bool IsUnread => ((int)Flags & 1) == 0;
        public string GetRtfBody()
        {
            if (RtfCompressed == null) return null;
            using (var stream = new RtfDecompressor().Decompress(RtfCompressed, true))
            using (var reader = new StreamReader(stream, Encoding.GetEncoding(1252)))
            {
                stream.Position = 0;
                return reader.ReadToEnd();
            }
        }
    }
}

