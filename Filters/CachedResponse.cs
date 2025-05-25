namespace Toko.Filters
{
    public class CachedResponse
    {
        public int StatusCode { get; }
        public string? ContentType { get; }
        public byte[] Body { get; }

        public CachedResponse(int statusCode, string? contentType, byte[] body)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Body = body;
        }
    }

}
