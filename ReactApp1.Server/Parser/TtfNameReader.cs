namespace PatternTracker.Server.Parser;

/// <summary>
/// Читает PostScript-имя (nameID 6) из таблицы name шрифта.
/// Нужно, чтобы сопоставить файл, извлеченный mutool (font-0022.ttf),
/// с именем шрифта, которое видит PdfPig (AAAAAE+PTSans-Bold).
/// </summary>
public static class TtfNameReader
{
    public static string? TryGetPostScriptName(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 12)
                return null;

            int numTables = ReadU16(data, 4);
            int dirEnd = 12 + numTables * 16;
            if (dirEnd > data.Length)
                return null;

            int nameOffset = -1;
            for (int i = 0; i < numTables; i++)
            {
                int entry = 12 + i * 16;
                if (data[entry] == 'n' && data[entry + 1] == 'a' && data[entry + 2] == 'm' && data[entry + 3] == 'e')
                {
                    nameOffset = (int)ReadU32(data, entry + 8);
                    break;
                }
            }
            if (nameOffset < 0 || nameOffset + 6 > data.Length)
                return null;

            int count = ReadU16(data, nameOffset + 2);
            int storageOffset = nameOffset + ReadU16(data, nameOffset + 4);

            string? fallback = null;
            for (int i = 0; i < count; i++)
            {
                int rec = nameOffset + 6 + i * 12;
                if (rec + 12 > data.Length)
                    break;

                int platform = ReadU16(data, rec);
                int encoding = ReadU16(data, rec + 2);
                int nameId = ReadU16(data, rec + 6);
                int length = ReadU16(data, rec + 8);
                int offset = ReadU16(data, rec + 10);
                if (nameId != 6)
                    continue;

                int strStart = storageOffset + offset;
                if (strStart < 0 || strStart + length > data.Length)
                    continue;

                // platform 3 = Windows (UTF-16BE), platform 1 = Mac (MacRoman, для ASCII совпадает с latin1)
                string value = platform == 1 && encoding == 0
                    ? System.Text.Encoding.Latin1.GetString(data, strStart, length)
                    : System.Text.Encoding.BigEndianUnicode.GetString(data, strStart, length);

                if (platform == 3)
                    return value;
                fallback ??= value;
            }

            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadU16(byte[] data, int offset) => (data[offset] << 8) | data[offset + 1];

    private static uint ReadU32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
