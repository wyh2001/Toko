namespace Toko.Filters
{
    public class CachedResponse(int statusCode, string? contentType, byte[] body)
    {
        public int StatusCode { get; } = statusCode;
        public string? ContentType { get; } = contentType;
        public byte[] Body { get; } = body;
    }

}
