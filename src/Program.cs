using System.Text;
using OtpNet;

var app = WebApplication.CreateBuilder(args).Build();

app.MapGet("/code", () =>
{
    var secret = Environment.GetEnvironmentVariable("TOTP_SECRET")
        ?? throw new InvalidOperationException("TOTP_SECRET not configured");

    return ComputeCode(secret);
});

app.MapGet("/code/{secret}", (string secret) => ComputeCode(secret));

app.MapGet("/otpauth", ([Microsoft.AspNetCore.Mvc.FromQuery] string uri) =>
{
    try
    {
        var parsed = new Uri(uri);
        var query = parsed.Query.TrimStart('?')
            .Split('&')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        if (!query.TryGetValue("secret", out var secret))
            return Results.BadRequest(new { error = "Missing 'secret' in otpauth URI." });

        var period  = query.TryGetValue("period",    out var p) && int.TryParse(p, out var pi) ? pi : 30;
        var digits  = query.TryGetValue("digits",    out var d) && int.TryParse(d, out var di) ? di : 6;
        var algo    = query.TryGetValue("algorithm", out var a) ? a.ToUpper() : "SHA1";

        var hashMode = algo switch
        {
            "SHA256" => OtpHashMode.Sha256,
            "SHA512" => OtpHashMode.Sha512,
            _        => OtpHashMode.Sha1
        };

        var secretBytes = Base32Encoding.ToBytes(secret.ToUpper());
        var totp = new Totp(secretBytes, period, hashMode, digits);
        return Results.Ok(new { code = totp.ComputeTotp(), generated_at = DateTimeOffset.UtcNow });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UriFormatException)
    {
        return Results.BadRequest(new { error = "Invalid otpauth URI format." });
    }
});

app.MapGet("/base32/{**data}", (string data) =>
{
    try
    {
        var normalized = data.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);

        var bytes = Convert.FromBase64String(normalized);
        var accounts = ParseMigrationPayload(bytes);

        if (accounts.Count == 0)
            return Results.BadRequest(new { error = "No TOTP accounts found in migration data." });

        var results = accounts.Select(a =>
        {
            try
            {
                return new
                {
                    issuer = a.Issuer,
                    name = a.Name,
                    code = new Totp(a.Secret).ComputeTotp(),
                    generated_at = DateTimeOffset.UtcNow
                };
            }
            catch
            {
                return new { issuer = a.Issuer, name = a.Name, code = "error", generated_at = DateTimeOffset.UtcNow };
            }
        });

        return Results.Ok(results);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "Invalid Base64 data." });
    }
});

app.Run();

static IResult ComputeCode(string secret)
{
    try
    {
        var secretBytes = Base32Encoding.ToBytes(secret);
        var totp = new Totp(secretBytes);
        return Results.Ok(new { code = totp.ComputeTotp(), generated_at = DateTimeOffset.UtcNow });
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new { error = "Invalid Base32 secret. Use only characters A-Z and 2-7." });
    }
}

static List<(byte[] Secret, string Name, string Issuer)> ParseMigrationPayload(byte[] data)
{
    var accounts = new List<(byte[], string, string)>();
    int pos = 0;

    while (pos < data.Length)
    {
        int tag = ReadVarint(data, ref pos);
        int wireType = tag & 0x7;
        int fieldNumber = tag >> 3;

        if (wireType == 2)
        {
            int length = ReadVarint(data, ref pos);
            var value = data[pos..(pos + length)];
            pos += length;

            if (fieldNumber == 1)
                accounts.Add(ParseOtpParameters(value));
        }
        else if (wireType == 0)
        {
            ReadVarint(data, ref pos);
        }
        else break;
    }

    return accounts;
}

static (byte[] Secret, string Name, string Issuer) ParseOtpParameters(byte[] data)
{
    byte[] secret = [];
    string name = "", issuer = "";
    int pos = 0;

    while (pos < data.Length)
    {
        int tag = ReadVarint(data, ref pos);
        int wireType = tag & 0x7;
        int fieldNumber = tag >> 3;

        if (wireType == 2)
        {
            int length = ReadVarint(data, ref pos);
            var value = data[pos..(pos + length)];
            pos += length;

            if (fieldNumber == 1) secret = value;
            else if (fieldNumber == 2) name = Encoding.UTF8.GetString(value);
            else if (fieldNumber == 3) issuer = Encoding.UTF8.GetString(value);
        }
        else if (wireType == 0)
        {
            ReadVarint(data, ref pos);
        }
        else break;
    }

    return (secret, name, issuer);
}

static int ReadVarint(byte[] data, ref int pos)
{
    int result = 0, shift = 0;
    byte b;
    do { b = data[pos++]; result |= (b & 0x7F) << shift; shift += 7; }
    while ((b & 0x80) != 0);
    return result;
}
