using System.Globalization;
using System.Net;

namespace Feeder.Matrix;

/// <summary>The install page a phone opens from the Matrix Device QR: the newest .apk of the device folder.</summary>
public static class DevicePage
{
    public const string Path = "/device";

    public static IResult Page(MatrixOptions options, string token)
    {
        if (!MatrixAuthentication.FixedTimeEquals(token, options.LanToken))
            return Results.Text("This link is out of date: scan the QR in Unity (Matrix Device) again.", statusCode: StatusCodes.Status401Unauthorized);
        var apk = Newest(options);
        var body = apk is null
            ? "<p>No build yet. In Unity: <b>Tools &gt; Feeder &gt; Matrix Bridge &gt; Device &gt; Build Android</b>, then reload this page.</p>"
            : $"""
               <p class="name">{WebUtility.HtmlEncode(apk.Name)}</p>
               <p>{apk.Length / 1048576.0:0.0} MB, built {apk.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}</p>
               <a class="button" href="{Path}/app.apk?t={Uri.EscapeDataString(token)}">Download APK</a>
               <ol>
                 <li>Open the downloaded file and tap <b>Install</b>. The first time, Android asks to let this browser install apps.</li>
                 <li>Open the app: it links to Unity on its own.</li>
                 <li>"App not installed" over an older copy means it was signed with another key: uninstall the old app first.</li>
               </ol>
               """;
        var html = $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Matrix Device</title>
            <style>
              body { font-family: system-ui, sans-serif; background: #0b0f0c; color: #d7ffe0; margin: 0; padding: 24px; }
              h1 { color: #00ff66; font-size: 22px; margin: 0 0 16px; }
              .name { font-size: 18px; font-weight: bold; word-break: break-all; }
              .button { display: block; text-align: center; background: #00c853; color: #001a08; font-weight: bold;
                        font-size: 18px; padding: 16px; border-radius: 10px; text-decoration: none; margin: 20px 0; }
              li { margin: 8px 0; line-height: 1.4; }
            </style></head>
            <body><h1>Matrix Device</h1>{{body}}</body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8");
    }

    public static IResult Apk(MatrixOptions options, string token)
    {
        if (!MatrixAuthentication.FixedTimeEquals(token, options.LanToken))
            return Results.StatusCode(StatusCodes.Status401Unauthorized);
        var apk = Newest(options);
        return apk is null
            ? Results.NotFound()
            : Results.File(apk.FullName, "application/vnd.android.package-archive", apk.Name);
    }

    static FileInfo? Newest(MatrixOptions options)
    {
        if (string.IsNullOrEmpty(options.DeviceFolder) || !Directory.Exists(options.DeviceFolder))
            return null;
        return new DirectoryInfo(options.DeviceFolder).GetFiles("*.apk").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
    }
}
